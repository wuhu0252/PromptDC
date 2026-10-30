using System.Diagnostics;
using System.Text.Json;

namespace PromptDC;

internal sealed record ChatMessage(string Role, string Text);
internal sealed record ChatContext(string ThreadId, string Title, string Project, List<ChatMessage> Messages, string Evidence)
{
    public string Provider { get; init; } = "codex";
    public string StorageIdentity { get; init; } = "";
    public string ComposerIdentity { get; init; } = "";
    public bool IsNewChat { get; init; }
    public string ProviderName => Provider == "harness" ? "DeepSeek Harness" : "Codex";
    public bool SameConversation(ChatContext other) => Provider == other.Provider && IsNewChat == other.IsNewChat && ThreadId == other.ThreadId
        && string.Equals(StorageIdentity, other.StorageIdentity, StringComparison.OrdinalIgnoreCase)
        && ComposerIdentity == other.ComposerIdentity;
}
internal sealed record ContextRequest(bool RequireFocus = true, string ExpectedComposer = "", bool EditorOnly = false);
internal sealed record ContextReply(bool Success, string Error, ChatContext? Context, int VisibleTexts = 0, MatchDiagnostic? Diagnostic = null,
    string ComposerIdentity = "");
internal sealed class ContextResolutionException(string message, MatchDiagnostic? diagnostic) : InvalidOperationException(message)
{ public MatchDiagnostic? Diagnostic { get; } = diagnostic; }

internal static class ContextWorker
{
    public static string CodexHome => DataDirectories.CodexHome;
    public static string HarnessHome => DataDirectories.HarnessHome;
    internal static bool IsSupportedWindow(nint window) => WindowCapture.IsCodexWindow(window) || HarnessWindow.IsWindow(window);
    public static async Task<string> GetComposerIdentityAsync(nint window, CancellationToken token) =>
        (await RequestAsync(window, new(EditorOnly: true), token)).ComposerIdentity;
    public static async Task<ChatContext> ResolveAsync(nint window, CancellationToken token,
        string expectedComposer = "", bool requireFocus = true)
    {
        var reply = await RequestAsync(window, new(requireFocus, expectedComposer), token);
        return reply.Context ?? throw new InvalidOperationException("当前聊天未返回有效上下文，原文未替换。");
    }
    private static async Task<ContextReply> RequestAsync(nint window, ContextRequest request, CancellationToken token)
    {
        if (!IsSupportedWindow(window))
            throw new InvalidOperationException("自动上下文支持 Codex 和 DeepSeek Harness 官方桌面版本机聊天；当前窗口暂未适配，原文未替换。");
        using var process = new Process { StartInfo = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardInput = true, RedirectStandardError = true
        } };
        // Also support dotnet PromptDC.dll invocation, not just the apphost executable.
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            process.StartInfo.ArgumentList.Add(typeof(ContextWorker).Assembly.Location);
        process.StartInfo.ArgumentList.Add("--context-worker");
        process.StartInfo.ArgumentList.Add(window.ToInt64().ToString());
        process.Start();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var errors = process.StandardError.ReadToEndAsync(deadline.Token);
            // Only focus options and the ephemeral editor identity cross this pipe.
            // The selected draft is needed by the model, not by conversation matching.
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request).AsMemory(), deadline.Token);
            await process.StandardInput.FlushAsync(deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            var reply = JsonSerializer.Deserialize<ContextReply>(await output);
            await errors; // Drain without logging local UI data or runtime diagnostics.
            if (process.ExitCode != 0 || reply?.Success != true || reply.ComposerIdentity.Length == 0)
                throw new ContextResolutionException(reply?.Error ?? "无法确定当前聊天，原文未替换。", reply?.Diagnostic);
            return reply;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new InvalidOperationException("读取当前聊天超过 15 秒，已停止本次增强，原文未替换。"); }
        finally { if (!process.HasExited) process.Kill(true); }
    }
    public static async Task DiagnoseAsync(nint window = 0)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var context = await ResolveAsync(window == 0 ? Native.GetForegroundWindow() : window, CancellationToken.None,
                requireFocus: false);
            SelfTests.WriteReport("context-diagnostic.json", new { Success = true, Error = "", context.ThreadId,
                context.Provider, context.IsNewChat, MessageCount = context.Messages.Count, context.Evidence, ElapsedMilliseconds = timer.ElapsedMilliseconds });
        }
        catch (Exception e)
        {
            SelfTests.WriteReport("context-diagnostic.json", new { Success = false,
                Error = e is InvalidOperationException ? e.Message : "定位失败，未读取或发送模型请求。", ElapsedMilliseconds = timer.ElapsedMilliseconds,
                Diagnostic = (e as ContextResolutionException)?.Diagnostic });
            Environment.ExitCode = 1;
        }
    }
    public static void Run(string handle)
    {
        ContextReply reply;
        MatchDiagnostic? diagnostic = null;
        try
        {
            var window = new nint(long.Parse(handle));
            if (!IsSupportedWindow(window)) throw new InvalidOperationException("当前窗口不是已支持的 Codex 或 DeepSeek Harness 窗口。");
            var request = JsonSerializer.Deserialize<ContextRequest>(Console.In.ReadToEnd())
                ?? throw new InvalidOperationException("无效的上下文请求，原文未替换。");
            var provider = HarnessWindow.IsWindow(window) ? "harness" : "codex";
            var target = ComposerTarget.Locate(window, provider, request.RequireFocus);
            if (request.ExpectedComposer.Length > 0 && target.Identity != request.ExpectedComposer)
                throw new InvalidOperationException("聊天输入框已变化，原文未替换。");
            if (request.EditorOnly)
            {
                reply = new(true, "", null, ComposerIdentity: target.Identity);
            }
            else if (provider == "codex" && target.IsNewChat)
            {
                // The verified home composer is an unsent draft, not a stored thread.
                // Never use another conversation in the same project as its background.
                var context = new ChatContext("codex-draft:" + target.Identity, "", "", [], "已确认 Codex 新聊天页，暂无历史")
                    { IsNewChat = true, ComposerIdentity = target.Identity };
                reply = new(true, "", context, ComposerIdentity: target.Identity);
            }
            else if (provider == "harness")
            {
                var snapshot = HarnessWindow.Capture(window, target);
                reply = new(true, "", HarnessContext.Resolve(snapshot, HarnessHome) with { ComposerIdentity = target.Identity },
                    snapshot.Fragments.Count, ComposerIdentity: target.Identity);
            }
            else
            {
                var snapshot = WindowCapture.Capture(window, target);
                reply = new(true, "", CodexContext.Resolve(snapshot, CodexHome, value => diagnostic = value)
                    with { ComposerIdentity = target.Identity }, snapshot.Texts.Count, diagnostic, target.Identity);
            }
        }
        catch (InvalidOperationException e) { reply = new(false, e.Message, null, Diagnostic: diagnostic); }
        catch { reply = new(false, "无法读取当前聊天的本机记录，原文未替换。", null); }
        Console.Write(JsonSerializer.Serialize(reply));
        Environment.ExitCode = reply.Success ? 0 : 1;
    }
}
