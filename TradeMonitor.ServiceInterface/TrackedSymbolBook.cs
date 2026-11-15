using ServiceStack;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

public sealed record TrackedRow(
    string ProductId,
    string Name,
    int SortOrder,
    bool Listed,
    decimal BaseIncrement,
    decimal QuoteIncrement,
    decimal BaseMinSize,
    decimal MinMarketFunds);

// The chart list, plus any symbol that still has an open order or a held position.
public sealed class TrackedSymbolBook(IDbConnectionFactory dbFactory, ILiveMarket market, ILedgerClock clock)
{
    public bool CatalogLoaded => market.Products().Count > 0;

    public List<TrackedRow> List()
    {
        using var db = dbFactory.Open();
        return db.Select<TrackedSymbol>()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.ProductId, StringComparer.Ordinal)
            .Select(ToRow)
            .ToList();
    }

    public List<TrackedRow> Add(string? product)
    {
        var id = Clean(product);
        if (!CatalogLoaded)
            throw Fail(400, "NoProducts", "The public product list is not loaded yet.");
        if (!market.IsListed(id))
            throw Fail(400, "UnknownProduct", $"Unknown USD product '{id}'.");
        using var db = dbFactory.Open();
        if (!db.Exists<TrackedSymbol>(x => x.ProductId == id))
        {
            var next = db.Select<TrackedSymbol>().Select(x => x.SortOrder).DefaultIfEmpty(-1).Max() + 1;
            db.Insert(new TrackedSymbol
            {
                ProductId = id,
                SortOrder = next,
                CreatedAt = clock.UtcNow,
            });
        }

        return List();
    }

    public List<TrackedRow> Remove(string? product)
    {
        var id = Clean(product);
        using var db = dbFactory.Open();
        db.Delete<TrackedSymbol>(x => x.ProductId == id);
        return List();
    }

    public List<TrackedRow> Reorder(IReadOnlyList<string>? products)
    {
        var wanted = (products ?? [])
            .Select(x => x?.Trim().ToUpperInvariant() ?? "")
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        using var db = dbFactory.Open();
        var rows = db.Select<TrackedSymbol>();
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < wanted.Count; i++)
            rank[wanted[i]] = i;
        var ordered = rows
            .OrderBy(x => rank.TryGetValue(x.ProductId, out var n) ? n : int.MaxValue)
            .ThenBy(x => x.SortOrder)
            .ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SortOrder = i;
            db.Update(ordered[i]);
        }

        return List();
    }

    public List<string> PollSymbols()
    {
        using var db = dbFactory.Open();
        var symbols = new List<string>();
        foreach (var row in db.Select<TrackedSymbol>().OrderBy(x => x.SortOrder).ThenBy(x => x.ProductId))
            Add(symbols, row.ProductId);
        foreach (var order in db.Select<PaperOrder>(x => x.Status == LiveOrderStatus.Open))
            Add(symbols, order.Symbol);
        foreach (var position in db.Select<Position>(x => x.Qty > 0))
            Add(symbols, position.Symbol);
        return symbols;
    }

    void Add(List<string> symbols, string? raw)
    {
        var text = raw?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(text))
            return;
        if (symbols.Exists(x => x.Equals(text, StringComparison.OrdinalIgnoreCase)))
            return;
        if (CatalogLoaded && !market.IsListed(text))
            return;
        symbols.Add(text);
    }

    TrackedRow ToRow(TrackedSymbol row)
    {
        var listed = !CatalogLoaded || market.IsListed(row.ProductId);
        var known = market.Products().FirstOrDefault(x => x.ProductId.Equals(row.ProductId, StringComparison.OrdinalIgnoreCase));
        return new TrackedRow(
            row.ProductId,
            known?.Name ?? row.ProductId,
            row.SortOrder,
            listed,
            known?.BaseIncrement ?? 0,
            known?.QuoteIncrement ?? 0,
            known?.BaseMinSize ?? 0,
            known?.MinMarketFunds ?? 0);
    }

    static string Clean(string? product)
    {
        var text = product?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(text))
            throw Fail(400, "InvalidRequest", "Symbol is required.");
        return text;
    }

    static HttpError Fail(int status, string code, string message) => new(status, code, message);
}
