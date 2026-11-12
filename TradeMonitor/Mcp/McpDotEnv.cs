namespace TradeMonitor;

/// <summary>
/// Loads a gitignored .env file into the process environment.
/// A non-empty environment variable wins, so a shell value overrides the file.
/// The search starts at the working directory and the app directory and walks
/// up to the repository root. The first .env found on each path is the one used.
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
        // dotnet run uses the project directory as the working directory, while
        // .env lives in the repository root, one level above TradeMonitor.
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = start;
            for (var depth = 0; depth < 6 && !string.IsNullOrEmpty(dir); depth++)
            {
                var candidate = Path.Combine(dir, ".env");
                if (File.Exists(candidate))
                {
                    yield return candidate;
                    break;
                }
                var parent = Directory.GetParent(dir)?.FullName;
                if (string.IsNullOrEmpty(parent) || parent == dir)
                    break;
                dir = parent;
            }
        }
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
            if (name.Length == 0 || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
                continue;

            var value = line[(split + 1)..].Trim();
            if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
                value = value[1..^1];
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
