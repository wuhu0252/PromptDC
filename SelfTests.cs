using System.Net;
using System.Text;
using System.Text.Json;

namespace PromptDC;

internal static class SelfTests
{
    public static void Run()
    {
        var results = new List<object>();
        void Check(string name, Action action)
        {
            try { action(); results.Add(new { name, passed = true, error = "" }); }
            catch (Exception e) { results.Add(new { name, passed = false, error = e.Message }); Environment.ExitCode = 1; }
        }
        Check("DPAPI encrypt/decrypt and no plaintext in settings", () =>
        {
            const string value = "test-credential-not-real";
            var encrypted = Secret.Protect(value);
            Assert(encrypted != value && Secret.Unprotect(encrypted) == value, "DPAPI roundtrip");
            var path = Path.Combine(Path.GetTempPath(), "promptdc-test-" + Guid.NewGuid(), "settings.json");
            try
            {
                SettingsStore.Save(new Settings { ProtectedKey = encrypted }, path);
                Assert(!File.ReadAllText(path).Contains(value), "plaintext leaked");
                Assert(JsonSerializer.Deserialize<Settings>(File.ReadAllText(path))!.GetKey() == value, "settings roundtrip");
            }
            finally { File.Delete(path); Directory.Delete(Path.GetDirectoryName(path)!); }
        });
        Check("hotkey accepts modifiers and rejects ambiguous combinations", () =>
        {
            Assert(Native.ParseHotkey("Ctrl+Alt+E") == (0x4003u, 69u), "hotkey parse");
            foreach (var bad in new[] { "E", "Shift+E", "Ctrl+E+F", "Ctrl+Enter", "Ctrl" })
            {
                var rejected = false;
                try { Native.ParseHotkey(bad); } catch (InvalidOperationException) { rejected = true; }
                Assert(rejected, "accepted " + bad);
            }
        });
        Check("DeepSeek request contract and Unicode output", () => ApiTest().GetAwaiter().GetResult());
        foreach (var code in new[] { 401, 402, 429, 500 })
            Check("HTTP " + code + " returns friendly error without leaking server body", () =>
                ExpectFailure(code, "server-private-data").GetAwaiter().GetResult());
        foreach (var (name, body) in new[]
        {
            ("truncated output", "{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"partial\"}}]}"),
            ("empty output", "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\" \"}}]}"),
            ("malformed JSON", "not json"),
            ("missing choices", "{}"),
            ("empty choices", "{\"choices\":[]}")
        }) Check("reject " + name, () => ExpectFailure(200, body).GetAwaiter().GetResult());
        Check("empty and oversized inputs fail before network", () =>
        {
            foreach (var text in new[] { " ", new string('a', 24001) })
            {
                var handler = new StubHandler(200, "{}");
                using var http = new HttpClient(handler);
                try { new DeepSeekClient(http).EnhanceAsync(text, TestSettings(), default).GetAwaiter().GetResult(); throw new Exception("not rejected"); }
                catch (InvalidOperationException) { Assert(handler.Calls == 0, "network called"); }
            }
        });
        WriteReport("self-tests.json", results);
    }
    private static Settings TestSettings() => new() { ProtectedKey = Secret.Protect("test-only") };
    private static async Task ApiTest()
    {
        var handler = new StubHandler(200, "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"  请排查登录接口。  \"}}]}");
        using var http = new HttpClient(handler);
        var result = await new DeepSeekClient(http).EnhanceAsync("帮我查登录错误", TestSettings(), default);
        Assert(result == "请排查登录接口。", "Unicode/trim");
        Assert(handler.Url == DeepSeekClient.Endpoint && handler.Bearer == "test-only", "endpoint/auth");
        using var body = JsonDocument.Parse(handler.Body!);
        Assert(body.RootElement.GetProperty("thinking").GetProperty("type").GetString() == "disabled", "thinking not disabled");
        Assert(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString() == "帮我查登录错误", "source changed");
    }
    private static async Task ExpectFailure(int status, string body)
    {
        using var http = new HttpClient(new StubHandler(status, body));
        try { await new DeepSeekClient(http).EnhanceAsync("请优化", TestSettings(), default); }
        catch (InvalidOperationException e) { Assert(!e.Message.Contains("server-private-data"), "leaked response body"); return; }
        throw new Exception("response was accepted");
    }
    internal static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static void WriteReport(string name, object results)
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "test-results");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }
    private sealed class StubHandler(int status, string response) : HttpMessageHandler
    {
        public int Calls;
        public string? Body, Url, Bearer;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; Url = request.RequestUri!.ToString(); Bearer = request.Headers.Authorization?.Parameter;
            Body = await request.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(response) };
        }
    }
}
