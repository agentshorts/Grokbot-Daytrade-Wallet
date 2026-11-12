using NUnit.Framework;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Tests;

public class PnlEngineTests
{
    static readonly DateTime Day1 = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    static readonly DateTime Day4End = new(2026, 6, 4, 23, 59, 59, DateTimeKind.Utc);

    [Test]
    public void Partial_sell_fees_full_close_reopen_and_missing_mark_match_hand_totals()
    {
        // Buy 2 at 10 with fee 2 costs 22. Buy 2 at 20 with fee 2 costs 42.
        // Sell 3 at 20 with fee 3 takes the whole first lot (22) and half of the second (21).
        // Matched cost 43. Proceeds 60 - 3 = 57. Realized 14.
        // The leftover unit costs 21 and is sold at 21 with no fee, so that close realizes 0.
        // A later buy of a different symbol has no mark, so its unrealized P&L is unknown.
        var buyA = At(new DateTime(2026, 6, 1, 15, 1, 0, DateTimeKind.Utc));
        var buyB = At(new DateTime(2026, 6, 1, 15, 2, 0, DateTimeKind.Utc));
        var marked = At(new DateTime(2026, 6, 1, 15, 3, 0, DateTimeKind.Utc));
        var sold = At(new DateTime(2026, 6, 2, 15, 0, 0, DateTimeKind.Utc));
        var closed = At(new DateTime(2026, 6, 3, 15, 0, 0, DateTimeKind.Utc));
        var reopened = At(new DateTime(2026, 6, 4, 15, 0, 0, DateTimeKind.Utc));

        var trades = new List<Trade>
        {
            Trade(1, TradeSide.Buy, 2, 10, 2, buyA, target: 18),
            Trade(2, TradeSide.Buy, 2, 20, 2, buyB),
            Trade(3, TradeSide.Sell, 3, 20, 3, sold),
            Trade(4, TradeSide.Sell, 1, 21, 0, closed),
            Trade(5, TradeSide.Buy, 2, 5, 1, reopened, "SOL-USD", target: 9),
        };
        var replay = LedgerBook.Replay(1, trades,
        [
            Transfer(1, TransferKind.Deposit, 200, At(new DateTime(2026, 6, 1, 15, 0, 0, DateTimeKind.Utc))),
        ],
        [
            Mark(1, "BTC-USD", 15, marked),
        ], buyA);

        Assert.That(replay.Oversold, Is.False);
        Assert.That(replay.Cash, Is.EqualTo(203m));
        Assert.That(replay.RealizedPnl, Is.EqualTo(14m));
        Assert.That(replay.Fees, Is.EqualTo(8m));
        Assert.That(replay.PositionsValue, Is.EqualTo(11m));
        Assert.That(replay.TotalValue, Is.EqualTo(214m));
        Assert.That(replay.States, Has.Count.EqualTo(7));

        var afterMark = replay.States[3];
        Assert.That(afterMark.At, Is.EqualTo(marked));
        Assert.That(afterMark.TotalValue, Is.EqualTo(196m));
        Assert.That(afterMark.Cash, Is.EqualTo(136m));
        Assert.That(afterMark.UnrealizedPnl, Is.EqualTo(-4m));
        Assert.That(afterMark.ValuedAtCost, Is.False);

        var afterPartial = replay.States[4];
        Assert.That(afterPartial.RealizedPnl, Is.EqualTo(14m));
        Assert.That(afterPartial.UnrealizedPnl, Is.EqualTo(-6m));
        Assert.That(afterPartial.TotalValue, Is.EqualTo(208m));
        Assert.That(afterPartial.Cash, Is.EqualTo(193m));
        Assert.That(afterPartial.Symbols["BTC-USD"].Qty, Is.EqualTo(1m));
        Assert.That(afterPartial.Symbols["BTC-USD"].Cost, Is.EqualTo(21m));

        var afterClose = replay.States[5];
        Assert.That(afterClose.RealizedPnl, Is.EqualTo(14m));
        Assert.That(afterClose.UnrealizedPnl, Is.EqualTo(0m));
        Assert.That(afterClose.TotalValue, Is.EqualTo(214m));
        Assert.That(afterClose.Symbols["BTC-USD"].Qty, Is.EqualTo(0m));

        var final = replay.States[^1];
        Assert.That(final.UnrealizedPnl, Is.Null);
        Assert.That(final.ValuedAtCost, Is.True);
        Assert.That(final.UnmarkedCost, Is.EqualTo(11m));
        Assert.That(final.MarkedValue, Is.EqualTo(0m));
        Assert.That(final.TotalValue, Is.EqualTo(214m));
        Assert.That(final.Symbols["SOL-USD"].Unrealized, Is.Null);
        Assert.That(final.Symbols["BTC-USD"].Realized, Is.EqualTo(14m));

        var lots = replay.LotTrades.ToDictionary(x => x.TradeId);
        var first = lots[1];
        Assert.That(first.Status, Is.EqualTo(PnlTerms.Closed));
        Assert.That(first.RealizedPnl, Is.EqualTo(16m));
        Assert.That(first.Pnl, Is.EqualTo(16m));
        Assert.That(first.MatchedCost, Is.EqualTo(22m));
        Assert.That(first.Proceeds, Is.EqualTo(38m));
        Assert.That(first.BuyPrice, Is.EqualTo(10m));
        Assert.That(first.ExitPrice, Is.EqualTo(20m));
        Assert.That(first.PriceKind, Is.EqualTo(PnlTerms.Exit));
        Assert.That(first.PnlPercent, Is.EqualTo(16m / 22m * 100m));
        Assert.That(first.Fills.Single().SellFee, Is.EqualTo(2m));
        Assert.That(first.Fills.Single().BuyFee, Is.EqualTo(2m));

        var second = lots[2];
        Assert.That(second.Status, Is.EqualTo(PnlTerms.Closed));
        Assert.That(second.RealizedPnl, Is.EqualTo(-2m));
        Assert.That(second.Pnl, Is.EqualTo(-2m));
        Assert.That(second.ExitPrice, Is.EqualTo(20.5m));
        Assert.That(second.Fills.Select(x => x.RealizedPnl), Is.EqualTo(new[] { -2m, 0m }));

        var span = lots[3];
        Assert.That(span.Side, Is.EqualTo(TradeSide.Sell));
        Assert.That(span.MatchedCost, Is.EqualTo(43m));
        Assert.That(span.Proceeds, Is.EqualTo(57m));
        Assert.That(span.RealizedPnl, Is.EqualTo(14m));
        Assert.That(span.Pnl, Is.EqualTo(14m));
        Assert.That(span.BuyPrice, Is.EqualTo((2m * 10m + 1m * 20m) / 3m));
        Assert.That(span.CurrentOrExitPrice, Is.EqualTo(20m));
        Assert.That(span.PriceKind, Is.EqualTo(PnlTerms.Exit));
        Assert.That(span.PnlPercent, Is.EqualTo(14m / 43m * 100m));
        Assert.That(span.Fills.Select(x => x.Cost), Is.EqualTo(new[] { 22m, 21m }));
        Assert.That(span.Fills.Select(x => x.BuyFee), Is.EqualTo(new[] { 2m, 1m }));
        Assert.That(span.Fills.Select(x => x.SellFee), Is.EqualTo(new[] { 2m, 1m }));

        var flat = lots[4];
        Assert.That(flat.RealizedPnl, Is.EqualTo(0m));
        Assert.That(flat.MatchedCost, Is.EqualTo(21m));
        Assert.That(flat.Proceeds, Is.EqualTo(21m));
        Assert.That(flat.BuyPrice, Is.EqualTo(20m));

        var reopen = lots[5];
        Assert.That(reopen.Status, Is.EqualTo(PnlTerms.Open));
        Assert.That(reopen.PriceKind, Is.EqualTo(PnlTerms.Unknown));
        Assert.That(reopen.CurrentOrExitPrice, Is.Null);
        Assert.That(reopen.UnrealizedPnl, Is.Null);
        Assert.That(reopen.Pnl, Is.Null);
        Assert.That(reopen.PnlPercent, Is.Null);
        Assert.That(reopen.ValuedAtCost, Is.True);
        Assert.That(reopen.MarketValue, Is.EqualTo(11m));
        Assert.That(reopen.RemainingQty, Is.EqualTo(2m));
        Assert.That(reopen.Fills, Is.Empty);

        Assert.That(replay.LotTrades.Where(x => x.Side == TradeSide.Buy).Sum(x => x.RealizedPnl), Is.EqualTo(14m));
        Assert.That(replay.LotTrades.Where(x => x.Side == TradeSide.Sell).Sum(x => x.RealizedPnl), Is.EqualTo(14m));

        var days = PnlReport.Days(replay, Day1, Day4End);
        Assert.That(days.Select(x => x.Date), Is.EqualTo(new[]
        {
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc),
        }));
        Assert.That(days[0].Deposits, Is.EqualTo(200m));
        Assert.That(days[0].RealizedPnl, Is.EqualTo(0m));
        Assert.That(days[0].Fees, Is.EqualTo(4m));
        Assert.That(days[0].UnrealizedChange, Is.EqualTo(-4m));
        Assert.That(days[0].Pnl, Is.EqualTo(-4m));
        Assert.That(days[0].TotalValue, Is.EqualTo(196m));
        Assert.That(days[0].ValueChange, Is.EqualTo(196m));
        Assert.That(days[0].Trades, Is.EqualTo(2));
        Assert.That(days[1].RealizedPnl, Is.EqualTo(14m));
        Assert.That(days[1].Fees, Is.EqualTo(3m));
        Assert.That(days[1].UnrealizedChange, Is.EqualTo(-2m));
        Assert.That(days[1].Pnl, Is.EqualTo(12m));
        Assert.That(days[1].TotalValue, Is.EqualTo(208m));
        Assert.That(days[1].ValueChange, Is.EqualTo(12m));
        Assert.That(days[2].RealizedPnl, Is.EqualTo(0m));
        Assert.That(days[2].UnrealizedChange, Is.EqualTo(6m));
        Assert.That(days[2].Pnl, Is.EqualTo(6m));
        Assert.That(days[2].TotalValue, Is.EqualTo(214m));
        Assert.That(days[3].RealizedPnl, Is.EqualTo(0m));
        Assert.That(days[3].Fees, Is.EqualTo(1m));
        Assert.That(days[3].UnrealizedChange, Is.Null);
        Assert.That(days[3].Pnl, Is.Null);
        Assert.That(days[3].ValueChange, Is.EqualTo(0m));
        Assert.That(days[3].ValuedAtCost, Is.True);
        Assert.That(days[3].UnmarkedCost, Is.EqualTo(11m));
        Assert.That(days.Take(3).Sum(x => x.Pnl ?? 0m), Is.EqualTo(14m));

        var card = PnlReport.Summarize(Book(replay, trades), Day1, Day4End);
        Assert.That(card.RealizedPnl, Is.EqualTo(14m));
        Assert.That(card.UnrealizedPnl, Is.Null);
        Assert.That(card.Pnl, Is.Null);
        Assert.That(card.Fees, Is.EqualTo(8m));
        Assert.That(card.Deposits, Is.EqualTo(200m));
        Assert.That(card.Withdrawals, Is.EqualTo(0m));
        Assert.That(card.Sweeps, Is.EqualTo(0m));
        Assert.That(card.TotalValue, Is.EqualTo(214m));
        Assert.That(card.Cash, Is.EqualTo(203m));
        Assert.That(card.ValueChange, Is.EqualTo(214m));
        Assert.That(card.OpenExposure, Is.EqualTo(0m));
        Assert.That(card.UnmarkedCost, Is.EqualTo(11m));
        Assert.That(card.ValuedAtCost, Is.True);
        Assert.That(card.TradeCount, Is.EqualTo(5));
        Assert.That(card.Wins, Is.EqualTo(1));
        Assert.That(card.Losses, Is.EqualTo(0));
        Assert.That(card.Scratches, Is.EqualTo(1));
        Assert.That(card.WinRate, Is.EqualTo(100m));
        Assert.That(card.OpenPositions, Is.EqualTo(1));

        var symbols = PnlReport.Symbols([Book(replay, trades)], Day1, Day4End, null);
        var btc = symbols.Single(x => x.Symbol == "BTC-USD");
        var sol = symbols.Single(x => x.Symbol == "SOL-USD");
        Assert.That(btc.RealizedPnl, Is.EqualTo(14m));
        Assert.That(btc.Pnl, Is.EqualTo(14m));
        Assert.That(btc.UnrealizedPnl, Is.EqualTo(0m));
        Assert.That(btc.Fees, Is.EqualTo(7m));
        Assert.That(btc.Qty, Is.EqualTo(0m));
        Assert.That(btc.Wins, Is.EqualTo(1));
        Assert.That(sol.Pnl, Is.Null);
        Assert.That(sol.UnrealizedPnl, Is.Null);
        Assert.That(sol.Qty, Is.EqualTo(2m));
        Assert.That(sol.Fees, Is.EqualTo(1m));
        Assert.That(sol.ValuedAtCost, Is.True);
        Assert.That(sol.MarketValue, Is.EqualTo(11m));
    }

    [Test]
    public void Newest_mark_wins_wallet_specific_wins_a_tie_and_other_wallets_are_ignored()
    {
        var t0 = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        var t1 = t0.AddHours(1);
        var t2 = t0.AddHours(2);
        var replay = LedgerBook.Replay(1,
            [Trade(1, TradeSide.Buy, 1, 10, 0, t0)],
            [],
            [
                Mark(1, "BTC-USD", 11, t1),
                Mark(2, "BTC-USD", 12, t1, walletId: 1),
                Mark(3, "BTC-USD", 13, t2),
                Mark(4, "BTC-USD", 99, t2, walletId: 2),
                Mark(5, "BTC-USD", 14, t2, walletId: 1),
            ], t2);

        Assert.That(replay.States, Has.Count.EqualTo(5));
        Assert.That(replay.States[0].UnrealizedPnl, Is.Null);
        Assert.That(replay.States[0].ValuedAtCost, Is.True);
        Assert.That(replay.States[0].PositionsValue, Is.EqualTo(10m));
        Assert.That(replay.States[1].UnrealizedPnl, Is.EqualTo(1m));
        Assert.That(replay.States[1].MarkedValue, Is.EqualTo(11m));
        Assert.That(replay.States[2].UnrealizedPnl, Is.EqualTo(2m));
        Assert.That(replay.States[2].MarkedValue, Is.EqualTo(12m));
        Assert.That(replay.States[3].UnrealizedPnl, Is.EqualTo(3m));
        Assert.That(replay.States[3].MarkedValue, Is.EqualTo(13m));
        Assert.That(replay.States[4].UnrealizedPnl, Is.EqualTo(4m));
        Assert.That(replay.States[4].MarkedValue, Is.EqualTo(14m));
        Assert.That(replay.States.Select(x => x.MarkedValue), Does.Not.Contain(99m));

        var position = replay.Positions.Single();
        Assert.That(position.LastMarkPrice, Is.EqualTo(14m));
        Assert.That(position.LastMarkAt, Is.EqualTo(t2));
        Assert.That(position.Qty, Is.EqualTo(1m));
        Assert.That(position.CostBasis, Is.EqualTo(10m));
        var open = replay.LotTrades.Single();
        Assert.That(open.PriceKind, Is.EqualTo(PnlTerms.Mark));
        Assert.That(open.CurrentOrExitPrice, Is.EqualTo(14m));
        Assert.That(open.UnrealizedPnl, Is.EqualTo(4m));
        Assert.That(open.Pnl, Is.EqualTo(4m));
    }

    [Test]
    public void Last_share_of_a_lot_keeps_the_remaining_cost()
    {
        var at = new DateTime(2026, 7, 2, 12, 0, 0, DateTimeKind.Utc);
        var replay = LedgerBook.Replay(1,
        [
            Trade(1, TradeSide.Buy, 3, 1, 0.01m, at),
            Trade(2, TradeSide.Sell, 1, 1, 0, at.AddMinutes(1)),
            Trade(3, TradeSide.Sell, 1, 1, 0, at.AddMinutes(2)),
            Trade(4, TradeSide.Sell, 1, 1, 0, at.AddMinutes(3)),
        ], [], [], at);

        var first = 3.01m * 1m / 3m;
        var rest = 3.01m - first;
        var second = rest * 1m / 2m;
        var third = rest - second;
        var sells = replay.LotTrades.Where(x => x.Side == TradeSide.Sell).OrderBy(x => x.TradeId).ToList();
        Assert.That(sells.Select(x => x.MatchedCost), Is.EqualTo(new[] { first, second, third }));
        Assert.That(sells.Sum(x => x.MatchedCost), Is.EqualTo(3.01m));
        Assert.That(sells.Sum(x => x.RealizedPnl), Is.EqualTo(-0.01m));
        Assert.That(replay.RealizedPnl, Is.EqualTo(-0.01m));
        Assert.That(replay.Positions.Single().Qty, Is.EqualTo(0m));
        Assert.That(replay.Positions.Single().CostBasis, Is.EqualTo(0m));
        Assert.That(replay.Positions.Single().FeesPaid, Is.EqualTo(0.01m));
    }

    [Test]
    public void Transfers_change_value_and_are_not_pnl()
    {
        var at = new DateTime(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);
        var replay = LedgerBook.Replay(1,
            [Trade(1, TradeSide.Buy, 1, 10, 0, at.AddMinutes(1))],
            [
                Transfer(1, TransferKind.Deposit, 100, at),
                Transfer(2, TransferKind.Sweep, 40, at.AddMinutes(3)),
                Transfer(3, TransferKind.Withdrawal, 10, at.AddMinutes(4)),
            ],
            [Mark(1, "BTC-USD", 12, at.AddMinutes(2))], at);

        Assert.That(replay.RealizedPnl, Is.EqualTo(0m));
        Assert.That(replay.Cash, Is.EqualTo(40m));
        Assert.That(replay.Swept, Is.EqualTo(40m));
        Assert.That(replay.PositionsValue, Is.EqualTo(12m));
        Assert.That(replay.TotalValue, Is.EqualTo(52m));
        var day = PnlReport.Days(replay, null, null).Single();
        Assert.That(day.RealizedPnl, Is.EqualTo(0m));
        Assert.That(day.UnrealizedChange, Is.EqualTo(2m));
        Assert.That(day.Pnl, Is.EqualTo(2m));
        Assert.That(day.Deposits, Is.EqualTo(100m));
        Assert.That(day.Withdrawals, Is.EqualTo(10m));
        Assert.That(day.Sweeps, Is.EqualTo(40m));
        Assert.That(day.Fees, Is.EqualTo(0m));
        Assert.That(day.ValueChange, Is.EqualTo(52m));
        Assert.That(day.ValueChange, Is.EqualTo(day.Deposits - day.Withdrawals - day.Sweeps + day.Pnl));
    }

    static WalletBook Book(LedgerReplay replay, List<Trade> trades) => new()
    {
        Wallet = new Wallet { Id = 1, Key = "paper", Name = "Paper", BaseCurrency = "USD" },
        Trades = trades,
        Replay = replay,
    };

    static DateTime At(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    static Trade Trade(int id, TradeSide side, decimal qty, decimal price, decimal fee, DateTime at, string symbol = "BTC-USD", decimal? target = null) => new()
    {
        Id = id,
        WalletId = 1,
        ClientId = id.ToString(),
        Symbol = symbol,
        Side = side,
        Qty = qty,
        Price = price,
        Fee = fee,
        ExecutedAt = at,
        TargetPrice = target,
    };

    static Transfer Transfer(int id, TransferKind kind, decimal amount, DateTime at) => new()
    {
        Id = id,
        WalletId = 1,
        ClientId = "m" + id,
        Kind = kind,
        Amount = amount,
        Currency = "USD",
        At = at,
    };

    static PriceMark Mark(int id, string symbol, decimal price, DateTime at, int? walletId = null) => new()
    {
        Id = id,
        Symbol = symbol,
        Price = price,
        At = at,
        WalletId = walletId,
    };
}
