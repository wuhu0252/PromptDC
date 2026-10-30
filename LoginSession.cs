using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PromptDC;

// This is a local credential lease, not a provider account or refresh token.
internal static class LoginSession
{
    public const int LifetimeDays = 30;
    private sealed record Ticket(string Provider, string Model, string KeyHash, DateTimeOffset IssuedUtc);

    public static Settings Clear(Settings settings) => settings with { ProtectedKey = "", ProtectedSession = "" };

    public static DateTimeOffset? ExpiresUtc(Settings settings)
    {
        try
        {
            if (settings.ProtectedKey.Length == 0 || settings.ProtectedSession.Length == 0) return null;
            var ticket = JsonSerializer.Deserialize<Ticket>(Secret.Unprotect(settings.ProtectedSession));
            if (ticket == null || ticket.Provider != settings.Provider || ticket.Model != settings.Model
                || ticket.KeyHash != Hash(settings.GetKey())) return null;
            return ticket.IssuedUtc.AddDays(LifetimeDays);
        }
        catch { return null; } // Corrupt/foreign-user credentials require a new login.
    }

    public static bool IsValid(Settings settings, DateTimeOffset now)
    {
        var expires = ExpiresUtc(settings);
        return expires.HasValue && now >= expires.Value.AddDays(-LifetimeDays) && now < expires.Value;
    }

    public static async Task<Settings> LoginAsync(Settings candidate, string key, DeepSeekClient client,
        CancellationToken token, Func<DateTimeOffset>? utcNow = null)
    {
        key = key.Trim();
        if (key.Length == 0 || key.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("请输入有效的 API 密钥（不能含空白字符）。");
        if (string.IsNullOrWhiteSpace(candidate.Model)) throw new InvalidOperationException("请选择或填写模型名称。");
        var pending = Clear(candidate) with { ProtectedKey = Secret.Protect(key) };
        await client.TestConnectionAsync(pending, token);
        token.ThrowIfCancellationRequested();
        var ticket = new Ticket(pending.Provider, pending.Model, Hash(key), (utcNow ?? (() => DateTimeOffset.UtcNow))());
        return pending with { ProtectedSession = Secret.Protect(JsonSerializer.Serialize(ticket)) };
    }

    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
