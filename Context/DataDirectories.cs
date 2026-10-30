namespace PromptDC;

internal static class DataDirectories
{
    public static string CodexHome => ResolveHome(Environment.GetEnvironmentVariable("CODEX_HOME"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "CODEX_HOME");
    public static string HarnessHome => ResolveHome(Environment.GetEnvironmentVariable("DSH_HOME"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh", "DSH_HOME");

    internal static string ResolveHome(string? configured, string user, string folder, string variable)
    {
        if (string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(Path.Combine(user, folder));
        if (configured == "~") return Path.GetFullPath(user);
        if (configured.StartsWith("~/", StringComparison.Ordinal) || configured.StartsWith("~\\", StringComparison.Ordinal))
            return Path.GetFullPath(Path.Combine(user, configured[2..]));
        if (!Path.IsPathFullyQualified(configured))
            throw new InvalidOperationException($"{variable} 请使用实际数据目录的绝对路径或 ~/ 前缀，避免随启动目录改变而读取错误位置，原文未替换。");
        return Path.GetFullPath(configured);
    }
}
