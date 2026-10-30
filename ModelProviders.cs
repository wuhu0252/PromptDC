namespace PromptDC;

internal sealed record ModelProvider(string Name, string Endpoint, string[] Models);

internal static class ModelProviders
{
    // Official Chat Completions endpoints; never infer a host from a model name.
    public static readonly ModelProvider[] All =
    [
        new("DeepSeek", DeepSeekClient.Endpoint, ["deepseek-flash", "deepseek-v4-pro"]),
        new("Kimi", "https://api.moonshot.cn/v1/chat/completions", ["kimi-k3.1-pro", "kimi-k2.6"]),
        new("豆包", "https://ark.cn-beijing.volces.com/api/v3/chat/completions", ["doubao-seed-2-1-pro-260915"]),
        new("智谱", "https://open.bigmodel.cn/api/paas/v4/chat/completions", ["glm-4.7"]),
        new("OpenAI (GPT)", "https://api.openai.com/v1/chat/completions", ["gpt-5.4-mini"])
    ];

    public static ModelProvider Get(string name) => All.FirstOrDefault(p => p.Name == name)
        ?? throw new InvalidOperationException("服务商不可用，请重新选择服务商并登录。");

    public static Dictionary<string, object> Request(Settings settings, List<object> messages)
    {
        Get(settings.Provider);
        var body = new Dictionary<string, object> { ["model"] = settings.Model, ["messages"] = messages, ["stream"] = false };
        var kimi3 = settings.Provider == "Kimi" && settings.Model.StartsWith("kimi-k3", StringComparison.Ordinal);
        body[settings.Provider == "OpenAI (GPT)" || kimi3 ? "max_completion_tokens" : "max_tokens"] = 8192;
        if (settings.Provider == "DeepSeek") body["temperature"] = 0.3;
        if (settings.Provider is "DeepSeek" or "豆包" or "智谱"
            || (settings.Provider == "Kimi" && settings.Model == "kimi-k2.6"))
            body["thinking"] = new { type = "disabled" };
        if (kimi3) body["reasoning_effort"] = "low";
        return body;
    }
}
