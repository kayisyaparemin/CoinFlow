using System.Reflection;

namespace CoinFlow.App.Services;

public static class BuildInfo
{
#if COINFLOW_DEV_BUILD
    public const bool IsDevelopment = true;
#else
    public const bool IsDevelopment = false;
#endif

    public static string Version => AppInfo.Current.VersionString;
    public static string Commit => Metadata("CoinFlowCommit", "local");
    public static string BuildNumber => Metadata("CoinFlowBuildNumber", AppInfo.Current.BuildString);
    public static string Channel => IsDevelopment ? "Development Build" : "Stable Release";

    private static string Metadata(string key, string fallback) =>
        typeof(BuildInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == key)?.Value ?? fallback;
}
