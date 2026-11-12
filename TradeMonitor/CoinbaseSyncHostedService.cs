using Microsoft.Extensions.Options;
using ServiceStack.Data;
using TradeMonitor.ServiceInterface;

namespace TradeMonitor;

public sealed class CoinbaseSyncHostedService(
    IOptions<CoinbaseSyncOptions> options,
    IHostEnvironment environment,
    ICoinbaseCredentialSource credentials,
    IDbConnectionFactory dbFactory,
    ILedgerClock clock,
    CoinbaseSyncStatus status,
    ILogger<CoinbaseSyncHostedService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        status.Enabled = false;
        if (!CoinbaseSyncGate.ShouldRun(settings.Enabled, environment.IsEnvironment("Testing"), credentials))
        {
            if (settings.Enabled && !credentials.IsSupported)
            {
                status.LastError = "Coinbase sync is enabled but Windows Credential Manager is not available on this OS.";
                log.LogError("{Message}", status.LastError);
            }
            return;
        }

        CoinbaseApiCredential? credential;
        try
        {
            credential = credentials.Read();
        }
        catch (Exception ex)
        {
            status.Enabled = true;
            status.LastError = "Coinbase credential read failed (" + ex.GetType().Name + ").";
            log.LogError("{Message}", status.LastError);
            return;
        }

        if (credential == null)
        {
            status.Enabled = true;
            status.LastError = "Coinbase key is missing from Windows Credential Manager (service WalletSync-Coinbase).";
            log.LogError("{Message}", status.LastError);
            return;
        }

        CoinbaseSigner signer;
        try
        {
            signer = CoinbaseSigner.Load(credential.KeyName, credential.PrivateKey);
        }
        catch (Exception)
        {
            status.Enabled = true;
            status.LastError = "Coinbase private key format was not recognized.";
            log.LogError("{Message}", status.LastError);
            return;
        }

        status.Enabled = true;
        status.LastError = null;
        log.LogInformation("Coinbase key loaded from Windows Credential Manager.");
        var interval = TimeSpan.FromSeconds(settings.IntervalSeconds > 0 ? settings.IntervalSeconds : 60);
        var statePath = string.IsNullOrWhiteSpace(settings.StatePath)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "coinbase-sync-state.json")
            : settings.StatePath;
        var syncLog = new CoinbaseSyncLog
        {
            InfoSink = message => log.LogInformation("{Message}", message),
            ErrorSink = message => log.LogError("{Message}", message),
        };
        using var client = new CoinbaseBrokerageClient(signer);
        var syncer = new CoinbaseSyncer(client, new CoinbaseLedgerGateway(dbFactory, clock), clock, syncLog, statePath);
        try
        {
            await CoinbaseSyncLoop.RunAsync(
                syncer,
                token => CoinbaseSyncLoop.EnsureViewOnlyAsync(client, token),
                (delay, token) => Task.Delay(delay, token),
                maxCycles: null,
                status,
                clock,
                interval,
                syncLog,
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
