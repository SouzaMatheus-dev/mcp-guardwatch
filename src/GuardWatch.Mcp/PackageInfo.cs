namespace GuardWatch.Mcp;

public static class PackageInfo
{
    public static string Version { get; } = typeof(PackageInfo).Assembly.GetName().Version is { } version
        ? $"{version.Major}.{version.Minor}.{version.Build}"
        : "0.4.0";

    public static string UserAgent => "McpGuardWatch/" + Version;
}
