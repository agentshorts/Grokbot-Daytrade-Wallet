using System.Data;
using ServiceStack;
using ServiceStack.OrmLite;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

public sealed class WalletBook
{
    public required Wallet Wallet { get; init; }
    public List<Trade> Trades { get; init; } = [];
    public List<Transfer> Transfers { get; init; } = [];
    public List<PriceMark> Marks { get; init; } = [];
    public required LedgerReplay Replay { get; init; }
    public DateTime? LastActivityAt { get; init; }
}

public static class LedgerData
{
    public static List<WalletBook> Load(IDbConnection db, DateTime updatedAt)
    {
        var wallets = db.Select<Wallet>();
        var trades = db.Select<Trade>();
        var transfers = db.Select<Transfer>();
        var marks = db.Select<PriceMark>();
        var books = new List<WalletBook>(wallets.Count);
        foreach (var wallet in wallets)
        {
            var mine = trades.Where(x => x.WalletId == wallet.Id).ToList();
            var flow = transfers.Where(x => x.WalletId == wallet.Id).ToList();
            var applicable = marks.Where(x => x.WalletId == null || x.WalletId == wallet.Id).ToList();
            var replay = LedgerBook.Replay(wallet.Id, mine, flow, applicable, updatedAt);
            if (replay.Oversold)
                throw new HttpError(500, "LedgerCorrupt",
                    $"Stored trades oversell {replay.OversellSymbol} in wallet '{wallet.Key}'.");
            books.Add(new WalletBook
            {
                Wallet = wallet,
                Trades = mine,
                Transfers = flow,
                Marks = applicable,
                Replay = replay,
                LastActivityAt = LastActivity(mine, flow, applicable),
            });
        }

        return books;
    }

    static DateTime? LastActivity(List<Trade> trades, List<Transfer> transfers, List<PriceMark> marks)
    {
        DateTime? last = null;
        void Touch(DateTime at)
        {
            if (last == null || at > last)
                last = at;
        }

        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var trade in trades)
        {
            Touch(trade.ExecutedAt);
            symbols.Add(trade.Symbol.Trim().ToUpperInvariant());
        }

        foreach (var transfer in transfers)
            Touch(transfer.At);

        foreach (var mark in marks)
        {
            var symbol = mark.Symbol.Trim().ToUpperInvariant();
            if (mark.WalletId != null || symbols.Contains(symbol))
                Touch(mark.At);
        }

        return last;
    }
}

public static class TradeMaps
{
    public static TradeView ToView(Trade trade, Wallet wallet, LotTrade? lot)
    {
        var view = new TradeView
        {
            Id = trade.Id,
            Wallet = wallet.Key,
            WalletName = wallet.Name,
            Mode = wallet.Mode,
            WalletId = trade.WalletId,
            ClientId = trade.ClientId,
            Symbol = trade.Symbol,
            Title = trade.Title,
            Side = trade.Side,
            Kind = trade.Kind,
            Qty = trade.Qty,
            Price = trade.Price,
            Fee = trade.Fee,
            FeeCurrency = trade.FeeCurrency,
            ExecutedAt = trade.ExecutedAt,
            OrderRef = trade.OrderRef,
            TargetPrice = trade.TargetPrice,
            Note = trade.Note,
            RecordedAt = trade.RecordedAt,
            BuyPrice = trade.Price,
            Costing = trade.Costing,
            LotId = trade.LotId,
        };
        if (lot == null)
            return view;

        view.BuyPrice = lot.BuyPrice;
        view.CurrentOrExitPrice = lot.CurrentOrExitPrice;
        view.PriceKind = lot.PriceKind;
        view.Status = lot.Status;
        view.ExitPrice = lot.ExitPrice;
        view.LastMarkPrice = lot.LastMarkPrice;
        view.LastMarkAt = lot.LastMarkAt;
        view.MatchedCost = lot.MatchedCost;
        view.Proceeds = lot.Proceeds;
        view.RealizedPnl = lot.RealizedPnl;
        view.UnrealizedPnl = lot.UnrealizedPnl;
        view.Pnl = lot.Pnl;
        view.PnlPercent = lot.PnlPercent;
        view.RemainingQty = lot.RemainingQty;
        view.ValuedAtCost = lot.ValuedAtCost;
        return view;
    }
}
