using System.Runtime.InteropServices;

namespace PromptDC;

internal sealed record EnhancementResult(string Original, string Enhanced, bool Replaced);

internal sealed class SelectionEnhancer(InputGuard guard, Action<string>? trace = null)
{
    public async Task<EnhancementResult> RunAsync(Func<string, CancellationToken, Task<string>> enhance,
        Action<string> status, CancellationToken token, Func<CancellationToken, Task<bool>>? validateContext = null)
    {
        var window = Native.GetForegroundWindow();
        var epoch = guard.Epoch;
        // Do not synthesize Ctrl+C until the user's hotkey modifiers have been released.
        var deadline = Environment.TickCount64 + 2500;
        while (Native.ModifiersPressed && Environment.TickCount64 < deadline) await Task.Delay(25, token);
        // WM_HOTKEY can arrive before queued modifier releases are reflected in keyboard state.
        await Task.Delay(75, token);
        if (Native.ModifiersPressed) throw new InvalidOperationException("请松开快捷键后重试。");
        if (!guard.Matches(window, epoch)) throw new InvalidOperationException("输入位置已变化，请重新选中文字后重试。");
        status("正在读取选中文字…");
        var original = await CopySelection(window, epoch, token);
        if (string.IsNullOrWhiteSpace(original)) throw new InvalidOperationException("未读取到选中文字，请选中输入框里的文字后重试。");
        status("正在增强，请保持选区，暂时不要编辑或切换窗口…");
        var enhanced = await enhance(original, token);
        if (!guard.Matches(window, epoch) || Native.ModifiersPressed) return new(original, enhanced, false);
        if (validateContext != null)
        {
            status("增强完成，正在再次核对当前聊天…");
            try { if (!await validateContext(token)) return new(original, enhanced, false); }
            catch (InvalidOperationException) { return new(original, enhanced, false); }
            if (!guard.Matches(window, epoch)) return new(original, enhanced, false);
        }
        string selected;
        try { selected = await CopySelection(window, epoch, token); }
        catch (InvalidOperationException) { return new(original, enhanced, false); }
        if (selected != original || !guard.Matches(window, epoch) || Native.ModifiersPressed)
            return new(original, enhanced, false);

        var previous = CaptureClipboard();
        Clipboard.SetText(enhanced);
        var sequence = Native.GetClipboardSequenceNumber();
        try
        {
            // No await between final validation and queuing paste; never send Enter.
            if (!guard.Matches(window, epoch) || Native.ModifiersPressed) return new(original, enhanced, false);
            Native.Chord(0x11, 0x56);
            // Allow Electron/browser processes time to consume the clipboard before restoring it.
            await Task.Delay(650, CancellationToken.None);
        }
        finally { await RestoreClipboardAsync(previous, sequence); }
        return new(original, enhanced, true);
    }

    private async Task<string> CopySelection(nint window, long epoch, CancellationToken token)
    {
        if (!guard.Matches(window, epoch)) throw new InvalidOperationException("输入位置已变化。");
        var previous = CaptureClipboard();
        var marker = "PromptDC-selection-" + Guid.NewGuid();
        Clipboard.SetText(marker);
        var ownedSequence = Native.GetClipboardSequenceNumber();
        var markerSequence = ownedSequence;
        try
        {
            Native.Chord(0x11, 0x43);
            var deadline = Environment.TickCount64 + 1200;
            while (Environment.TickCount64 < deadline)
            {
                await Task.Delay(25, token);
                if (!guard.Matches(window, epoch)) throw new InvalidOperationException("输入位置已变化。");
                var current = Native.GetClipboardSequenceNumber();
                if (current == markerSequence) continue;
                ownedSequence = current;
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        var text = Clipboard.GetText();
                        if (text != marker) return text;
                    }
                }
                catch (ExternalException e) when (e.ErrorCode == unchecked((int)0x800401D0))
                { trace?.Invoke("copy busy: retrying read"); }
            }
            throw new InvalidOperationException("未读取到选中文字，请确认已选中输入框中的文字。管理员窗口可能无法访问。");
        }
        finally { await RestoreClipboardAsync(previous, ownedSequence); }
    }

    internal static DataObject CaptureClipboard()
    {
        var result = new DataObject();
        var current = Clipboard.GetDataObject();
        if (current != null)
            foreach (var format in current.GetFormats(false))
            {
                var data = current.GetData(format, false);
                if (data != null) result.SetData(format, false, data);
            }
        return result;
    }

    private Task<bool> RestoreClipboardAsync(DataObject previous, uint ownedSequence) =>
        ClipboardRestore.TryAsync(() =>
            {
                if (previous.GetFormats(false).Length == 0) Clipboard.Clear();
                else Clipboard.SetDataObject(previous, true, 0, 0);
            }, () => Native.GetClipboardSequenceNumber() == ownedSequence, trace);
}
