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
            var fixtureProject = Path.Combine(Path.GetTempPath(), "PromptDC-regression", "PromptDC");
            var context = new ChatContext("synthetic-regression", "PromptDC", fixtureProject,
                [new("user", "我们正在开发 PromptDC，一个 C#、.NET 8、Windows Forms 桌面提示词增强工具。"),
                 new("assistant", $"项目目录示例为 {fixtureProject}。已有 README、PromptDC.csproj 和 scripts/publish.ps1，编译后的程序入口为 PromptDC.exe。")], "synthetic project fixture, not an actual workspace scan");
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
            // Public synthetic inputs exercise brevity and constraint retention with the actual model.
            foreach (var sample in new[]
            {
                (Input: "我想要登录后再跳转到这个界面，不是直接在这个界面登陆", Required: new[] { "登录", "主界面" }),
                (Input: "帮我排查登录接口偶尔返回 500 的问题，先看看项目现有代码，别改接口返回格式。", Required: new[] { "500", "返回格式" }),
                (Input: "请改为先显示独立登录页，登录成功后再进入当前主界面。请先登录再进入主界面，主界面在登录成功后显示。", Required: new[] { "登录", "主界面" })
            })
            {
                var output = await client.EnhanceAsync(sample.Input, settings with { Instructions = "" }, default, context);
                var passed = output.Length <= 120 && sample.Required.All(value => output.Contains(value, StringComparison.Ordinal));
                results.Add(new { Case = "concise rewrite", Input = sample.Input, Output = output,
                    InputLength = sample.Input.Length, OutputLength = output.Length, Passed = passed });
                if (!passed) Environment.ExitCode = 1;
            }
            const string preferredInput = "帮我排查登录接口偶尔返回 500 的问题，先看看项目现有代码，别改接口返回格式。";
            var preferred = await client.EnhanceAsync(preferredInput,
                settings with { Instructions = "只输出一句话，尽量在 60 个中文字符以内。" }, default, context);
            var preferencePassed = preferred.Length <= 60 && preferred.Contains("500") && preferred.Contains("返回格式");
            results.Add(new { Case = "extra rewrite preference", Input = preferredInput, Output = preferred,
                OutputLength = preferred.Length, Passed = preferencePassed });
            if (!preferencePassed) Environment.ExitCode = 1;
        }
        catch (Exception e)
        {
            results.Add(new { Passed = false, Error = e is InvalidOperationException ? e.Message : "接口回归未完成，请检查网络或配置。" });
            Environment.ExitCode = 1;
        }
        SelfTests.WriteReport("enhancement-regression.json", results);
    }
}
