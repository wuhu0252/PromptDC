using System.Text.Json;

namespace PromptDC;

// Opt-in real API regression: fixed public project facts only, never reads actual chat history.
internal static class EnhancementRegression
{
    public static async Task RunAsync()
    {
        var results = new List<object>();
        try
        {
            var settings = SettingsStore.Load();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            var client = new DeepSeekClient(http);
            const string input = "这个项目的位置在哪，我要怎么启动";
            var context = new ChatContext("synthetic-regression", "PromptDC", "E:\\PromptDC",
                [new("user", "我们正在开发 PromptDC，一个 C#、.NET 8、Windows Forms 桌面提示词增强工具。"),
                 new("assistant", "当前项目目录为 E:\\PromptDC。已有 README、PromptDC.csproj 和 scripts/publish.ps1，编译后的程序入口为 PromptDC.exe。")], "fixed public fixture");
            foreach (var useContext in new[] { false, true })
            {
                var output = await client.EnhanceAsync(input, settings, default, useContext ? context : null);
                var inverted = output.StartsWith("请先说明你指的是", StringComparison.Ordinal)
                    || output.Contains("提供以下任一信息", StringComparison.Ordinal)
                    || output.Contains("请提供项目名称", StringComparison.Ordinal);
                var identity = !useContext || output.Contains("PromptDC", StringComparison.Ordinal);
                var passed = !inverted && identity;
                results.Add(new { UseContext = useContext, Input = input, Output = output, Passed = passed });
                if (!passed) Environment.ExitCode = 1;
            }
        }
        catch (Exception e)
        {
            results.Add(new { Passed = false, Error = e is InvalidOperationException ? e.Message : "接口回归未完成，请检查网络或配置。" });
            Environment.ExitCode = 1;
        }
        SelfTests.WriteReport("enhancement-regression.json", results);
    }
}
