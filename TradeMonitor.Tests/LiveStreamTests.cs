using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ServiceStack;
using ServiceStack.OrmLite;
using ServiceStack.Web;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel;

namespace TradeMonitor.Tests;

// TestServer never flushes a long-lived event stream, so this subscribes in memory
// and checks that each tick is published on its own price channel.
[NonParallelizable]
public class LiveStreamTests : IDisposable
{
    readonly string dbPath = Path.Combine(Path.GetTempPath(), "trademonitor-stream-" + Guid.NewGuid().ToString("N") + ".db");
    readonly string logDir = Path.Combine(Path.GetTempPath(), "trademonitor-stream-logs-" + Guid.NewGuid().ToString("N"));
    readonly string? previousMcpKey = Environment.GetEnvironmentVariable(McpConfiguredKey.EnvName);
    StreamApp? factory;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Environment.SetEnvironmentVariable(McpConfiguredKey.EnvName, null);
    }

    [TearDown]
    public void TearDown()
    {
        factory?.Dispose();
        factory = null;
        // The request log stores its monthly sqlite file in a process-wide named connection.
        // Drop it so the next host opens its own directory.
        foreach (var name in OrmLiteConnectionFactory.NamedConnections.Keys.Where(key => key.StartsWith("requests_", StringComparison.Ordinal)).ToList())
            OrmLiteConnectionFactory.NamedConnections.Remove(name);
        Environment.SetEnvironmentVariable(McpConfiguredKey.EnvName, previousMcpKey);
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var file = dbPath + suffix;
            if (File.Exists(file))
                File.Delete(file);
        }
        if (Directory.Exists(logDir))
            Directory.Delete(logDir, recursive: true);
    }

    public void Dispose() => TearDown();

    [Test]
    public void Server_events_heartbeat_is_fifteen_seconds()
    {
        Boot();
        var feature = HostContext.AppHost.GetPlugin<ServerEventsFeature>();
        Assert.That(feature, Is.Not.Null);
        Assert.That(feature!.HeartbeatInterval, Is.EqualTo(TimeSpan.FromSeconds(ConfigureServerEvents.HeartbeatSeconds)));
        Assert.That(feature.LimitToAuthenticatedUsers, Is.True);
        Assert.That(feature.StreamPath, Is.EqualTo("/event-stream"));
        Assert.That(feature.OnCreated, Is.Not.Null);
    }

    [Test]
    public async Task Each_published_tick_reaches_its_price_channel()
    {
        Boot();
        var events = factory!.Services.GetRequiredService<IServerEvents>();
        var frames = new List<string>();
        var response = ResponseStub.Create(frames);
        var sub = new EventSubscription(response)
        {
            SubscriptionId = "live-test",
            SessionId = "live-test-session",
            UserId = "1",
            UserName = "admin@email.com",
            DisplayName = "Admin",
            IsAuthenticated = true,
            Channels = [LiveChannels.Price("BTC-USD")],
            OnPublish = (_, _, frame) => frames.Add(frame),
        };
        await events.RegisterAsync(sub);

        var publisher = factory.Services.GetRequiredService<LiveTickerPublisher>();
        var at = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        publisher.Publish(new LiveTick { ProductId = "BTC-USD", Price = 100.25m, At = at });
        publisher.Publish(new LiveTick { ProductId = "BTC-USD", Price = 101.5m, At = at.AddSeconds(1) });
        publisher.Publish(new LiveTick { ProductId = "eth-usd", Price = 9m, At = at });
        publisher.Publish(new LiveTick { ProductId = "BTC-USD", Price = 102.75m, At = at.AddSeconds(2) });

        var body = string.Join("\n", frames);
        Assert.That(Count(body, "price.tick"), Is.GreaterThanOrEqualTo(3), body);
        Assert.That(body, Does.Contain("100.25"));
        Assert.That(body, Does.Contain("101.5"));
        Assert.That(body, Does.Contain("102.75"));
        Assert.That(body, Does.Not.Contain("ETH-USD"));
        await events.UnRegisterAsync(sub.SubscriptionId);
    }

    void Boot()
    {
        factory = new StreamApp(dbPath, logDir);
        using var _ = factory.CreateClient();
    }

    static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    sealed class StreamApp(string dbPath, string logDir) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "DataSource=" + dbPath + ";Cache=Shared");
            builder.UseSetting("RequestLogs:Dir", logDir);
            builder.UseSetting(McpConfiguredKey.ConfigPath, "");
        }
    }

    class ResponseStub : DispatchProxy
    {
        readonly MemoryStream body = new();

        public static IResponse Create(List<string> frames)
        {
            var proxy = Create<IResponse, ResponseStub>();
            ((ResponseStub)(object)proxy).Frames = frames;
            return proxy;
        }

        List<string> Frames { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod?.Name ?? "";
            if (name == "get_OutputStream")
                return body;
            if (name is "Write" or "WriteAsync" && args is { Length: > 0 } && args[0] is string text)
                Frames.Add(text);
            if (targetMethod?.ReturnType == typeof(Task))
                return Task.CompletedTask;
            if (targetMethod?.ReturnType == typeof(bool))
                return false;
            if (targetMethod?.ReturnType == typeof(string))
                return "";
            return null;
        }
    }
}
