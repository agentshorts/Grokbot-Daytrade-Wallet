using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

public sealed partial class CoinbaseSyncer
{
    async Task BookPrediction(CbFill fill, CancellationToken cancellationToken)
    {
        var productId = fill.ProductId?.Trim() ?? "";
        if (productId.Length == 0)
        {
            NoteSkippedProduct(fill.ProductId);
            return;
        }

        if (bookingMode == PredictionBookingMode.Off)
        {
            NoteSkippedProduct(productId);
            return;
        }

        bookings?.Seen(productId, clock.UtcNow);
        var kind = PredictionBooking.KindOf(fill.TradeType);
        var clientId = PredictionBooking.ClientIdFor(fill.TradeId, kind);
        if (clientId == null)
        {
            FailPrediction(productId, "fill id is missing or too long");
            return;
        }

        if (fill.Size <= 0)
        {
            FailPrediction(productId, "zero quantity");
            return;
        }

        if (fill.SizeInQuote)
        {
            FailPrediction(productId, "size is in quote");
            return;
        }

        if (productId.Length > 64 || (PredictionMarkets.HandPrefix + productId).Length > 128)
        {
            FailPrediction(productId, "product id is too long");
            return;
        }

        if (AlreadyBooked(productId, kind, fill.TradeId, fill.OrderId))
        {
            bookedThisCycle.Add(productId);
            bookings?.Booked(productId, clock.UtcNow, FindBookedKey(productId));
            if (skippedThisCycle.Add("booked:" + clientId))
                log.Info("prediction fill already booked " + clientId);
            return;
        }

        var quote = await QuotePrediction(fill, cancellationToken);
        if (quote == null)
        {
            FailPrediction(productId, "missing cash total or YES/NO side");
            return;
        }

        var side = quote.Buy ? "Buy" : "Sell";
        if (bookingMode == PredictionBookingMode.DryRun)
        {
            var proposal = PredictionBooking.Proposal(
                clientId, quote.ContractSide, side, fill.Size, quote.Price, fill.Commission, fill.OrderId);
            bookings?.WouldBook(productId, clock.UtcNow, proposal);
            pendingMarks.Add(new PendingPrediction(productId, new PredictionTradeMark(clientId, fill.OrderId, kind)));
            bookedThisCycle.Add(productId);
            log.Info("would book " + proposal);
            return;
        }

        Wallet wallet;
        try
        {
            wallet = EnsurePredictionWallet(productId);
        }
        catch (LedgerToolException ex)
        {
            FailPrediction(productId, ex.Message);
            return;
        }

        try
        {
            ledger.RecordTrade(new RecordTrade
            {
                Wallet = wallet.Key,
                ClientId = clientId,
                Symbol = productId,
                Side = side == "Buy" ? TradeSide.Buy : TradeSide.Sell,
                Qty = fill.Size,
                Price = quote.Price,
                Fee = fill.Commission,
                Kind = kind,
                ExecutedAt = fill.TradeTimeUtc,
                Title = PredictionBooking.TradeTitle(quote.ContractSide, side, fill.Size),
                OrderRef = fill.OrderId,
                Note = PredictionBooking.TradeNote(quote.ContractSide, side, fill.TradeId),
            });
        }
        catch (LedgerToolException ex)
        {
            LoadLedger();
            if (AlreadyBooked(productId, kind, fill.TradeId, fill.OrderId))
            {
                bookedThisCycle.Add(productId);
                bookings?.Booked(productId, clock.UtcNow, FindBookedKey(productId) ?? wallet.Key);
                return;
            }

            FailPrediction(productId, ex.Message);
            return;
        }

        RememberFill(wallet.Id, new PredictionTradeMark(clientId, fill.OrderId, kind));
        bookedThisCycle.Add(productId);
        bookings?.Booked(productId, clock.UtcNow, wallet.Key);
        log.Info("recorded " + clientId + " " + quote.ContractSide + " " + side + " "
            + CoinbaseWallets.PythonG(fill.Size) + " " + productId
            + " @ " + CoinbaseWallets.PythonG(quote.Price)
            + " fee " + CoinbaseWallets.PythonG(fill.Commission));
    }

    async Task<PredictionQuote?> QuotePrediction(CbFill fill, CancellationToken cancellationToken)
    {
        var contractSide = PredictionBooking.ContractSide(fill.PredictionSide);
        var total = fill.TotalValueAfterFees;
        var fees = fill.Commission;
        var qty = fill.Size;
        if (contractSide == null || total == null)
        {
            var order = await CachedOrder(fill.OrderId, cancellationToken);
            if (order != null)
            {
                contractSide ??= PredictionBooking.ContractSide(order.PredictionSide);
                if (total == null && order.TotalValueAfterFees != null && order.FilledSize is > 0)
                {
                    total = order.TotalValueAfterFees;
                    fees = order.TotalFees ?? fees;
                    qty = order.FilledSize.Value;
                }
            }
        }

        if (contractSide == null || total == null)
            return null;
        var buy = PredictionBooking.LedgerIsBuy(fill.Side, contractSide);
        if (buy == null)
            return null;
        var price = PredictionBooking.Price(buy.Value, total.Value, fees, qty);
        if (price == null)
            return null;
        return new PredictionQuote(price.Value, contractSide, PredictionBooking.KindOf(fill.TradeType), buy.Value);
    }

    async Task<CbOrder?> CachedOrder(string? orderId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return null;
        if (orderCache.TryGetValue(orderId, out var cached))
            return cached;
        CbOrder? order = null;
        try
        {
            order = await coinbase.OrderAsync(orderId, cancellationToken);
        }
        catch (CoinbaseRejectedException)
        {
            order = null;
        }

        orderCache[orderId] = order;
        return order;
    }

    Wallet EnsurePredictionWallet(string productId)
    {
        var found = FindExactWallet(productId);
        if (found != null)
            return found;
        var created = ledger.CreateWallet(
            PredictionMarkets.HandPrefix + productId,
            PredictionBooking.WalletName(productId),
            PredictionBooking.WalletNote);
        wallets[created.Key] = created;
        return created;
    }

    Wallet? FindExactWallet(string productId)
    {
        var exact = PredictionMarkets.HandPrefix + productId;
        foreach (var wallet in wallets.Values)
        {
            if (wallet.Key.Equals(exact, StringComparison.OrdinalIgnoreCase))
                return wallet;
        }

        return null;
    }

    string? FindBookedKey(string productId) =>
        PredictionMarkets.MatchingKey(productId, wallets.Values.Select(wallet => wallet.Key));

    bool AlreadyBooked(string productId, TradeKind kind, string tradeId, string? orderId)
    {
        foreach (var pending in pendingMarks)
        {
            if (!pending.ProductId.Equals(productId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (PredictionBooking.IsAlreadyBooked(pending.Mark, kind, tradeId, orderId, productId))
                return true;
        }

        foreach (var wallet in wallets.Values)
        {
            if (!PredictionMarkets.KeyBooks(wallet.Key, productId))
                continue;
            if (!predictionFills.TryGetValue(wallet.Id, out var marks))
                continue;
            foreach (var mark in marks)
            {
                if (PredictionBooking.IsAlreadyBooked(mark, kind, tradeId, orderId, productId))
                    return true;
            }
        }

        return false;
    }

    void RememberFill(int walletId, PredictionTradeMark mark)
    {
        if (!predictionFills.TryGetValue(walletId, out var marks))
        {
            marks = new List<PredictionTradeMark>();
            predictionFills[walletId] = marks;
        }

        marks.Add(mark);
    }

    void FailPrediction(string productId, string reason)
    {
        var text = reason.Length > 300 ? reason[..300] : reason;
        bookings?.Problem(productId, clock.UtcNow, text);
        NoteSkippedProduct(productId);
    }

    sealed record PredictionQuote(decimal Price, string ContractSide, TradeKind Kind, bool Buy);

    sealed record PendingPrediction(string ProductId, PredictionTradeMark Mark);
}
