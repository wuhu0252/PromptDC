using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PromptDC;

// A failed local-history lookup may still enhance the verified selection. No history,
// title or project is guessed; the scoped UI fingerprint is only a paste-time guard.
internal static class CodexHistoryFallback
{
    internal static ChatContext Resolve(WindowSnapshot snapshot, string composerIdentity,
        string scopeIdentity, Func<ChatContext> resolve)
    {
        if (!snapshot.ConversationScoped || string.IsNullOrEmpty(composerIdentity) || string.IsNullOrEmpty(scopeIdentity))
            throw new InvalidOperationException("无法确认聊天输入区域，原文未替换。");
        try { return resolve() with { ComposerIdentity = composerIdentity }; }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException
            or JsonException or ArgumentException or KeyNotFoundException or System.Security.SecurityException)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Add("codex-selection-only-v1");
            Add(scopeIdentity);
            // Only transcript text participates. Aggregate group names can contain the
            // editor's draft, while editable/button/navigation descendants are excluded.
            foreach (var text in snapshot.Texts.Where(text => text.Type == "ControlType.Text" && text.Excluded == ""))
                Add(text.Text);
            var fingerprint = Convert.ToHexString(hash.GetHashAndReset());
            return new("codex-selection-only:" + fingerprint, "", "", [], "本次未使用历史，仅增强选中文字")
            {
                HistoryUnavailable = true,
                ComposerIdentity = composerIdentity,
                StorageIdentity = "ui:" + fingerprint
            };

            void Add(string text)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                Span<byte> length = stackalloc byte[sizeof(int)];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
                hash.AppendData(length);
                hash.AppendData(bytes);
            }
        }
    }
}
