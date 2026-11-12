using System.Reflection;

namespace TradeMonitor;

public static class AppVersion
{
    public static string Current { get; } = Read();

    static string Read()
    {
        var assembly = typeof(AppVersion).Assembly;
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
            return info;
        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
