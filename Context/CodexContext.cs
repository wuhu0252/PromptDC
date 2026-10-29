using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PromptDC;

internal sealed record StoredChat(string Id, string Title, string Project, List<ChatMessage> Messages);
internal sealed record MatchCount(string ThreadId, int User, int Assistant);
internal sealed record MatchDiagnostic(int Texts, int Anchors, List<int> AnchorLengths, List<MatchCount> Candidates);

internal static class CodexContext
{
    private const int MaxChats = 200;
    public static ChatContext Resolve(WindowSnapshot snapshot, string home, string original, Action<MatchDiagnostic>? trace = null)
    {
        var anchors = GetAnchors(snapshot, original);
        if (anchors.Count < 2)
            throw new InvalidOperationException("Codex 未提供足够的聊天区文字，无法可靠定位当前聊天，原文未替换。");
        var index = Directory.EnumerateFiles(home, "state_*.sqlite", SearchOption.TopDirectoryOnly)
            .OrderByDescending(p => int.TryParse(Path.GetFileNameWithoutExtension(p).Replace("state_", ""), out var version) ? version : 0).FirstOrDefault();
        if (index == null) throw new InvalidOperationException("未找到 Codex 本机聊天索引，云端或远程聊天暂不支持。");
        using var database = new LocalSqlite(index);
        var rows = database.Query("SELECT id, title, cwd, rollout_path FROM threads WHERE archived = 0 AND source IN ('vscode', 'cli') LIMIT 201");
        if (rows.Count > MaxChats) throw new InvalidOperationException("本机待核对聊天过多，暂时无法可靠定位，原文未替换。");
        var chats = new List<StoredChat>();
        long scannedBytes = 0;
        foreach (var row in rows)
        {
            var file = ValidateRolloutPath(home, row[3]);
            scannedBytes += new FileInfo(file).Length;
            if (scannedBytes > 64 * 1024 * 1024)
                throw new InvalidOperationException("本次聊天核对数据超过 64 MB，原文未替换。");
            // An unreadable candidate must not silently turn an ambiguous match into a unique one.
            var chat = new StoredChat(row[0], row[1], row[2], ReadMessages(file));
            chats.Add(chat);
        }
        var matches = FindMatches(chats, anchors);
        trace?.Invoke(new(snapshot.Texts.Count, anchors.Count, anchors.Select(x => x.Length).ToList(), chats.Select(chat =>
        {
            var score = Match(chat, anchors);
            return new MatchCount(chat.Id, score.User, score.Assistant);
        }).ToList()));
        if (matches.Count != 1)
            throw new InvalidOperationException(matches.Count == 0
                ? "当前界面与本机聊天记录未形成唯一匹配，原文未替换。"
                : "多个聊天包含相同的可见内容，无法确定当前聊天，原文未替换。");
        var match = matches[0];
        return new(match.Chat.Id, match.Chat.Title, match.Chat.Project, LimitContext(match.Chat.Messages),
            $"核对了 {match.User} 条用户消息和 {match.Assistant} 条助手最终回复");
    }

    internal static List<string> GetAnchors(WindowSnapshot snapshot, string original)
    {
        if (!snapshot.ConversationScoped) return [];
        var draft = Normalize(original);
        return snapshot.Texts.Where(x => x.Excluded == "" && x.Type == "ControlType.Text" && ((x.Width > 0 && x.Height > 0) || x.Offscreen))
            .Select(x => Normalize(x.Text)).Where(x => x.Length >= 6 && x.Length <= 6000)
            .Where(x => draft.Length == 0 || (!x.Contains(draft, StringComparison.Ordinal) && !draft.Contains(x, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal).TakeLast(500).ToList();
    }
    private static List<(StoredChat Chat, int User, int Assistant)> FindMatches(List<StoredChat> chats, List<string> anchors)
    {
        var normalized = chats.ToDictionary(chat => chat.Id,
            chat => chat.Messages.Select(message => (message.Role, Text: Normalize(message.Text))).ToList());
        var matches = chats.Select(chat => (Chat: chat, Match: Match(chat, anchors)))
            .Where(x => x.Match.User > 0 && x.Match.Assistant > 0)
            .Select(x => (x.Chat, x.Match.User, x.Match.Assistant)).ToList();
        if (matches.Count <= 1) return matches;
        // Intersect exact message evidence. Shared fork prefixes do not outweigh a later distinguishing message.
        foreach (var anchor in anchors)
        {
            // Short UI labels cannot decide between related forks. Only compare candidates with both message roles.
            if (anchor.Length < 24) continue;
            var candidateIds = matches.Select(x => x.Chat.Id).ToHashSet(StringComparer.Ordinal);
            var supporting = normalized.Where(chat => candidateIds.Contains(chat.Key) && chat.Value.Any(message =>
                anchor.Length >= (message.Role == "assistant" ? 32 : 6) && message.Text.Contains(anchor, StringComparison.Ordinal)))
                .Select(chat => chat.Key).ToHashSet(StringComparer.Ordinal);
            if (supporting.Count == 0) continue; // A UI label or a still-streaming progress message is not history evidence.
            matches.RemoveAll(match => !supporting.Contains(match.Chat.Id));
            if (matches.Count == 0) break;
        }
        return matches;
    }
    internal static (int User, int Assistant) Match(StoredChat chat, IReadOnlyList<string> anchors)
    {
        var user = 0; var assistant = 0;
        foreach (var message in chat.Messages)
        {
            var text = Normalize(message.Text);
            // Whole visible text fragments must match, not a fuzzy title or timestamp score.
            if (!anchors.Any(anchor => anchor.Length >= (message.Role == "assistant" ? 32 : 6) && text.Contains(anchor, StringComparison.Ordinal))) continue;
            if (message.Role == "user") user++; else if (message.Role == "assistant") assistant++;
        }
        return (user, assistant);
    }
    internal static string Normalize(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormKC))
            if (char.IsLetterOrDigit(c)) result.Append(char.ToUpperInvariant(c));
        return result.ToString();
    }
    internal static string ValidateRolloutPath(string home, string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        path = Path.GetFullPath(path);
        var allowed = new[] { "sessions", "archived_sessions" }.Any(folder =>
            path.StartsWith(Path.GetFullPath(Path.Combine(home, folder)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (!allowed || !path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new InvalidOperationException("聊天索引中存在不可读取的本机记录，已停止定位，原文未替换。");
        return path;
    }
    internal static List<ChatMessage> ReadMessages(string path)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024)
            throw new InvalidOperationException("聊天记录超过当前读取上限，原文未替换。");
        var messages = new List<ChatMessage>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            // A running Codex may be writing the last JSONL line. Ignore only that incomplete tail.
            JsonDocument record;
            try { record = JsonDocument.Parse(line); }
            catch (JsonException)
            {
                if (reader.EndOfStream && !line.TrimEnd().EndsWith('}')) break;
                throw new InvalidOperationException("聊天记录包含无法解析的内容，原文未替换。");
            }
            using (record)
            {
                var root = record.RootElement;
                if (!root.TryGetProperty("type", out var kind) || kind.GetString() != "response_item") continue;
                if (!root.TryGetProperty("payload", out var payload) || !payload.TryGetProperty("type", out var type) || type.GetString() != "message") continue;
                var role = payload.TryGetProperty("role", out var roleValue) ? roleValue.GetString() : null;
                if (role is not ("user" or "assistant")) continue;
                var phase = payload.TryGetProperty("phase", out var phaseValue) ? phaseValue.GetString() : null;
                var channel = payload.TryGetProperty("channel", out var channelValue) ? channelValue.GetString() : null;
                if (role == "assistant" && (phase is not (null or "final_answer") || channel is not (null or "final"))) continue;
                if (!payload.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
                var pieces = new List<string>();
                foreach (var piece in content.EnumerateArray())
                    if (piece.TryGetProperty("type", out var pieceType) && pieceType.GetString() is "input_text" or "output_text"
                        && piece.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) pieces.Add(text.GetString()!);
                var cleaned = CleanMessage(string.Join("\n", pieces), role == "user");
                if (cleaned.Length != 0) messages.Add(new(role, cleaned));
            }
        }
        return messages;
    }
    internal static string CleanMessage(string text, bool user)
    {
        if (!user) return Regex.Replace(text, @"<oai-mem-citation>.*?</oai-mem-citation>", "", RegexOptions.Singleline).Trim();
        var marker = text.IndexOf("## My request:", StringComparison.Ordinal);
        if (marker >= 0) return text[(marker + "## My request:".Length)..].Trim();
        text = text.Trim();
        if (text.StartsWith("<send_user_message_question_reply>", StringComparison.Ordinal))
        {
            var body = text.Replace("<send_user_message_question_reply>", "").Replace("</send_user_message_question_reply>", "");
            try
            {
                using var answers = JsonDocument.Parse(body);
                return string.Join("\n", answers.RootElement.EnumerateArray().Select(answer =>
                    $"{answer.GetProperty("question").GetString()}：{answer.GetProperty("answer").GetString()}"));
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { return ""; }
        }
        if (new[] { "<environment_context>", "<external_codex_apps_open_page>", "<permissions", "<skills_instructions>", "# AGENTS.md instructions" }.Any(prefix => text.StartsWith(prefix, StringComparison.Ordinal))) return "";
        return text;
    }
    internal static List<ChatMessage> LimitContext(IReadOnlyList<ChatMessage> source, int budget = 20000)
    {
        var selected = new List<ChatMessage>();
        var remaining = budget;
        // Keep recent actual messages; prioritize the latest statements over old assumptions.
        for (var i = source.Count - 1; i >= 0 && selected.Count < 20 && remaining > 0; i--)
        {
            var text = RedactSecrets(source[i].Text);
            var take = Math.Min(Math.Min(text.Length, 5000), remaining);
            if (take < text.Length)
            {
                const string marker = "\n[本条历史消息已截断]";
                if (take <= marker.Length) break;
                var end = take - marker.Length;
                if (char.IsHighSurrogate(text[end - 1])) end--;
                text = text[..end] + marker;
            }
            selected.Add(new(source[i].Role, text)); remaining -= text.Length;
        }
        selected.Reverse();
        return selected;
    }
    internal static string RedactSecrets(string text) => Regex.Replace(text,
        @"\b(?:sk-[A-Za-z0-9_-]{12,}|gh[pousr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[A-Z0-9]{16})\b", "[已隐藏凭据]");
}
