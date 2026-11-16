namespace TradeMonitor.ServiceInterface;

// Prediction contracts live in pm-<product id> wallets.
// pm-<product id>-2 is the same contract. Sync adds missing wallets and fills.
// It does not rewrite a wallet or a trade that is already stored.
public static class PredictionMarkets
{
    public const string HandPrefix = "pm-";
    public const string KalshiSuffix = "-KALSHI";

    public static bool IsHandWallet(string? key) =>
        !string.IsNullOrEmpty(key) && key.StartsWith(HandPrefix, StringComparison.OrdinalIgnoreCase);

    public static bool KeyBooks(string? walletKey, string? productId)
    {
        if (string.IsNullOrWhiteSpace(walletKey) || string.IsNullOrWhiteSpace(productId))
            return false;
        if (!IsHandWallet(walletKey))
            return false;
        var exact = HandPrefix + productId.Trim();
        if (walletKey.Equals(exact, StringComparison.OrdinalIgnoreCase))
            return true;
        var stem = exact + "-";
        if (!walletKey.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
            return false;
        return IsCopySuffix(walletKey[stem.Length..]);
    }

    public static string? MatchingKey(string productId, IEnumerable<string> walletKeys)
    {
        string? copy = null;
        foreach (var key in walletKeys)
        {
            if (string.IsNullOrEmpty(key) || !KeyBooks(key, productId))
                continue;
            if (key.Equals(HandPrefix + productId.Trim(), StringComparison.OrdinalIgnoreCase))
                return key;
            copy ??= key;
        }
        return copy;
    }

    static bool IsCopySuffix(string suffix)
    {
        if (suffix.Length is < 1 or > 6)
            return false;
        if (suffix[0] == '0')
            return false;
        for (var i = 0; i < suffix.Length; i++)
        {
            if (suffix[i] is < '0' or > '9')
                return false;
        }
        return true;
    }

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
