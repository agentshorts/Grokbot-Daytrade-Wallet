using NUnit.Framework;

namespace TradeMonitor.Tests;

public class SpaDocumentTests
{
    [Test]
    public void Browser_navigation_to_the_wallet_list_is_the_spa()
    {
        Assert.That(SpaDocument.ShouldServe("GET", "/wallets", "text/html,application/xhtml+xml", false), Is.True);
        Assert.That(SpaDocument.ShouldServe("GET", "/wallets/", "text/html", false), Is.True);
        Assert.That(SpaDocument.ShouldServe("HEAD", "/Wallets", "text/html", false), Is.True);
    }

    [Test]
    public void Json_and_nested_wallet_routes_stay_on_the_api()
    {
        Assert.That(SpaDocument.ShouldServe("GET", "/wallets", "application/json", false), Is.False);
        Assert.That(SpaDocument.ShouldServe("GET", "/wallets", "text/html", true), Is.False);
        Assert.That(SpaDocument.ShouldServe("POST", "/wallets", "text/html", false), Is.False);
        Assert.That(SpaDocument.ShouldServe("GET", "/wallets/paper", "text/html", false), Is.False);
        Assert.That(SpaDocument.ShouldServe("GET", "/wallets/paper/balances", "text/html", false), Is.False);
        Assert.That(SpaDocument.ShouldServe("GET", "/wallet/paper", "text/html", false), Is.False);
        Assert.That(SpaDocument.ShouldServe("GET", "/summary", "text/html", false), Is.False);
    }

    [Test]
    public void Browser_navigation_to_trades_is_the_spa()
    {
        Assert.That(SpaDocument.ShouldServe("GET", "/trades", "text/html", false), Is.True);
        Assert.That(SpaDocument.ShouldServe("GET", "/trades/12", "text/html", false), Is.False);
    }
}
