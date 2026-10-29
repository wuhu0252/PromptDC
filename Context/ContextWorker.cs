using System.Diagnostics;
using System.Text.Json;

namespace PromptDC;

internal sealed record ChatMessage(string Role, string Text);
internal sealed record ChatContext(string ThreadId, string Title, string Project, List<ChatMessage> Messages, string Evidence);
internal sealed record ContextReply(bool Success, string Error, ChatContext? Context, int VisibleTexts = 0, MatchDiagnostic? Diagnostic = null);
internal sealed class ContextResolutionException(string message, MatchDiagnostic? diagnostic) : InvalidOperationException(message)
{ public MatchDiagnostic? Diagnostic { get; } = diagnostic; }

internal static class ContextWorker
{
    public static string CodexHome => Path.GetFullPath(Environment.GetEnvironmentVariable("CODEX_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));
    public static async Task<ChatContext> ResolveAsync(nint window, string original, CancellationToken token)
    {
        if (!WindowCapture.IsCodexWindow(window))
            throw new InvalidOperationException("自动上下文目前支持 Codex 本机聊天；当前窗口暂未适配，原文未替换。");
        using var process = new Process { StartInfo = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardInput = true, RedirectStandardError = true
        } };
        process.StartInfo.ArgumentList.Add("--context-worker");
        process.StartInfo.ArgumentList.Add(window.ToInt64().ToString());
        process.Start();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var errors = process.StandardError.ReadToEndAsync(deadline.Token);
            // Input is passed through a pipe, never on the command line or in a temporary file.
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(original).AsMemory(), deadline.Token);
            await process.StandardInput.FlushAsync(deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            var reply = JsonSerializer.Deserialize<ContextReply>(await output);
            await errors; // Drain without logging local UI data or runtime diagnostics.
            if (process.ExitCode != 0 || reply?.Success != true || reply.Context == null)
                throw new ContextResolutionException(reply?.Error ?? "无法确定当前 Codex 聊天，原文未替换。", reply?.Diagnostic);
            return reply.Context;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new InvalidOperationException("读取当前聊天超过 15 秒，已停止本次增强，原文未替换。"); }
        finally { if (!process.HasExited) process.Kill(true); }
    }
    public static async Task DiagnoseAsync()
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var context = await ResolveAsync(Native.GetForegroundWindow(), "", CancellationToken.None);
            SelfTests.WriteReport("context-diagnostic.json", new { Success = true, Error = "", context.ThreadId,
                MessageCount = context.Messages.Count, context.Evidence, ElapsedMilliseconds = timer.ElapsedMilliseconds });
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
            if (!WindowCapture.IsCodexWindow(window)) throw new InvalidOperationException("当前前台窗口不是已支持的 Codex 窗口。");
            var input = JsonSerializer.Deserialize<string>(Console.In.ReadToEnd()) ?? "";
            var snapshot = WindowCapture.Capture(window);
            reply = new(true, "", CodexContext.Resolve(snapshot, CodexHome, input, value => diagnostic = value), snapshot.Texts.Count, diagnostic);
        }
        catch (InvalidOperationException e) { reply = new(false, e.Message, null, Diagnostic: diagnostic); }
        catch { reply = new(false, "无法读取 Codex 当前聊天，原文未替换。", null); }
        Console.Write(JsonSerializer.Serialize(reply));
        Environment.ExitCode = reply.Success ? 0 : 1;
    }
}
