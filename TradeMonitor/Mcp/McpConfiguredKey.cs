namespace TradeMonitor;

/// <summary>
/// The local MCP API key comes from MCP_API_KEY or Mcp:ApiKey in local config.
/// The value is never written to the repository.
/// </summary>
public static class McpConfiguredKey
{
    public const string EnvName = "MCP_API_KEY";
    public const string ConfigPath = "Mcp:ApiKey";
    public const int MinimumLength = 8;

    public static void AddLocalSources(ConfigurationManager configuration)
    {
        configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
        var envKey = Environment.GetEnvironmentVariable(EnvName);
        if (string.IsNullOrWhiteSpace(envKey))
            return;

        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ConfigPath] = envKey.Trim(),
        });
    }

    public static string? Read(IConfiguration configuration)
    {
        var value = configuration[ConfigPath];
        if (string.IsNullOrWhiteSpace(value))
            value = Environment.GetEnvironmentVariable(EnvName);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var key = value.Trim();
        return key.Length >= MinimumLength ? key : null;
    }
}
