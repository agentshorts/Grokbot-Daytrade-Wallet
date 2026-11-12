namespace TradeMonitor;

/// <summary>
/// Loads a gitignored .env file into the process environment.
/// Existing environment variables win, so a shell value overrides the file.
/// </summary>
public static class McpDotEnv
{
    public static void Load()
    {
        foreach (var path in Candidates())
        {
            if (File.Exists(path))
                Apply(path);
        }
    }

    static IEnumerable<string> Candidates()
    {
        yield return Path.Combine(Directory.GetCurrentDirectory(), ".env");
        yield return Path.Combine(AppContext.BaseDirectory, ".env");
    }

    static void Apply(string path)
    {
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var split = line.IndexOf('=');
            if (split <= 0)
                continue;

            var name = line[..split].Trim();
            if (name.Length == 0 || Environment.GetEnvironmentVariable(name) != null)
                continue;

            var value = line[(split + 1)..].Trim();
            if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
                value = value[1..^1];
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
