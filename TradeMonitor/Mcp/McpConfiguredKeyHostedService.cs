using ServiceStack.Configuration;
using ServiceStack.Data;
using ServiceStack.OrmLite;

namespace TradeMonitor;

/// <summary>
/// Registers MCP_API_KEY for the local admin user when that key is not already stored.
/// </summary>
public class McpConfiguredKeyHostedService(IConfiguration configuration, IDbConnectionFactory dbFactory, ILogger<McpConfiguredKeyHostedService> log)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var key = McpConfiguredKey.Read(configuration);
        if (key == null)
        {
            var raw = configuration[McpConfiguredKey.ConfigPath] ?? Environment.GetEnvironmentVariable(McpConfiguredKey.EnvName);
            if (!string.IsNullOrWhiteSpace(raw))
                log.LogWarning("The local MCP API key is shorter than {Minimum} characters and was not registered.", McpConfiguredKey.MinimumLength);
            return Task.CompletedTask;
        }

        var feature = HostContext.AppHost.GetPlugin<ApiKeysFeature>();
        if (feature == null)
        {
            log.LogWarning("API keys are not configured, so the local MCP API key was not registered.");
            return Task.CompletedTask;
        }

        using var db = dbFactory.Open();
        var existing = feature.GetApiKey(db, key);
        if (existing != null)
            return Task.CompletedTask;

        var admin = IdentityUsers.GetByUserNames(db, ["admin@email.com"]).FirstOrDefault();
        if (admin == null)
        {
            log.LogWarning("The local MCP API key was not registered because admin@email.com does not exist yet.");
            return Task.CompletedTask;
        }

        feature.Insert(db, new ApiKeysFeature.ApiKey
        {
            Key = key,
            Name = "Local MCP",
            UserId = admin.Id,
            UserName = admin.UserName,
            Scopes = [RoleNames.Admin],
        });
        log.LogInformation("Registered the local MCP API key for {User}.", admin.UserName);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
