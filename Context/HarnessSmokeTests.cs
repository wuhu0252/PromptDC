using System.Net;
using System.Text.Json;

namespace PromptDC;

// Opt-in integration check in the real Harness composer. It accepts only this exact
// synthetic input and uses a fake HTTP handler: no personal Key or network request.
internal sealed class HarnessSmokeTests : NativeWindow, IDisposable
{
    internal const string Input = "PromptDC 本机适配测试：请结合当前聊天说明项目位置与启动步骤。";
    internal const string Output = "请结合当前对话及已有项目资料，说明项目的位置与启动步骤，并核对实际入口。";
    private readonly InputGuard guard = new();
    private readonly System.Windows.Forms.Timer deadline = new() { Interval = 60000 };
    private readonly nint target;
    private bool busy, finished;

    private HarnessSmokeTests(nint window)
    {
        target = window;
        CreateHandle(new CreateParams { Caption = "PromptDC Harness integration test" });
        var hotkey = Native.ParseHotkey("Ctrl+Alt+9");
        if (!HarnessWindow.IsWindow(target) || !Native.RegisterHotKey(Handle, Native.HotkeyId, hotkey.Modifiers, hotkey.Key))
        {
            Dispose();
            throw new InvalidOperationException("需要 Harness 官方桌面窗口且测试快捷键 Ctrl+Alt+9 未被占用。");
        }
        deadline.Tick += (_, _) => Finish(false, "测试等待快捷键超时，未执行模型请求。", 0);
        deadline.Start();
    }

    internal static void Run(nint window)
    {
        try { using var test = new HarnessSmokeTests(window); Application.Run(); }
        catch (Exception e)
        {
            SelfTests.WriteReport("harness-smoke-tests.json", new { Passed = false,
                Error = e is InvalidOperationException ? e.Message : "无法启动本机 Harness 测试。", NetworkUsed = false });
            Environment.ExitCode = 1;
        }
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x312 && (int)message.WParam == Native.HotkeyId && !busy && !finished) _ = ExecuteAsync();
        base.WndProc(ref message);
    }
    private async Task ExecuteAsync()
    {
        busy = true;
        deadline.Stop();
        ChatContext? context = null;
        try
        {
            if (Native.GetForegroundWindow() != target) throw new InvalidOperationException("测试目标不在前台。");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var http = new HttpClient(new FakeModel());
            var client = new DeepSeekClient(http);
            var epoch = guard.Epoch;
            var composer = await ContextWorker.GetComposerIdentityAsync(target, timeout.Token);
            if (!guard.Matches(target, epoch)) throw new InvalidOperationException("输入框预检查期间焦点已变化。");
            var result = await new SelectionEnhancer(guard).RunAsync(async (text, token) =>
            {
                if (text != Input) throw new InvalidOperationException("选区不是固定测试文字，原文未替换。");
                context = await ContextWorker.ResolveAsync(target, token, composer);
                if (context.Provider != "harness") throw new InvalidOperationException("错误的上下文适配器。");
                return await client.EnhanceAsync(text, new Settings { ProtectedKey = Secret.Protect("test-only") }, token, context);
            }, _ => { }, timeout.Token, async token =>
                context != null && context.SameConversation(await ContextWorker.ResolveAsync(target, token, composer)));
            Finish(result.Replaced && result.Enhanced == Output, result.Replaced ? "" : "位置变化，结果未自动替换。", context?.Messages.Count ?? 0);
        }
        catch (Exception e) { Finish(false, e is InvalidOperationException ? e.Message : "本机输入流程检查失败。", context?.Messages.Count ?? 0); }
    }
    private void Finish(bool passed, string error, int count)
    {
        if (finished) return;
        finished = true;
        deadline.Stop();
        SelfTests.WriteReport("harness-smoke-tests.json", new { Passed = passed, Error = error,
            ContextMessages = count, NetworkUsed = false, KeySource = "synthetic test-only credential" });
        Environment.ExitCode = passed ? 0 : 1;
        Application.ExitThread();
    }
    private sealed class FakeModel : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var messages = body.RootElement.GetProperty("messages");
            SelfTests.Assert(request.RequestUri?.ToString() == DeepSeekClient.Endpoint && messages.GetArrayLength() == 3
                && messages[2].GetProperty("content").GetString() == Input, "wrong model request");
            using var background = JsonDocument.Parse(messages[1].GetProperty("content").GetString()!);
            SelfTests.Assert(background.RootElement.GetProperty("messages").GetArrayLength() >= 2
                && background.RootElement.GetProperty("workspace").GetProperty("source").GetString()!.StartsWith("DeepSeek Harness"), "missing Harness background");
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
                { choices = new[] { new { finish_reason = "stop", message = new { content = Output } } } })) };
        }
    }
    public void Dispose()
    {
        deadline.Dispose(); Native.UnregisterHotKey(Handle, Native.HotkeyId); guard.Dispose(); DestroyHandle();
    }
}
