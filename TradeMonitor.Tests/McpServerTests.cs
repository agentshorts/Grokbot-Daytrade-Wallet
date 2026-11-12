using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using ServiceStack;
using ServiceStack.Auth;
using ServiceStack.Data;
using ServiceStack.Jobs;
using ServiceStack.OrmLite;
using ServiceStack.Web;
using TradeMonitor.ServiceModel;

namespace TradeMonitor.Tests;

[NonParallelizable]
public class LoopbackBindingTests
{
    [Test]
    public void Loopback_urls_are_rewritten_to_127_0_0_1()
    {
        Assert.That(LoopbackBinding.ToIpv4Loopback("http://127.0.0.1:5050"), Is.EqualTo("http://127.0.0.1:5050"));
        Assert.That(LoopbackBinding.ToIpv4Loopback("https://localhost:5051"), Is.EqualTo("https://127.0.0.1:5051"));
        Assert.That(LoopbackBinding.ToIpv4Loopback("http://[::1]:5050"), Is.EqualTo("http://127.0.0.1:5050"));
    }

    [TestCase("http://0.0.0.0:5050")]
    [TestCase("http://+:5050")]
    [TestCase("http://*:5050")]
    [TestCase("http://8.8.8.8:5050")]
    [TestCase("not a url")]
    public void Non_loopback_urls_are_refused(string url)
    {
        var error = Assert.Throws<InvalidOperationException>(() => LoopbackBinding.ToIpv4Loopback(url));
        Assert.That(error!.Message, Does.Contain("127.0.0.1"));
    }
}

[NonParallelizable]
public class McpServerTests
{
    const string AdminEmail = "admin@email.com";
    const string EmployeeEmail = "employee@email.com";

    string dbPath = null!;
    string logDir = null!;
    McpAppFactory factory = null!;
    string adminKey = null!;
    string employeeKey = null!;
    string? previousMcpKey;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        previousMcpKey = Environment.GetEnvironmentVariable(McpConfiguredKey.EnvName);
        Environment.SetEnvironmentVariable(McpConfiguredKey.EnvName, null);

        dbPath = Path.Combine(Path.GetTempPath(), "trademonitor-mcp-" + Guid.NewGuid().ToString("N") + ".db");
        logDir = Path.Combine(Path.GetTempPath(), "trademonitor-mcp-logs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logDir);
        factory = new McpAppFactory(dbPath, logDir);
        using var _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        var feature = HostContext.AppHost.GetPlugin<ApiKeysFeature>();
        Assert.That(feature, Is.Not.Null);
        using var db = dbFactory.Open();
        var users = IdentityUsers.GetByUserNames(db, [AdminEmail, EmployeeEmail]);
        var admin = users.Single(x => x.UserName == AdminEmail);
        var employee = users.Single(x => x.UserName == EmployeeEmail);
        adminKey = feature!.Insert(db, new ApiKeysFeature.ApiKey
        {
            Name = "MCP test admin",
            UserId = admin.Id,
            UserName = admin.UserName,
            Scopes = [Roles.Admin],
        }).Key;
        employeeKey = feature.Insert(db, new ApiKeysFeature.ApiKey
        {
            Name = "MCP test employee",
            UserId = employee.Id,
            UserName = employee.UserName,
            Scopes = [],
        }).Key;
        Assert.That(adminKey, Is.Not.Empty);
        Assert.That(employeeKey, Is.Not.EqualTo(adminKey));
        await Task.CompletedTask;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        factory?.Dispose();
        Environment.SetEnvironmentVariable(McpConfiguredKey.EnvName, previousMcpKey);
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var file = dbPath + suffix;
            if (file != null && File.Exists(file))
                File.Delete(file);
        }
        if (Directory.Exists(logDir))
            Directory.Delete(logDir, recursive: true);
    }

    [Test]
    public async Task Missing_key_is_401_and_non_admin_key_is_403()
    {
        using var http = factory.CreateClient();
        var body = new StringContent("{}", Encoding.UTF8, "application/json");
        var missing = await http.PostAsync("/mcp", body);
        Assert.That((int)missing.StatusCode, Is.EqualTo(401));

        var unknown = await Send("/mcp", "not-a-real-key");
        Assert.That((int)unknown.StatusCode, Is.EqualTo(401));

        var employeeHeader = await Send("/mcp", employeeKey);
        Assert.That((int)employeeHeader.StatusCode, Is.EqualTo(403));

        var employeeBearer = await Send("/mcp", employeeKey, bearer: true);
        Assert.That((int)employeeBearer.StatusCode, Is.EqualTo(403));

        var employeeQuery = await Send("/mcp?apikey=" + Uri.EscapeDataString(employeeKey), header: null);
        Assert.That((int)employeeQuery.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task Cancelled_and_expired_keys_are_401()
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        var feature = HostContext.AppHost.GetPlugin<ApiKeysFeature>()!;
        using var db = dbFactory.Open();
        var admin = IdentityUsers.GetByUserNames(db, [AdminEmail]).Single();
        var cancelled = feature.Insert(db, new ApiKeysFeature.ApiKey
        {
            Name = "MCP cancelled",
            UserId = admin.Id,
            UserName = admin.UserName,
            Scopes = [Roles.Admin],
            CancelledDate = DateTime.UtcNow,
        });
        var expired = feature.Insert(db, new ApiKeysFeature.ApiKey
        {
            Name = "MCP expired",
            UserId = admin.Id,
            UserName = admin.UserName,
            Scopes = [Roles.Admin],
            ExpiryDate = DateTime.UtcNow.AddMinutes(-5),
        });

        Assert.That((int)(await Send("/mcp", cancelled.Key)).StatusCode, Is.EqualTo(401));
        Assert.That((int)(await Send("/mcp", expired.Key)).StatusCode, Is.EqualTo(401));
    }

    [Test]
    public async Task Admin_key_lists_every_tool_and_writes_round_trip()
    {
        await using var mcp = await Connect(adminKey);
        var tools = await mcp.ListToolsAsync();
        var names = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        Assert.That(names, Is.EquivalentTo(new[]
        {
            "wallet_create",
            "wallet_update",
            "trade_record",
            "transfer_record",
            "price_mark_record",
            "price_marks_record",
            "wallets_list",
            "wallet_balances",
            "wallet_history",
            "trades_query",
            "pnl_by_day",
        }));
        Assert.That(tools.All(tool => !string.IsNullOrWhiteSpace(tool.Description)), Is.True);

        var created = await Call(mcp, "wallet_create", new Dictionary<string, object?>
        {
            ["key"] = "paper",
            ["name"] = "Paper",
            ["mode"] = "Paper",
            ["venue"] = "Coinbase",
            ["base_currency"] = "USD",
        });
        Assert.That(Bool(created, "alreadyExists"), Is.False);
        Assert.That(Text(Prop(Prop(created, "result"), "key")), Is.EqualTo("paper"));
        var walletId = Int(Prop(Prop(created, "result"), "id"));

        var again = await Call(mcp, "wallet_create", new Dictionary<string, object?>
        {
            ["key"] = "paper",
            ["name"] = "Paper",
            ["mode"] = "Paper",
            ["venue"] = "Coinbase",
            ["base_currency"] = "USD",
        });
        Assert.That(Bool(again, "alreadyExists"), Is.True);
        Assert.That(Int(Prop(Prop(again, "result"), "id")), Is.EqualTo(walletId));

        var conflict = await mcp.CallToolAsync("wallet_create", new Dictionary<string, object?>
        {
            ["key"] = "paper",
            ["name"] = "Different",
            ["mode"] = "Paper",
        });
        Assert.That(conflict.IsError, Is.True);
        Assert.That(Content(conflict), Does.Contain("different values"));

        var updated = await Call(mcp, "wallet_update", new Dictionary<string, object?>
        {
            ["key"] = "paper",
            ["name"] = "Paper book",
            ["sort_order"] = 2,
        });
        Assert.That(Text(Prop(Prop(updated, "result"), "name")), Is.EqualTo("Paper book"));

        var deposit = await Call(mcp, "transfer_record", new Dictionary<string, object?>
        {
            ["wallet"] = "paper",
            ["client_id"] = "dep-50",
            ["kind"] = "deposit",
            ["amount"] = 50m,
            ["at"] = "2026-06-01T15:00:00Z",
        });
        Assert.That(Bool(deposit, "alreadyRecorded"), Is.False);
        Assert.That(Decimal(Prop(Prop(deposit, "balance"), "cash")), Is.EqualTo(50m));
        var transferId = Int(Prop(Prop(deposit, "result"), "id"));

        var depositAgain = await Call(mcp, "transfer_record", new Dictionary<string, object?>
        {
            ["wallet"] = "paper",
            ["client_id"] = "dep-50",
            ["kind"] = "deposit",
            ["amount"] = 50m,
            ["at"] = "2026-06-01T15:00:00Z",
        });
        Assert.That(Bool(depositAgain, "alreadyRecorded"), Is.True);
        Assert.That(Int(Prop(Prop(depositAgain, "result"), "id")), Is.EqualTo(transferId));

        var depositConflict = await mcp.CallToolAsync("transfer_record", new Dictionary<string, object?>
        {
            ["wallet"] = "paper",
            ["client_id"] = "dep-50",
            ["kind"] = "deposit",
            ["amount"] = 40m,
        });
        Assert.That(depositConflict.IsError, Is.True);
        Assert.That(Content(depositConflict), Does.Contain("different values"));

        var buy = await Call(mcp, "trade_record", new Dictionary<string, object?>
        {
            ["wallet"] = "paper",
            ["client_id"] = "buy-1",
            ["symbol"] = "btc-usd",
            ["side"] = "Buy",
            ["qty"] = 1m,
            ["price"] = 10m,
            ["fee"] = 0.5m,
            ["executed_at"] = "2026-06-01T15:05:00Z",
            ["title"] = "clip",
            ["order_ref"] = "ord-1",
        });
        Assert.That(Bool(buy, "alreadyRecorded"), Is.False);
        Assert.That(Text(Prop(Prop(buy, "result"), "symbol")), Is.EqualTo("BTC-USD"));
        Assert.That(Decimal(Prop(Prop(buy, "balance"), "cash")), Is.EqualTo(39.5m));
        var tradeId = Int(Prop(Prop(buy, "result"), "id"));

        var buyAgain = await Call(mcp, "trade_record", new Dictionary<string, object?>
        {
            ["wallet"] = "paper",
            ["client_id"] = "buy-1",
            ["symbol"] = "BTC-USD",
            ["side"] = "Buy",
            ["qty"] = 1m,
            ["price"] = 10m,
            ["fee"] = 0.5m,
            ["title"] = "clip",
            ["order_ref"] = "ord-1",
        });
        Assert.That(Bool(buyAgain, "alreadyRecorded"), Is.True);
        Assert.That(Int(Prop(Prop(buyAgain, "result"), "id")), Is.EqualTo(tradeId));

        var tradeConflict = await mcp.CallToolAsync("trade_record", new Dictionary<string, object?>
        {
            ["wallet"] = "paper",
            ["client_id"] = "buy-1",
            ["symbol"] = "BTC-USD",
            ["side"] = "Buy",
            ["qty"] = 2m,
            ["price"] = 10m,
            ["fee"] = 0.5m,
        });
        Assert.That(tradeConflict.IsError, Is.True);
        Assert.That(Content(tradeConflict), Does.Contain("different values"));

        var oversell = await mcp.CallToolAsync("trade_record", new Dictionary<string, object?>
        {
            ["wallet"] = "paper",
            ["client_id"] = "sell-too-much",
            ["symbol"] = "BTC-USD",
            ["side"] = "Sell",
            ["qty"] = 5m,
            ["price"] = 12m,
            ["fee"] = 0m,
        });
        Assert.That(oversell.IsError, Is.True);
        Assert.That(Content(oversell), Does.Contain("exceeds the open quantity").IgnoreCase);

        var unknown = await mcp.CallToolAsync("wallet_balances", new Dictionary<string, object?>
        {
            ["wallet"] = "missing-wallet",
        });
        Assert.That(unknown.IsError, Is.True);
        Assert.That(Content(unknown), Does.Contain("Unknown wallet"));

        var mark = await Call(mcp, "price_mark_record", new Dictionary<string, object?>
        {
            ["symbol"] = "BTC-USD",
            ["price"] = 12m,
            ["at"] = "2026-06-01T15:06:00Z",
            ["wallet"] = "paper",
            ["client_id"] = "mark-1",
            ["source"] = "agent",
        });
        Assert.That(Bool(mark, "alreadyRecorded"), Is.False);
        var markId = Int(Prop(Prop(mark, "result"), "id"));

        var markAgain = await Call(mcp, "price_mark_record", new Dictionary<string, object?>
        {
            ["symbol"] = "BTC-USD",
            ["price"] = 12m,
            ["wallet"] = "paper",
            ["client_id"] = "mark-1",
            ["source"] = "agent",
        });
        Assert.That(Bool(markAgain, "alreadyRecorded"), Is.True);
        Assert.That(Int(Prop(Prop(markAgain, "result"), "id")), Is.EqualTo(markId));

        var marks = await Call(mcp, "price_marks_record", new Dictionary<string, object?>
        {
            ["marks"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["symbol"] = "BTC-USD",
                    ["price"] = 12m,
                    ["wallet"] = "paper",
                    ["client_id"] = "mark-1",
                    ["source"] = "agent",
                },
                new Dictionary<string, object?>
                {
                    ["symbol"] = "ETH-USD",
                    ["price"] = 3m,
                    ["client_id"] = "mark-eth",
                    ["source"] = "agent",
                },
            },
        });
        var results = Prop(marks, "results");
        Assert.That(results.GetArrayLength(), Is.EqualTo(2));
        Assert.That(Bool(results[0], "alreadyRecorded"), Is.True);
        Assert.That(Bool(results[1], "alreadyRecorded"), Is.False);

        var listed = await Call(mcp, "wallets_list", new Dictionary<string, object?>
        {
            ["include_archived"] = true,
        });
        var walletKeys = Prop(listed, "results").EnumerateArray().Select(row => Text(Prop(row, "key"))).ToList();
        Assert.That(walletKeys, Does.Contain("paper"));

        var balances = await Call(mcp, "wallet_balances", new Dictionary<string, object?>
        {
            ["wallet"] = "paper",
        });
        var balance = Prop(balances, "result");
        Assert.That(Decimal(Prop(balance, "cash")), Is.EqualTo(39.5m));
        Assert.That(Decimal(Prop(balance, "fees")), Is.EqualTo(0.5m));
        var positions = Prop(balance, "positions");
        Assert.That(positions.GetArrayLength(), Is.EqualTo(1));
        Assert.That(Decimal(Prop(positions[0], "lastMarkPrice")), Is.EqualTo(12m));

        var history = await Call(mcp, "wallet_history", new Dictionary<string, object?>
        {
            ["wallet"] = "paper",
            ["take"] = 10,
        });
        Assert.That(Int(Prop(history, "total")), Is.EqualTo(2));

        var trades = await Call(mcp, "trades_query", new Dictionary<string, object?>
        {
            ["wallet"] = "paper",
            ["symbol"] = "BTC-USD",
            ["side"] = "Buy",
            ["take"] = 10,
        });
        Assert.That(Int(Prop(trades, "total")), Is.EqualTo(1));
        Assert.That(Text(Prop(Prop(trades, "results")[0], "clientId")), Is.EqualTo("buy-1"));

        var pnl = await Call(mcp, "pnl_by_day", new Dictionary<string, object?>
        {
            ["wallet"] = "paper",
            ["from"] = "2026-06-01T00:00:00Z",
            ["to"] = "2026-06-02T00:00:00Z",
        });
        var days = Prop(pnl, "results");
        Assert.That(days.GetArrayLength(), Is.EqualTo(2));
        var june1 = days[0];
        Assert.That(Prop(june1, "date").GetRawText(), Does.Contain("2026-06-01"));
        Assert.That(Decimal(Prop(june1, "realizedPnl")), Is.EqualTo(0m));
        Assert.That(Decimal(Prop(june1, "unrealizedChange")), Is.EqualTo(1.5m));
        Assert.That(Decimal(Prop(june1, "pnl")), Is.EqualTo(1.5m));
        Assert.That(Decimal(Prop(june1, "fees")), Is.EqualTo(0.5m));
        Assert.That(Decimal(Prop(june1, "deposits")), Is.EqualTo(50m));
        Assert.That(Decimal(Prop(june1, "totalValue")), Is.EqualTo(51.5m));
        Assert.That(Decimal(Prop(june1, "cash")), Is.EqualTo(39.5m));
        Assert.That(Int(Prop(june1, "trades")), Is.EqualTo(1));
        var june2 = days[1];
        Assert.That(Prop(june2, "date").GetRawText(), Does.Contain("2026-06-02"));
        Assert.That(Decimal(Prop(june2, "pnl")), Is.EqualTo(0m));
        Assert.That(Int(Prop(june2, "trades")), Is.EqualTo(0));
        Assert.That(Decimal(Prop(june2, "totalValue")), Is.EqualTo(51.5m));
        var wallets = Prop(pnl, "wallets");
        Assert.That(wallets.GetArrayLength(), Is.EqualTo(1));
        Assert.That(Text(Prop(wallets[0], "wallet")), Is.EqualTo("paper"));
    }

    [Test]
    public async Task Mcp_request_log_drops_the_key_and_the_body()
    {
        const string marker = "log-body-marker-9f3c";
        await using var mcp = await Connect(adminKey);
        await Call(mcp, "wallet_create", new Dictionary<string, object?>
        {
            ["key"] = "log-wallet",
            ["name"] = marker,
            ["mode"] = "Paper",
        });

        var queryKey = "query-key-" + Guid.NewGuid().ToString("N");
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        var feature = HostContext.AppHost.GetPlugin<ApiKeysFeature>()!;
        using (var db = dbFactory.Open())
        {
            var admin = IdentityUsers.GetByUserNames(db, [AdminEmail]).Single();
            feature.Insert(db, new ApiKeysFeature.ApiKey
            {
                Key = queryKey,
                Name = "MCP query",
                UserId = admin.Id,
                UserName = admin.UserName,
                Scopes = [Roles.Admin],
            });
        }

        var queried = await Send("/mcp?apikey=" + Uri.EscapeDataString(queryKey), header: null, body: "{\"secret\":\"" + marker + "\"}");
        Assert.That((int)queried.StatusCode, Is.Not.EqualTo(401));
        Assert.That((int)queried.StatusCode, Is.Not.EqualTo(403));

        var logger = factory.Services.GetRequiredService<IRequestLogger>();
        Assert.That(logger, Is.InstanceOf<SqliteRequestLogger>());
        await ((SqliteRequestLogger)logger).TickAsync(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var files = Directory.GetFiles(logDir, "*", SearchOption.AllDirectories);
        Assert.That(files, Is.Not.Empty);
        var dumped = string.Join('\n', files.Select(file => Encoding.UTF8.GetString(File.ReadAllBytes(file))));
        Assert.That(dumped, Does.Contain("/mcp"));
        Assert.That(dumped, Does.Not.Contain(adminKey));
        Assert.That(dumped, Does.Not.Contain(queryKey));
        Assert.That(dumped, Does.Not.Contain(marker));
        Assert.That(dumped, Does.Not.Contain("X-Api-Key").IgnoreCase);
        Assert.That(dumped, Does.Not.Contain("Authorization"));
    }

    [Test]
    public void Host_refuses_a_non_loopback_url()
    {
        var path = Path.Combine(Path.GetTempPath(), "trademonitor-mcp-refuse-" + Guid.NewGuid().ToString("N") + ".db");
        using var refused = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("urls", "http://0.0.0.0:5050");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "DataSource=" + path + ";Cache=Shared");
            builder.UseSetting("RequestLogs:Dir", logDir);
            builder.UseSetting(McpConfiguredKey.ConfigPath, "");
        });

        var error = Assert.Catch(() => refused.CreateClient());
        Assert.That(error!.ToString(), Does.Contain("127.0.0.1"));
        if (File.Exists(path))
            File.Delete(path);
    }

    async Task<HttpResponseMessage> Send(string path, string? header, bool bearer = false, string? body = null)
    {
        using var http = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body ?? "{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        if (header != null)
        {
            if (bearer)
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + header);
            else
                request.Headers.TryAddWithoutValidation("X-Api-Key", header);
        }
        return await http.SendAsync(request);
    }

    async Task<McpClient> Connect(string key)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", key);
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, http, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    static async Task<JsonElement> Call(McpClient mcp, string name, Dictionary<string, object?> args)
    {
        var result = await mcp.CallToolAsync(name, args);
        Assert.That(result.IsError, Is.Not.True, Content(result));
        using var document = JsonDocument.Parse(Content(result));
        return document.RootElement.Clone();
    }

    static string Content(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    static JsonElement Prop(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }
        Assert.Fail($"Missing {name} in {element}");
        return default;
    }

    static string Text(JsonElement element) => element.GetString() ?? "";

    static bool Bool(JsonElement element, string name) => Prop(element, name).GetBoolean();

    static int Int(JsonElement element) => element.ValueKind == JsonValueKind.String
        ? int.Parse(element.GetString()!)
        : element.GetInt32();

    static decimal Decimal(JsonElement element) => element.ValueKind == JsonValueKind.String
        ? decimal.Parse(element.GetString()!, System.Globalization.CultureInfo.InvariantCulture)
        : element.GetDecimal();

    sealed class McpAppFactory(string dbPath, string logDir) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "DataSource=" + dbPath + ";Cache=Shared");
            builder.UseSetting("RequestLogs:Dir", logDir);
            builder.UseSetting(McpConfiguredKey.ConfigPath, "");
        }
    }
}
