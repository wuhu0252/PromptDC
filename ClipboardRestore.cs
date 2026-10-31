using System.Runtime.InteropServices;

namespace PromptDC;

internal static class ClipboardRestore
{
    internal static async Task<bool> TryAsync(Action restore, Func<bool> ownsClipboard,
        Action<string>? trace = null, Func<Task>? delay = null)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (!ownsClipboard()) { trace?.Invoke("restore skipped: sequence changed"); return false; }
            try { restore(); trace?.Invoke("restore succeeded on attempt " + attempt); return true; }
            catch (ExternalException e) when (e.ErrorCode == unchecked((int)0x800401D0))
            {
                trace?.Invoke("restore busy on attempt " + attempt);
                if (attempt == 19) return false;
                await (delay?.Invoke() ?? Task.Delay(50));
            }
        }
        return false;
    }
}
