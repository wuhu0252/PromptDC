using System.Net;
using System.Text.Json;

namespace PromptDC;

internal static class LoginTests
{
    public static void Register(Action<string, Action> check)
    {
        var now = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        foreach (var provider in ModelProviders.All)
            check(provider.Name + " login routes credentials and model to the official endpoint", () =>
            {
                var handler = new Handler(200, Good);
                using var http = new HttpClient(handler);
                var settings = LoginSession.LoginAsync(new() { Provider = provider.Name, Model = provider.Models[0] },
                    "test-provider-key", new(http), default, () => now).GetAwaiter().GetResult();
                SelfTests.Assert(handler.Url == provider.Endpoint && handler.Bearer == "test-provider-key"
                    && LoginSession.IsValid(settings, now), "provider/auth/session mismatch");
                using var json = JsonDocument.Parse(handler.Body!);
                var body = json.RootElement;
                SelfTests.Assert(body.GetProperty("model").GetString() == provider.Models[0] && !body.GetProperty("stream").GetBoolean(), "model/stream mismatch");
                if (provider.Name == "OpenAI (GPT)" || provider.Name == "Kimi")
                    SelfTests.Assert(body.GetProperty("max_completion_tokens").GetInt32() == 8192
                        && !body.TryGetProperty("temperature", out _) && !body.TryGetProperty("thinking", out _), "unsupported provider parameters");
                else SelfTests.Assert(body.GetProperty("thinking").GetProperty("type").GetString() == "disabled", "thinking not disabled");
            });
        check("login validates request before creating a protected 30-day session", () =>
        {
            var handler = new Handler(200, Good);
            using var http = new HttpClient(handler);
            var preferences = new Settings { Hotkey = "Ctrl+Alt+Q", AutomaticContext = false };
            var loggedIn = LoginSession.LoginAsync(preferences, " test-login-key ", new(http), default, () => now).GetAwaiter().GetResult();
            SelfTests.Assert(handler.Calls == 1 && handler.Bearer == "test-login-key", "connection not authenticated");
            SelfTests.Assert(loggedIn.Hotkey == preferences.Hotkey && !loggedIn.AutomaticContext, "preferences lost");
            SelfTests.Assert(LoginSession.IsValid(loggedIn, now) && LoginSession.IsValid(loggedIn, now.AddDays(30).AddTicks(-1))
                && !LoginSession.IsValid(loggedIn, now.AddDays(30)) && !LoginSession.IsValid(loggedIn, now.AddTicks(-1)), "expiry boundaries");
            SelfTests.Assert(LoginSession.ExpiresUtc(loggedIn) == now.AddDays(30), "not 30 days");
            var json = JsonSerializer.Serialize(loggedIn);
            SelfTests.Assert(!json.Contains("test-login-key") && !json.Contains("IssuedUtc"), "plaintext login data");
            var restored = JsonSerializer.Deserialize<Settings>(json)!;
            SelfTests.Assert(LoginSession.IsValid(restored, now.AddDays(20)), "restart lost session");
            foreach (var changed in new[] { restored with { Model = "changed" }, restored with { Provider = "changed" },
                restored with { ProtectedKey = Secret.Protect("other-key") }, restored with { ProtectedSession = "broken" },
                restored with { ProtectedKey = "broken" } })
                SelfTests.Assert(!LoginSession.IsValid(changed, now), "accepted changed credential/session");
            var cleared = LoginSession.Clear(restored);
            SelfTests.Assert(cleared.ProtectedKey == "" && cleared.ProtectedSession == "" && !LoginSession.IsValid(cleared, now)
                && cleared.Hotkey == preferences.Hotkey, "logout failed");
        });
        check("legacy encrypted keys are not silently treated as a successful login", () =>
        {
            var legacy = JsonSerializer.Deserialize<Settings>("{\"ProtectedKey\":\"" + Secret.Protect("legacy-test") + "\"}")!;
            SelfTests.Assert(legacy.Provider == "DeepSeek" && legacy.ProtectedSession == "" && !LoginSession.IsValid(legacy, now), "legacy bypass");
        });
        foreach (var (status, body) in new[] { (401, "private-server-data"), (402, "private-server-data"), (429, "private-server-data"),
            (500, "private-server-data"), (200, "not json"), (200, "{}"),
            (200, "{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"partial\"}}]}"),
            (200, "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"\"}}]}") })
            check($"login rejects HTTP {status} / invalid response {body.Length}", () =>
            {
                using var http = new HttpClient(new Handler(status, body));
                try { LoginSession.LoginAsync(new(), "test-login-key", new(http), default).GetAwaiter().GetResult(); }
                catch (InvalidOperationException e)
                {
                    SelfTests.Assert(!e.Message.Contains("test-login-key") && !e.Message.Contains("private-server-data"), "error leaks secret");
                    return;
                }
                throw new Exception("failed connection logged in");
            });
        check("login rejects missing or whitespace credentials before network and cancellation never logs in", () =>
        {
            var handler = new Handler(200, Good);
            using var http = new HttpClient(handler);
            foreach (var key in new[] { "", " ", "bad key" })
            {
                try { LoginSession.LoginAsync(new(), key, new(http), default).GetAwaiter().GetResult(); throw new Exception("invalid key accepted"); }
                catch (InvalidOperationException) { }
            }
            SelfTests.Assert(handler.Calls == 0, "network called for invalid input");
            using var cts = new CancellationTokenSource(); cts.Cancel();
            try { LoginSession.LoginAsync(new(), "test-login-key", new(http), cts.Token).GetAwaiter().GetResult(); throw new Exception("cancellation accepted"); }
            catch (OperationCanceledException) { }
        });
    }

    private const string Good = "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"验证通过\"}}]}";
    private sealed class Handler(int status, string body) : HttpMessageHandler
    {
        public int Calls;
        public string? Bearer, Url, Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Bearer = request.Headers.Authorization?.Parameter;
            Url = request.RequestUri!.ToString(); Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) };
        }
    }
}
