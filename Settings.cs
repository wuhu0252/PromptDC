using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace PromptDC;

internal sealed record Settings
{
    public string ProtectedKey { get; init; } = "";
    public string Model { get; init; } = "deepseek-flash";
    public string Mode { get; init; } = "自动识别";
    public string Hotkey { get; init; } = "Ctrl+Alt+E";
    public bool StartWithWindows { get; init; }
    public string Instructions { get; init; } = "";
    public bool AutomaticContext { get; init; } = true;
    public string GetKey() => ProtectedKey.Length == 0 ? "" : Secret.Unprotect(ProtectedKey);
}

internal static class SettingsStore
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PromptDC");
    public static string FilePath => Path.Combine(Folder, "settings.json");
    public static Settings Load() => File.Exists(FilePath)
        ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new() : new();

    public static void Save(Settings value, string? path = null)
    {
        path ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        File.Move(temporary, path, true);
    }
}

// Windows DPAPI, current-user scope. No API key is written in plaintext.
internal static class Secret
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public nint Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint pointer);

    public static string Protect(string text) => text.Length == 0 ? "" : Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(text), true));
    public static string Unprotect(string text) => Encoding.UTF8.GetString(Transform(Convert.FromBase64String(text), false));
    private static byte[] Transform(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        var output = new Blob();
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = encrypt ? CryptProtectData(ref input, "PromptDC", 0, 0, 0, 1, out output)
                : CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output);
            if (!ok) throw new InvalidOperationException("无法读取本机加密的 API Key，请重新填写并保存。");
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, output.Length);
            return result;
        }
        finally
        {
            for (var i = 0; i < bytes.Length; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != 0) LocalFree(output.Data);
            Array.Clear(bytes);
        }
    }
}
