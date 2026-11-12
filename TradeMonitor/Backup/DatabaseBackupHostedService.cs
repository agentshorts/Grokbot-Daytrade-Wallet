namespace TradeMonitor;

/// <summary>
/// Takes one dated copy of App_Data/*.db when the host starts, then once a day.
/// </summary>
public sealed class DatabaseBackupHostedService(
    IHostEnvironment environment,
    IConfiguration configuration,
    ILogger<DatabaseBackupHostedService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Backup:Enabled", true))
            return;

        var keep = configuration.GetValue("Backup:Keep", DatabaseBackup.DefaultKeep);
        if (keep < 1)
            keep = DatabaseBackup.DefaultKeep;

        var appData = Path.Combine(environment.ContentRootPath, "App_Data");
        var backups = Path.Combine(environment.ContentRootPath, "backups");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = DatabaseBackup.Copy(appData, backups, keep);
                log.LogInformation(
                    "Database backup wrote {Count} file(s) under {Directory} and removed {Removed}.",
                    result.Created.Count,
                    backups,
                    result.Removed.Count);
            }
            catch (Exception ex)
            {
                log.LogWarning("Database backup failed: {Message}", ex.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
