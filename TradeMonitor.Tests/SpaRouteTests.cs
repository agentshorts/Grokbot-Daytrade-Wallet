using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;

namespace TradeMonitor.Tests;

[NonParallelizable]
public class SpaRouteTests
{
    const string ShellMarker = "data-spa-shell=\"trademonitor\"";

    string dbPath = null!;
    string webRoot = null!;
    string? previousMcpKey;
    SpaAppFactory factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        previousMcpKey = Environment.GetEnvironmentVariable(McpConfiguredKey.EnvName);
        Environment.SetEnvironmentVariable(McpConfiguredKey.EnvName, null);

        dbPath = Path.Combine(Path.GetTempPath(), "trademonitor-spa-" + Guid.NewGuid().ToString("N") + ".db");
        webRoot = Path.Combine(Path.GetTempPath(), "trademonitor-spa-www-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(webRoot);
        File.WriteAllText(Path.Combine(webRoot, "index.html"), """
            <!doctype html>
            <html lang="en">
            <head><title>TradeMonitor</title></head>
            <body><div id="app" data-spa-shell="trademonitor"></div></body>
            </html>
            """);
        Directory.CreateDirectory(Path.Combine(webRoot, "assets"));
        File.WriteAllText(Path.Combine(webRoot, "assets", "app.js"), "console.log('asset');");
        File.WriteAllText(Path.Combine(webRoot, "assets", "app.css"), "body{color:#111}");
        File.WriteAllBytes(Path.Combine(webRoot, "assets", "mark.gif"), Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"));
        Directory.CreateDirectory(Path.Combine(webRoot, "predictions"));
        File.WriteAllText(Path.Combine(webRoot, "predictions", "keep.js"), "console.log('keep');");

        factory = new SpaAppFactory(dbPath, webRoot);
        using var _ = factory.CreateClient();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        factory?.Dispose();
        Environment.SetEnvironmentVariable(McpConfiguredKey.EnvName, previousMcpKey);
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var file = dbPath + suffix;
            if (File.Exists(file))
                File.Delete(file);
        }
        if (Directory.Exists(webRoot))
            Directory.Delete(webRoot, recursive: true);
    }

    [Test]
    public async Task Spa_paths_return_the_shell_and_api_paths_do_not()
    {
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await AssertShell(http, "/");
        await AssertShell(http, "/?mode=paper&range=7d");
        await AssertShell(http, "/trades");
        await AssertShell(http, "/trades?trade=123");
        await AssertShell(http, "/wallets");
        await AssertShell(http, "/wallets?range=all");
        await AssertShell(http, "/wallet/paper");
        await AssertShell(http, "/wallet/paper?range=all&wallet=paper");

        var login = await Send(http, HttpMethod.Post, "/api/Authenticate", "application/json", null,
            """{"provider":"credentials","userName":"admin@email.com","password":"p@55wOrd","rememberMe":true}""");
        var loginBody = await login.Content.ReadAsStringAsync();
        Assert.That((int)login.StatusCode, Is.LessThan(400), loginBody);

        var trades = await Send(http, HttpMethod.Get, "/api/trades", "application/json", null);
        var tradesBody = await trades.Content.ReadAsStringAsync();
        Assert.That((int)trades.StatusCode, Is.EqualTo(200), tradesBody);
        Assert.That(Media(trades), Does.Contain("json"));
        Assert.That(tradesBody, Does.Not.Contain(ShellMarker));
        using (var document = JsonDocument.Parse(tradesBody))
        {
            Assert.That(document.RootElement.TryGetProperty("results", out _) || document.RootElement.TryGetProperty("Results", out _), Is.True, tradesBody);
        }

        var browserAccept = await Send(http, HttpMethod.Get, "/api/trades", "text/html,application/xhtml+xml", null);
        var browserBody = await browserAccept.Content.ReadAsStringAsync();
        Assert.That((int)browserAccept.StatusCode, Is.Not.EqualTo(404), browserBody);
        Assert.That(browserBody, Does.Not.Contain(ShellMarker), browserBody);

        var missing = await Send(http, HttpMethod.Get, "/api/does-not-exist", "text/html,application/xhtml+xml", null);
        var missingBody = await missing.Content.ReadAsStringAsync();
        Assert.That((int)missing.StatusCode, Is.EqualTo(404), missingBody);
        Assert.That(missingBody, Does.Not.Contain(ShellMarker));
        Assert.That(Media(missing), Does.Contain("json"));
        using (var document = JsonDocument.Parse(missingBody))
        {
            Assert.That(document.RootElement.GetProperty("responseStatus").GetProperty("errorCode").GetString(), Is.EqualTo("NotFound"));
        }

        var health = await Send(http, HttpMethod.Get, "/api/health", "application/json", null);
        var healthBody = await health.Content.ReadAsStringAsync();
        Assert.That((int)health.StatusCode, Is.EqualTo(200), healthBody);
        Assert.That(Media(health), Does.Contain("json"));
        using (var document = JsonDocument.Parse(healthBody))
        {
            Assert.That(document.RootElement.GetProperty("status").GetString(), Is.EqualTo("ok"));
        }

        var mcp = await Send(http, HttpMethod.Post, "/mcp", "application/json", null, "{}");
        var mcpBody = await mcp.Content.ReadAsStringAsync();
        Assert.That((int)mcp.StatusCode, Is.EqualTo(401), mcpBody);
        Assert.That(mcpBody, Does.Not.Contain(ShellMarker));
    }

    [Test]
    public async Task Dotted_prediction_contract_returns_the_shell()
    {
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await AssertShell(http, "/predictions/contract/KXBTCD-26NOV1617-T64999.99");
        await AssertShell(http, "/predictions/contract/KXBTCD-26NOV1617-T64999.99?hours=6&cents=5");
        await AssertShell(http, "/predictions/contract/KXBTCD-26NOV1617-T64999.99/");
        await AssertShell(http, "/predictions/contract/KXBTCD-26NOV1617-T65000");
        await AssertShell(http, "/predictions");
        await AssertShell(http, "/predictions?view=oscillation");

        await AssertFile(http, "/assets/app.js", "console.log('asset');", "javascript");
        await AssertFile(http, "/assets/app.css", "body{color:#111}", "css");
        await AssertFile(http, "/predictions/keep.js", "console.log('keep');", "javascript");

        var image = await Send(http, HttpMethod.Get, "/assets/mark.gif", "image/gif,*/*", null);
        var imageBody = await image.Content.ReadAsByteArrayAsync();
        Assert.That((int)image.StatusCode, Is.EqualTo(200));
        Assert.That(Media(image), Does.Contain("gif"));
        Assert.That(imageBody, Has.Length.GreaterThan(0));
        Assert.That(System.Text.Encoding.UTF8.GetString(imageBody), Does.Not.Contain(ShellMarker));

        var missing = await Send(http, HttpMethod.Get, "/assets/missing.js", "text/html,*/*", null);
        var missingBody = await missing.Content.ReadAsStringAsync();
        Assert.That((int)missing.StatusCode, Is.EqualTo(404), missingBody);
        Assert.That(missingBody, Does.Not.Contain(ShellMarker));

        var api = await Send(http, HttpMethod.Get, "/api/predictions/contract?ticker=KXBTCD-26NOV1617-T64999.99", "application/json", null);
        var apiBody = await api.Content.ReadAsStringAsync();
        Assert.That((int)api.StatusCode, Is.EqualTo(302), apiBody);
        Assert.That(api.Headers.Location?.OriginalString, Does.Contain("Account/Login").And.Contain("%2Fapi%2Fpredictions%2Fcontract"));
        Assert.That(apiBody, Does.Not.Contain(ShellMarker));

        var browserApi = await Send(http, HttpMethod.Get, "/api/predictions/contract?ticker=KXBTCD-26NOV1617-T64999.99", "text/html,application/xhtml+xml", null);
        var browserApiBody = await browserApi.Content.ReadAsStringAsync();
        Assert.That((int)browserApi.StatusCode, Is.EqualTo(302), browserApiBody);
        Assert.That(browserApi.Headers.Location?.OriginalString, Does.Contain("Account/Login").And.Contain("%2Fapi%2Fpredictions%2Fcontract"));
        Assert.That(browserApiBody, Does.Not.Contain(ShellMarker));

        var apiDot = await Send(http, HttpMethod.Get, "/api/does-not-exist.js", "text/html,*/*", null);
        var apiDotBody = await apiDot.Content.ReadAsStringAsync();
        Assert.That((int)apiDot.StatusCode, Is.EqualTo(404), apiDotBody);
        Assert.That(apiDotBody, Does.Not.Contain(ShellMarker));
        Assert.That(Media(apiDot), Does.Contain("json"));
    }

    static async Task AssertFile(HttpClient http, string path, string body, string media)
    {
        var response = await Send(http, HttpMethod.Get, path, "*/*", null);
        var text = await response.Content.ReadAsStringAsync();
        Assert.That((int)response.StatusCode, Is.EqualTo(200), path + " " + text);
        Assert.That(text, Is.EqualTo(body), path);
        Assert.That(text, Does.Not.Contain(ShellMarker), path);
        Assert.That(Media(response), Does.Contain(media), path);
    }

    static async Task AssertShell(HttpClient http, string path)
    {
        var response = await Send(http, HttpMethod.Get, path, "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8", null);
        var body = await response.Content.ReadAsStringAsync();
        Assert.That((int)response.StatusCode, Is.EqualTo(200), path + " " + body);
        Assert.That(Media(response), Does.Contain("text/html"));
        Assert.That(body, Does.Contain(ShellMarker), path);
    }

    static async Task<HttpResponseMessage> Send(HttpClient http, HttpMethod method, string path, string accept, string? apiKey, string? json = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Clear();
        request.Headers.TryAddWithoutValidation("Accept", accept);
        if (apiKey != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (json != null)
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return await http.SendAsync(request);
    }

    static string Media(HttpResponseMessage response) => response.Content.Headers.ContentType?.MediaType ?? "";

    sealed class SpaAppFactory(string dbPath, string webRoot) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseWebRoot(webRoot);
            builder.UseSetting("ConnectionStrings:DefaultConnection", "DataSource=" + dbPath + ";Cache=Shared");
            builder.UseSetting(McpConfiguredKey.ConfigPath, "");
        }
    }
}
