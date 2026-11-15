namespace TradeMonitor.ServiceInterface;

// Coinbase prediction contracts are logged by hand into pm-<product id> wallets.
// The sync detects the same products and leaves them alone.
public static class PredictionMarkets
{
    public const string HandPrefix = "pm-";
    public const string KalshiSuffix = "-KALSHI";

    public static bool IsHandWallet(string? key) =>
        !string.IsNullOrEmpty(key) && key.StartsWith(HandPrefix, StringComparison.OrdinalIgnoreCase);

    public static bool IsProductId(string? productId) =>
        !string.IsNullOrEmpty(productId) && productId.EndsWith(KalshiSuffix, StringComparison.OrdinalIgnoreCase);

    public static bool IsKalshiSlug(string? key) =>
        !string.IsNullOrEmpty(key) && key.EndsWith("-kalshi", StringComparison.OrdinalIgnoreCase);

    public static bool FieldsLookPrediction(string? productType, string? productVenue, string? venue) =>
        Looks(productType) || Looks(productVenue) || Looks(venue);

    public static bool ProductLooksPrediction(CbProduct? product, string? productId)
    {
        if (IsProductId(productId))
            return true;
        if (product == null)
            return false;
        if (product.PredictionDetails)
            return true;
        return FieldsLookPrediction(product.ProductType, product.ProductVenue, product.Venue);
    }

    static bool Looks(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (value.Contains("PREDICTION", StringComparison.OrdinalIgnoreCase))
            return true;
        return value.Equals("KALSHI", StringComparison.OrdinalIgnoreCase);
    }
}
