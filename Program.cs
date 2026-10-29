namespace PromptDC;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--verify-enhancement")) { Task.Run(EnhancementRegression.RunAsync).GetAwaiter().GetResult(); return; }
        if (args.Length >= 2 && args[0] == "--context-worker") { Task.Run(() => ContextWorker.Run(args[1])).GetAwaiter().GetResult(); return; }
        if (args.Contains("--diagnose-context")) { Task.Run(ContextWorker.DiagnoseAsync).GetAwaiter().GetResult(); return; }
        if (args.Contains("--self-test")) { SelfTests.Run(); return; }
        if (args.Contains("--smoke-test")) { SmokeTests.Run(); return; }
        using var mutex = new Mutex(true, @"Local\PromptDC.Desktop", out var first);
        if (!first)
        {
            MessageBox.Show("PromptDC 已在运行，请双击任务栏右下角的托盘图标。", "PromptDC");
            return;
        }
        Application.Run(new MainForm(args.Contains("--background")));
    }
}
