using System.Text;
using System.Text.RegularExpressions;

namespace PromptDC;

// Provider-independent text matching, context budgets and credential redaction.
internal static class ChatText
{
    internal static string Normalize(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormKC))
            if (char.IsLetterOrDigit(c)) result.Append(char.ToUpperInvariant(c));
        return result.ToString();
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
