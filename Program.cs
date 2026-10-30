namespace PromptDC;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--verify-enhancement")) { Task.Run(EnhancementRegression.RunAsync).GetAwaiter().GetResult(); return; }
        if (args.Length >= 2 && args[0] == "--context-worker") { Task.Run(() => ContextWorker.Run(args[1])).GetAwaiter().GetResult(); return; }
        if (args.Contains("--diagnose-context"))
        {
            var index = Array.IndexOf(args, "--diagnose-context");
            var window = long.TryParse(args.ElementAtOrDefault(index + 1), out var handle) ? new nint(handle) : 0;
            Task.Run(() => ContextWorker.DiagnoseAsync(window)).GetAwaiter().GetResult(); return;
        }
        if (args.Contains("--self-test")) { SelfTests.Run(); return; }
        if (args.Contains("--smoke-test")) { SmokeTests.Run(); return; }
        if (args.Length == 2 && args[0] == "--smoke-harness" && long.TryParse(args[1], out var harnessWindow))
        { HarnessSmokeTests.Run(new nint(harnessWindow)); return; }
        using var mutex = new Mutex(true, @"Local\PromptDC.Desktop", out var first);
        if (!first)
        {
            MessageBox.Show("PromptDC 已在运行，请双击任务栏右下角的托盘图标。", "PromptDC");
            return;
        }
        Application.Run(new MainForm(args.Contains("--background")));
    }
}
