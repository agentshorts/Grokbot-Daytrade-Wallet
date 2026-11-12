using System.Diagnostics;
using NUnit.Framework;

namespace TradeMonitor.Tests;

public class HostScriptTests
{
    [Test]
    public void Example_client_config_keeps_the_key_out_of_the_file()
    {
        var json = File.ReadAllText(Path.Combine(RepoRoot(), "config", "mcp.client.example.json"));
        Assert.That(json, Does.Contain("YOUR_MCP_API_KEY"));
        Assert.That(json, Does.Contain("http://127.0.0.1:5050/mcp"));
        Assert.That(json, Does.Not.Contain("apikey="));
        Assert.That(json, Does.Not.Contain("?"));
    }

    [Test]
    public void Gitignore_covers_env_files_and_backups()
    {
        var ignore = File.ReadAllText(Path.Combine(RepoRoot(), ".gitignore"));
        Assert.That(ignore, Does.Contain(".env"));
        Assert.That(ignore, Does.Contain("!.env.example"));
        Assert.That(ignore, Does.Contain("backups/"));
        Assert.That(ignore, Does.Contain("mcp.client.json"));
    }

    [Test]
    public void Run_script_refuses_a_non_loopback_url_before_it_writes_config()
    {
        var pwsh = FindPwsh();
        if (pwsh == null)
            Assert.Ignore("pwsh is not installed");

        var root = TempRoot();
        try
        {
            var result = Run(pwsh, "run.ps1", $"-Root \"{root}\" -Urls http://0.0.0.0:5050 -PrepareOnly");
            Assert.That(result.ExitCode, Is.Not.EqualTo(0), result.Output);
            Assert.That(result.Output, Does.Contain("127.0.0.1"));
            Assert.That(result.Output, Does.Contain("Refusing to bind"));
            Assert.That(File.Exists(Path.Combine(root, ".env")), Is.False);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Run_script_generates_a_key_without_printing_it()
    {
        var pwsh = FindPwsh();
        if (pwsh == null)
            Assert.Ignore("pwsh is not installed");

        var root = TempRoot();
        try
        {
            var first = Run(pwsh, "run.ps1", $"-Root \"{root}\" -PrepareOnly");
            Assert.That(first.ExitCode, Is.EqualTo(0), first.Output);
            var key = ReadKey(Path.Combine(root, ".env"));
            Assert.That(key.Length, Is.GreaterThanOrEqualTo(8));
            Assert.That(first.Output, Does.Not.Contain(key));
            Assert.That(first.Output, Does.Contain(key[..4]));
            Assert.That(first.Output, Does.Contain("Created .env from .env.example"));

            var second = Run(pwsh, "run.ps1", $"-Root \"{root}\" -PrepareOnly");
            Assert.That(second.ExitCode, Is.EqualTo(0), second.Output);
            Assert.That(ReadKey(Path.Combine(root, ".env")), Is.EqualTo(key));
            Assert.That(second.Output, Does.Not.Contain(key));
            Assert.That(File.ReadAllText(Path.Combine(RepoRoot(), ".env.example")), Does.Not.Contain(key));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Mcp_call_refuses_a_lan_url_and_a_key_in_the_query()
    {
        var pwsh = FindPwsh();
        if (pwsh == null)
            Assert.Ignore("pwsh is not installed");

        var root = TempRoot();
        var secret = "unit-test-key-value-not-a-secret";
        try
        {
            var lan = Run(
                pwsh,
                "mcp-call.ps1",
                $"-Root \"{root}\" -Url http://8.8.8.8:5050/mcp wallet_balances {{\\\"wallet\\\":\\\"paper\\\"}}",
                secret);
            Assert.That(lan.ExitCode, Is.Not.EqualTo(0), lan.Output);
            Assert.That(lan.Output, Does.Contain("127.0.0.1"));
            Assert.That(lan.Output, Does.Not.Contain(secret));

            var query = Run(
                pwsh,
                "mcp-call.ps1",
                $"-Root \"{root}\" -Url http://127.0.0.1:5050/mcp?apikey=do-not-print wallet_balances {{\\\"wallet\\\":\\\"paper\\\"}}",
                secret);
            Assert.That(query.ExitCode, Is.Not.EqualTo(0), query.Output);
            Assert.That(query.Output, Does.Contain("query string"));
            Assert.That(query.Output, Does.Not.Contain("do-not-print"));
            Assert.That(query.Output, Does.Not.Contain(secret));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Mcp_call_reports_a_missing_key_without_inventing_one()
    {
        var pwsh = FindPwsh();
        if (pwsh == null)
            Assert.Ignore("pwsh is not installed");

        var root = TempRoot();
        try
        {
            var result = Run(pwsh, "mcp-call.ps1", $"-Root \"{root}\" wallet_balances {{\\\"wallet\\\":\\\"paper\\\"}}");
            Assert.That(result.ExitCode, Is.Not.EqualTo(0), result.Output);
            Assert.That(result.Output, Does.Contain("MCP_API_KEY"));
            Assert.That(result.Output, Does.Not.Contain("Bearer"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Startup_task_script_parses()
    {
        var pwsh = FindPwsh();
        if (pwsh == null)
            Assert.Ignore("pwsh is not installed");

        var script = Path.Combine(RepoRoot(), "scripts", "install-startup-task.ps1");
        var psi = new ProcessStartInfo(pwsh, "-NoProfile -Command -")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        process.StandardInput.Write($$"""
            $errors = $null
            [void][System.Management.Automation.Language.Parser]::ParseFile('{{script}}', [ref]$null, [ref]$errors)
            if ($errors -and $errors.Count -gt 0) { $errors | ForEach-Object { Write-Output $_.ToString() }; exit 1 }
            exit 0
            """);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.That(process.ExitCode, Is.EqualTo(0), stdout + stderr);
    }

    static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "trademonitor-script-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.Copy(Path.Combine(RepoRoot(), ".env.example"), Path.Combine(root, ".env.example"));
        return root;
    }

    static string ReadKey(string path)
    {
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (!line.StartsWith("MCP_API_KEY=", StringComparison.Ordinal))
                continue;
            return line["MCP_API_KEY=".Length..].Trim();
        }

        Assert.Fail("MCP_API_KEY was not written");
        return "";
    }

    static (int ExitCode, string Output) Run(string pwsh, string script, string arguments, string? apiKey = null)
    {
        var file = Path.Combine(RepoRoot(), "scripts", script);
        var psi = new ProcessStartInfo(pwsh, $"-NoProfile -File \"{file}\" {arguments}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment.Remove("MCP_API_KEY");
        psi.Environment.Remove("MCP_URL");
        psi.Environment.Remove("ASPNETCORE_URLS");
        if (apiKey != null)
            psi.Environment["MCP_API_KEY"] = apiKey;

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    static string? FindPwsh()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>
        {
            Path.Combine(home, ".dotnet", "tools", "pwsh"),
            "/usr/bin/pwsh",
            "/usr/local/bin/pwsh",
        };
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                candidates.Add(Path.Combine(dir, "pwsh"));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, ".env.example")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new AssertionException("repository root was not found");
    }
}
