namespace PromptDC;

// Native integration test: real global hotkey -> Ctrl+C -> fake model -> Ctrl+V.
// Runs only in its own test window; never touches another app's text fields.
internal sealed class SmokeTests : Form
{
    private readonly RichTextBox editor = new() { Dock = DockStyle.Fill };
    private readonly TextBox otherEditor = new() { Dock = DockStyle.Top, Text = "前缀 优化这段代码 后缀" };
    private readonly TextBox search = new() { Dock = DockStyle.Top, Text = "另一个搜索输入框" };
    private readonly InputGuard guard = new();
    private readonly List<object> results = [];
    private readonly List<string> clipboardTrace = [];
    private Func<Task>? hotkeyAction;
    private SmokeTests()
    {
        Text = "PromptDC 本机选区测试"; ClientSize = new Size(620, 260);
        StartPosition = FormStartPosition.CenterScreen; Controls.Add(editor);
        Controls.Add(otherEditor); Controls.Add(search);
        Shown += async (_, _) => await Execute();
    }
    public static void Run() { using var form = new SmokeTests(); Application.Run(form); }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312 && (int)m.WParam == Native.HotkeyId && hotkeyAction != null) _ = hotkeyAction();
        base.WndProc(ref m);
    }
    private async Task Execute()
    {
        DataObject? backup = null;
        try
        {
            backup = SelectionEnhancer.CaptureClipboard();
            await LoginUiTests.RunAsync(Check);
            Activate(); Native.SetForegroundWindow(Handle);
            var focusDeadline = Environment.TickCount64 + 60000;
            while (Native.GetForegroundWindow() != Handle && Environment.TickCount64 < focusDeadline)
                await Task.Delay(150);
            var parsed = Native.ParseHotkey("Ctrl+Alt+9");
            SelfTests.Assert(Native.RegisterHotKey(Handle, Native.HotkeyId, parsed.Modifiers, parsed.Key), "hotkey already occupied");
            await Check("global hotkey replaces only selected Unicode text and restores clipboard", async () =>
            {
                await Prepare();
                Clipboard.SetText("clipboard-before-test");
                var completion = new TaskCompletionSource<EnhancementResult>();
                hotkeyAction = async () =>
                {
                    try { completion.SetResult(await new SelectionEnhancer(guard, clipboardTrace.Add).RunAsync(async (text, token) => { SelfTests.Assert(text == "优化这段代码", "wrong selection"); await Task.Delay(100, token); return "请优化这段代码，保持现有行为。"; }, _ => { }, default)); }
                    catch (Exception e) { completion.SetException(e); }
                };
                Native.Chord(0x11, 0x12, 0x39);
                var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));
                SelfTests.Assert(result.Replaced && editor.Text == "前缀 请优化这段代码，保持现有行为。 后缀", "wrong replacement");
                SelfTests.Assert(Clipboard.GetText() == "clipboard-before-test", "clipboard not restored");
                editor.Undo();
                SelfTests.Assert(editor.Text == "前缀 优化这段代码 后缀", "undo failed");
            });
            await Check("changed selection retains result without replacing", async () =>
            {
                await Prepare();
                var result = await new SelectionEnhancer(guard, clipboardTrace.Add).RunAsync(async (_, token) => { editor.Select(0, 2); await Task.Delay(100, token); return "增强内容"; }, _ => { }, default);
                SelfTests.Assert(!result.Replaced && result.Enhanced == "增强内容" && editor.Text == "前缀 优化这段代码 后缀", "stale selection overwritten");
            });
            await Check("focus switch retains result without replacing", async () =>
            {
                await Prepare();
                using var other = new Form { Text = "PromptDC test focus target", Size = new Size(300, 120) };
                var result = await new SelectionEnhancer(guard, clipboardTrace.Add).RunAsync(async (_, token) => { other.Show(); other.Activate(); await Task.Delay(150, token); return "增强内容"; }, _ => { }, default);
                SelfTests.Assert(!result.Replaced && editor.Text == "前缀 优化这段代码 后缀", "focus guard failed");
            });
            await Check("API failure preserves source and clipboard", async () =>
            {
                await Prepare(); Clipboard.SetText("failure-clipboard");
                try { await new SelectionEnhancer(guard, clipboardTrace.Add).RunAsync((_, _) => throw new InvalidOperationException("mock API failure"), _ => { }, default); throw new Exception("failure not propagated"); }
                catch (InvalidOperationException e) { SelfTests.Assert(e.Message == "mock API failure", "unexpected error"); }
                SelfTests.Assert(editor.Text == "前缀 优化这段代码 后缀" && Clipboard.GetText() == "failure-clipboard", "source or clipboard lost");
            });
            await Check("conversation change during request retains result without pasting", async () =>
            {
                await Prepare(); Clipboard.SetText("context-clipboard");
                var result = await new SelectionEnhancer(guard, clipboardTrace.Add).RunAsync((_, _) => Task.FromResult("增强结果"), _ => { }, default,
                    _ => Task.FromResult(false));
                SelfTests.Assert(!result.Replaced && result.Enhanced == "增强结果" && editor.Text == "前缀 优化这段代码 后缀"
                    && Clipboard.GetText() == "context-clipboard", "changed conversation overwritten");
            });
            await Check("context revalidation failure retains generated result without pasting", async () =>
            {
                await Prepare();
                var result = await new SelectionEnhancer(guard, clipboardTrace.Add).RunAsync((_, _) => Task.FromResult("增强结果"), _ => { }, default,
                    _ => throw new InvalidOperationException("mock verification failure"));
                SelfTests.Assert(!result.Replaced && result.Enhanced == "增强结果" && editor.Text == "前缀 优化这段代码 后缀", "verification failure changed source");
            });
            await Check("programmatic focus change to an identical input in the same restored window prevents paste", async () =>
            {
                await Prepare();
                var result = await new SelectionEnhancer(guard).RunAsync(async (_, token) =>
                {
                    SelfTests.Assert(otherEditor.Focus(), "test setup could not focus the second input");
                    otherEditor.Select(3, "优化这段代码".Length);
                    await Task.Delay(180, token); return "不应粘贴";
                }, _ => { }, default);
                SelfTests.Assert(!result.Replaced && otherEditor.Text == "前缀 优化这段代码 后缀"
                    && editor.Text == "前缀 优化这段代码 后缀", "same-window focus swap pasted to another field");
            });
            await Check("focus roundtrip between same-window fields remains invalid even after selection returns", async () =>
            {
                await Prepare();
                var result = await new SelectionEnhancer(guard).RunAsync(async (_, token) =>
                {
                    SelfTests.Assert(otherEditor.Focus(), "test setup could not focus the second input"); await Task.Delay(150, token);
                    SelfTests.Assert(editor.Focus(), "test setup could not restore focus"); editor.Select(3, "优化这段代码".Length);
                    await Task.Delay(150, token); return "不应粘贴";
                }, _ => { }, default);
                SelfTests.Assert(!result.Replaced && editor.Text == "前缀 优化这段代码 后缀", "focus roundtrip was ignored");
            });
        }
        catch (Exception e) { results.Add(new { name = "setup", passed = false, error = e.ToString() }); Environment.ExitCode = 1; }
        finally
        {
            try
            {
                Native.UnregisterHotKey(Handle, Native.HotkeyId); guard.Dispose();
                if (backup != null)
                {
                    var sequence = Native.GetClipboardSequenceNumber();
                    await Check("test cleanup restores clipboard without an unhandled exception", async () =>
                    {
                        var restored = await ClipboardRestore.TryAsync(() =>
                        {
                            if (backup.GetFormats(false).Length == 0) Clipboard.Clear();
                            else Clipboard.SetDataObject(backup, true, 0, 0);
                        }, () => Native.GetClipboardSequenceNumber() == sequence, clipboardTrace.Add);
                        SelfTests.Assert(restored, "clipboard restoration did not complete; clipboard busy or changed");
                    });
                }
            }
            catch (Exception e) { results.Add(new { name = "cleanup", passed = false, error = e.ToString() }); Environment.ExitCode = 1; }
            finally
            {
                try { SelfTests.WriteReport("smoke-tests.json", results); }
                catch { Environment.ExitCode = 1; Console.Error.WriteLine("无法写入桌面测试报告。"); }
                finally { Close(); }
            }
        }
    }
    private async Task Prepare()
    {
        Show(); WindowState = FormWindowState.Normal; Activate(); Native.SetForegroundWindow(Handle);
        SelfTests.Assert(editor.Focus(), "test setup could not focus the editor");
        editor.Text = "前缀 优化这段代码 后缀"; editor.Select(3, "优化这段代码".Length);
        await Task.Delay(220);
        SelfTests.Assert(Native.GetForegroundWindow() == Handle, "test window not in foreground");
    }
    private async Task Check(string name, Func<Task> test)
    {
        clipboardTrace.Clear();
        try { await test(); results.Add(new { name, passed = true, error = "" }); }
        catch (Exception e) { results.Add(new { name, passed = false, error = e + "\n" + string.Join("; ", clipboardTrace) }); Environment.ExitCode = 1; }
    }
}
