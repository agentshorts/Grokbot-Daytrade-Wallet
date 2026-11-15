using System.Net;
using NUnit.Framework;
using TradeMonitor.ServiceInterface;

namespace TradeMonitor.Tests;

public class ContractCloseTests
{
    const string Sample = """
        {"market":{"ticker":"KXBTCD-26NOV1212-T71499.99","close_time":"2026-11-12T17:00:00Z","expiration_time":"2026-11-19T17:00:00Z","expected_expiration_time":"2026-11-12T17:05:00Z","subtitle":"$71,500 or above"}}
        """;

    [Test]
    public void Kalshi_close_time_is_noon_eastern_and_not_the_later_expiration()
    {
        var close = ContractCloseReader.ReadKalshi(Sample);
        Assert.That(close, Is.EqualTo(new DateTime(2026, 11, 12, 17, 0, 0, DateTimeKind.Utc)));
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var local = TimeZoneInfo.ConvertTimeFromUtc(close!.Value, eastern);
        Assert.That(local.Hour, Is.EqualTo(12));
        Assert.That(local.Date, Is.EqualTo(new DateTime(2026, 11, 12)));
        Assert.That(ContractTickers.KalshiTicker("pm-KXBTCD-26NOV1212-T71499.99-KALSHI"), Is.EqualTo("KXBTCD-26NOV1212-T71499.99"));
        Assert.That(ContractTickers.KalshiTicker("Coinbase KXBTCD-26NOV1210-T71199.99-KALSHI"), Is.EqualTo("KXBTCD-26NOV1210-T71199.99"));
    }

    [Test]
    public void Missing_or_broken_payloads_have_no_close_time()
    {
        Assert.That(ContractCloseReader.ReadKalshi("{}"), Is.Null);
        Assert.That(ContractCloseReader.ReadKalshi("not-json"), Is.Null);
        Assert.That(ContractCloseReader.ReadCoinbase("{}"), Is.Null);
        Assert.That(ContractTickers.KalshiTicker("pm-win"), Is.Null);
        Assert.That(ContractTickers.CoinbaseProductId("btc-usd"), Is.Null);
    }

    [Test]
    public void Coinbase_details_supply_a_close_when_present()
    {
        const string json = """
            {"product_id":"KXBTCD-26NOV1212-T71499.99-KALSHI","prediction_market_product_details":{"close_time":"2026-11-12T17:00:00Z","expiration":"2026-11-19T17:00:00Z"}}
            """;
        Assert.That(ContractCloseReader.ReadCoinbase(json), Is.EqualTo(new DateTime(2026, 11, 12, 17, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void Lookup_uses_kalshi_close_time_caches_it_and_sends_no_credentials()
    {
        var handler = new ScriptedHandler();
        handler.When("api.elections.kalshi.com", HttpStatusCode.OK, Sample);
        using var http = new HttpClient(handler);
        var closes = new PublicContractCloses(http);

        var first = closes.CloseAt("pm-KXBTCD-26NOV1212-T71499.99-KALSHI");
        var second = closes.CloseAt("KXBTCD-26NOV1212-T71499.99-KALSHI");

        Assert.That(first, Is.EqualTo(new DateTime(2026, 11, 12, 17, 0, 0, DateTimeKind.Utc)));
        Assert.That(second, Is.EqualTo(first));
        Assert.That(handler.Calls, Is.EqualTo(1));
        Assert.That(handler.Paths[0], Does.EndWith("/markets/KXBTCD-26NOV1212-T71499.99"));
        Assert.That(handler.SentAuthorization[0], Is.False);
    }

    [Test]
    public void Lookup_falls_back_to_coinbase_when_kalshi_has_no_market()
    {
        var handler = new ScriptedHandler();
        handler.When("api.elections.kalshi.com", HttpStatusCode.NotFound, "");
        handler.When("api.coinbase.com", HttpStatusCode.OK, """
            {"prediction_market_product_details":{"expiration_time":"2026-11-12T15:00:00Z"}}
            """);
        using var http = new HttpClient(handler);
        var closes = new PublicContractCloses(http);

        var close = closes.CloseAt("KXBTCD-26NOV1210-T71199.99");

        Assert.That(close, Is.EqualTo(new DateTime(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc)));
        Assert.That(handler.Calls, Is.EqualTo(2));
        Assert.That(handler.Paths[1], Does.Contain("KXBTCD-26NOV1210-T71199.99-KALSHI"));
        Assert.That(handler.SentAuthorization, Is.All.False);
    }

    sealed class ScriptedHandler : HttpMessageHandler
    {
        readonly List<(string Host, HttpStatusCode Status, string Body)> routes = [];

        public int Calls { get; private set; }

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Paths { get; } = [];

        public List<bool> SentAuthorization { get; } = [];

        public void When(string host, HttpStatusCode status, string body) => routes.Add((host, status, body));

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => Respond(request);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Respond(request));

        HttpResponseMessage Respond(HttpRequestMessage request)
        {
            Calls++;
            Requests.Add(request);
            Paths.Add(request.RequestUri?.AbsolutePath ?? "");
            SentAuthorization.Add(request.Headers.Authorization != null || request.Headers.Contains("X-Api-Key"));
            var route = routes.FirstOrDefault(item => request.RequestUri?.Host.Contains(item.Host, StringComparison.OrdinalIgnoreCase) == true);
            var body = route.Body ?? "";
            var status = route.Status == 0 ? HttpStatusCode.NotFound : route.Status;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body),
            };
        }
    }
}
