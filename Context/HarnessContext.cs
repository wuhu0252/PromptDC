using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZstdSharp;

namespace PromptDC;

internal static class HarnessContext
{
    private const long MaxFileBytes = 32 * 1024 * 1024;
    private const long MaxTotalBytes = 64 * 1024 * 1024;
    internal sealed record LogChat(string Id, string Title, string Project, string File, List<ChatMessage> Messages);

    internal static ChatContext Resolve(HarnessSnapshot snapshot, string home)
    {
        if (!snapshot.ConversationScoped) throw Fail("未获取可信的 Harness 对话区域");
        // Fragments come only from the verified transcript body; HarnessWindow excludes
        // editable ancestors. Identical historical text remains valid evidence, even when
        // the user selects that same greeting, quote or previous prompt for enhancement.
        var anchors = snapshot.Fragments.Select(x => new ChatMessage(x.Role, ChatText.Normalize(x.Text)))
            .Where(x => x.Role is "user" or "assistant" && x.Text.Length >= (x.Role == "user" ? 2 : 8) && x.Text.Length <= 6000)
            .Distinct().TakeLast(500).ToList();
        if (!anchors.Any(x => x.Role == "user") || anchors.Where(x => x.Role == "assistant").Sum(x => x.Text.Length) < 32)
            throw Fail("Harness 聊天区缺少足够的用户消息和助手回复证据");
        var files = FindLogs(home);
        var chats = new List<LogChat>();
        long total = 0;
        foreach (var file in files)
        {
            var bytes = ReadLog(file);
            total += bytes.Length;
            if (total > MaxTotalBytes) throw Fail("本次 Harness 记录核对超过 64 MB");
            chats.Add(ParseLog(bytes, file));
        }
        if (chats.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != chats.Count)
            throw Fail("Harness 记录中存在重复会话标识");
        var normalized = chats.Select(chat => new
        {
            Chat = chat,
            Messages = chat.Messages.Select(x => new ChatMessage(x.Role, ChatText.Normalize(x.Text))).ToList()
        }).ToList();
        bool Supports(List<ChatMessage> messages, ChatMessage anchor) => messages.Any(x => x.Role == anchor.Role
            && x.Text.Contains(anchor.Text, StringComparison.Ordinal));
        var matches = normalized.Where(chat => anchors.Any(x => x.Role == "user" && Supports(chat.Messages, x))
            && chat.Messages.Where(x => x.Role == "assistant").Any(message => anchors.Where(x => x.Role == "assistant"
                && message.Text.Contains(x.Text, StringComparison.Ordinal)).Sum(x => x.Text.Length) >= 32)).ToList();
        // Use all exact supported evidence, not recency or a highest-score guess. A fork's
        // distinguishing message can disambiguate its shared prefix; contradictions refuse it.
        foreach (var anchor in anchors)
        {
            if (!normalized.Any(chat => Supports(chat.Messages, anchor))) continue;
            matches.RemoveAll(chat => !Supports(chat.Messages, anchor));
        }
        if (matches.Count != 1) throw Fail(matches.Count == 0
            ? "当前 Harness 界面与本机记录未形成唯一匹配" : "多个 Harness 聊天包含相同内容，无法区分当前会话");
        var match = matches[0].Chat;
        return new(match.Id, match.Title, match.Project, ChatText.LimitContext(match.Messages), "Harness 用户消息与助手最终回复精确匹配")
        { Provider = "harness", StorageIdentity = Path.GetFullPath(match.File) };
    }

    internal static List<string> FindLogs(string home)
    {
        var root = Path.GetFullPath(Path.Combine(home, "sessions"));
        RejectLinks(home); RejectLinks(root);
        if (!Directory.Exists(root)) throw Fail("未找到 Harness 本机会话目录");
        var files = new List<string>();
        var visited = 0;
        foreach (var project in Directory.EnumerateDirectories(root))
        {
            RejectLinks(project);
            foreach (var session in Directory.EnumerateDirectories(project))
            {
                if (++visited > 200) throw Fail("Harness 待核对会话超过 200 个");
                RejectLinks(session);
                var generations = new List<(int Version, string File)>();
                foreach (var file in Directory.EnumerateFiles(session))
                {
                    var name = Path.GetFileName(file);
                    var match = Regex.Match(name, @"^session(?:\.v([1-9][0-9]*))?\.jsonl(?:\.zstd)?$");
                    if (!match.Success) continue;
                    RejectLinks(file);
                    if (!int.TryParse(match.Groups[1].Success ? match.Groups[1].Value : "0", out var version))
                        throw Fail("Harness 记录版本超出支持范围");
                    generations.Add((version, file));
                }
                if (generations.Count == 0) throw Fail("Harness 会话目录中缺少可核对的记录");
                var latest = generations.Where(x => x.Version == generations.Max(y => y.Version)).ToList();
                if (latest.Count != 1 || latest[0].Version != 4)
                    throw Fail("Harness 记录格式尚未支持或存在多个当前代文件");
                files.Add(latest[0].File);
            }
        }
        if (files.Count == 0) throw Fail("没有可核对的 Harness 会话");
        return files;
    }

    internal static byte[] ReadLog(string file)
    {
        RejectLinks(file);
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (input.Length > MaxFileBytes) throw Fail("Harness 单份记录超过 32 MB");
        try
        {
            using var decoded = file.EndsWith(".zstd", StringComparison.Ordinal)
                ? (Stream)new DecompressionStream(input) : input;
            if (decoded is DecompressionStream zstd)
                zstd.SetParameter(ZstdSharp.Unsafe.ZSTD_dParameter.ZSTD_d_windowLogMax, 25);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = decoded.Read(buffer)) != 0)
            {
                if (output.Length + count > MaxFileBytes) throw Fail("Harness 解压后记录超过 32 MB");
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        catch (Exception e) when (e is ZstdException or EndOfStreamException)
        { throw Fail("Harness 压缩记录未完整写入或已损坏，请稍后重试"); }
    }

    internal static LogChat ParseLog(byte[] bytes, string file)
    {
        try { return Parse(new UTF8Encoding(false, true).GetString(bytes), file); }
        catch (Exception e) when (e is JsonException or DecoderFallbackException or KeyNotFoundException)
        { throw Fail("Harness 记录不完整或结构不受支持，请稍后重试"); }
    }

    private static LogChat Parse(string text, string file)
    {
        using var reader = new StringReader(text);
        var first = reader.ReadLine() ?? throw Fail("Harness 记录为空");
        using var header = JsonDocument.Parse(first);
        var h = header.RootElement;
        if (h.GetProperty("type").GetString() != "session" || h.GetProperty("version").GetInt32() != 4)
            throw Fail("Harness 记录不是已支持的 v4 格式");
        var id = h.GetProperty("id").GetString() ?? "";
        if (!Regex.IsMatch(id, @"^[A-Za-z0-9_-]{1,200}$") || Path.GetFileName(Path.GetDirectoryName(file)) != id)
            throw Fail("Harness 会话目录与记录标识不一致");
        var cwd = h.TryGetProperty("cwd", out var cwdValue) ? cwdValue.GetString() ?? "" : "";
        var title = "";
        var messages = new List<(int Order, ChatMessage Message)>();
        var pending = new Dictionary<int, (int Order, ChatMessage Message)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sequence = -1;
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) throw Fail("Harness 记录包含空白事件");
            using var record = JsonDocument.Parse(line);
            var row = record.RootElement;
            var seq = row.GetProperty("seq").GetInt32();
            if (seq <= sequence) throw Fail("Harness 事件顺序不受支持");
            sequence = seq;
            var type = row.GetProperty("type").GetString();
            var data = row.GetProperty("data");
            // A history rewrite cannot be silently interpreted as an append-only transcript.
            if (row.TryGetProperty("surfaceOp", out var op) && op.GetString() != "append")
                throw Fail("Harness 记录包含尚未支持的历史改写操作");
            if (type == "session/title") { title = data.GetProperty("title").GetString() ?? ""; continue; }
            if (type == "user/message" && data.GetProperty("source").GetProperty("kind").GetString() == "user"
                && data.GetProperty("role").GetString() == "user")
            {
                var content = TextContent(data.GetProperty("content"));
                if (content.Length == 0) continue;
                if (!seen.Add(data.GetProperty("id").GetString() ?? "")) throw Fail("Harness 消息标识重复");
                messages.Add((seq, new("user", content)));
            }
            else if (type == "assistant/message")
            {
                var turn = data.GetProperty("turn").GetInt32();
                var message = data.GetProperty("message");
                var blocks = message.GetProperty("content");
                // Tool requests and intermediate turns are not final visible replies. Clear a
                // previous candidate when the last assistant step contains calls or no text.
                pending.Remove(turn);
                if (message.GetProperty("role").GetString() != "assistant" || blocks.EnumerateArray().Any(x =>
                    x.GetProperty("type").GetString() is not ("text" or "reasoning"))) continue;
                var content = TextContent(blocks);
                if (content.Length > 0) pending[turn] = (seq, new("assistant", content));
            }
            else if (type == "turn/end")
            {
                var turn = data.GetProperty("turn").GetInt32();
                if (pending.Remove(turn, out var reply) && data.GetProperty("reason").GetProperty("kind").GetString() == "completed")
                    messages.Add(reply);
            }
        }
        return new(id, title, cwd, file, messages.OrderBy(x => x.Order).Select(x => x.Message).ToList());
    }

    private static string TextContent(JsonElement blocks) => string.Join("\n", blocks.EnumerateArray()
        .Where(x => x.GetProperty("type").GetString() == "text")
        .Select(x => x.GetProperty("text").GetString())).Trim();

    private static void RejectLinks(string path)
    {
        // Check every ancestor too, so a redirected DSH_HOME cannot escape the intended tree.
        for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw Fail("Harness 记录路径包含链接或重定向目录，已停止读取");
    }
    private static InvalidOperationException Fail(string reason) => new(reason + "，原文未替换。");
}
