using System.Data;
using System.Globalization;
using ServiceStack;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

// Cash = deposits - withdrawals - sweeps - buy cost (qty * price + fee) + sell proceeds (qty * price - fee).
// A buy may leave cash negative only when the wallet has no deposit yet, so a real wallet with an
// unknown starting balance can still be recorded. The balances response flags that with CashIsNegative.
public class LedgerServices(IDbConnectionFactory dbFactory, ILedgerClock clock) : Service
{
    const int DefaultTake = 100;
    const int MaxTake = 1000;
    const int MaxMarks = 500;
    const int SyntheticId = int.MaxValue - 1;

    static readonly string[] HistoryKinds = ["buy", "sell", "deposit", "withdrawal", "sweep"];

    public object Any(CreateWallet request)
    {
        var draft = NormalizeWallet(request);
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                return WriteWallet(draft);
            }
            catch (UniqueHit) when (attempt == 1)
            {
            }
        }

        throw Fail(409, "DifferentValues", $"Wallet '{draft.Key}' already exists with different values.");
    }

    public object Any(UpdateWallet request)
    {
        using var db = dbFactory.Open();
        var wallet = RequireWallet(db, request.Key);
        if (request.Name != null)
            wallet.Name = RequireText(request.Name, "Wallet name", Limits.Name);
        if (request.Note != null)
            wallet.Note = RequireNote(request.Note);
        if (request.SortOrder is int sort)
            wallet.SortOrder = sort;
        if (request.IsArchived is bool archived)
            wallet.IsArchived = archived;
        db.Update(wallet);
        return new UpdateWalletResponse { Result = db.SingleById<Wallet>(wallet.Id) };
    }

    public object Any(GetWallet request)
    {
        using var db = dbFactory.Open();
        return new GetWalletResponse { Result = RequireWallet(db, request.Key) };
    }

    public object Any(QueryWallets request)
    {
        using var db = dbFactory.Open();
        var rows = request.IncludeArchived
            ? db.Select<Wallet>()
            : db.Select<Wallet>(x => x.IsArchived == false);
        return new QueryWalletsResponse
        {
            Results = rows
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name, StringComparer.Ordinal)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .ToList(),
        };
    }

    public object Any(GetWalletBalances request)
    {
        using var db = dbFactory.Open();
        var wallet = RequireWallet(db, request.Wallet);
        return new GetWalletBalancesResponse { Result = ReadBalances(db, wallet) };
    }

    public object Any(QueryWalletHistory request)
    {
        var from = request.From is DateTime start ? AsUtc(start) : (DateTime?)null;
        var to = request.To is DateTime end ? AsUtc(end) : (DateTime?)null;
        EnsureRange(from, to);
        var kind = ParseKind(request.Kind);
        var skip = NormalizeSkip(request.Skip);
        var take = NormalizeTake(request.Take);

        using var db = dbFactory.Open();
        var wallet = RequireWallet(db, request.Wallet);
        var entries = new List<WalletHistoryEntry>();
        foreach (var trade in db.Select<Trade>(x => x.WalletId == wallet.Id))
        {
            entries.Add(new WalletHistoryEntry
            {
                Wallet = wallet.Key,
                Kind = trade.Side.ToString(),
                Id = trade.Id,
                ClientId = trade.ClientId,
                At = trade.ExecutedAt,
                Symbol = trade.Symbol,
                Side = trade.Side,
                Qty = trade.Qty,
                Price = trade.Price,
                Fee = trade.Fee,
                Currency = trade.FeeCurrency,
                Title = trade.Title,
                Note = trade.Note,
                OrderRef = trade.OrderRef,
                TargetPrice = trade.TargetPrice,
                RecordedAt = trade.RecordedAt,
            });
        }

        foreach (var transfer in db.Select<Transfer>(x => x.WalletId == wallet.Id))
        {
            entries.Add(new WalletHistoryEntry
            {
                Wallet = wallet.Key,
                Kind = transfer.Kind.ToString(),
                Id = transfer.Id,
                ClientId = transfer.ClientId,
                At = transfer.At,
                Amount = transfer.Amount,
                Currency = transfer.Currency,
                Note = transfer.Note,
                RecordedAt = transfer.RecordedAt,
            });
        }

        IEnumerable<WalletHistoryEntry> filtered = entries;
        if (from is DateTime fromAt)
            filtered = filtered.Where(x => x.At >= fromAt);
        if (to is DateTime toAt)
            filtered = filtered.Where(x => x.At <= toAt);
        if (kind != null)
            filtered = filtered.Where(x => x.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase));

        var ordered = filtered
            .OrderByDescending(x => x.At)
            .ThenByDescending(x => x.RecordedAt)
            .ThenByDescending(x => x.Id)
            .ThenBy(x => x.Kind, StringComparer.Ordinal)
            .ToList();

        return new QueryWalletHistoryResponse
        {
            Total = ordered.Count,
            Skip = skip,
            Take = take,
            Results = ordered.Skip(skip).Take(take).ToList(),
        };
    }

    public object Any(QueryTrades request)
    {
        var from = request.From is DateTime start ? AsUtc(start) : (DateTime?)null;
        var to = request.To is DateTime end ? AsUtc(end) : (DateTime?)null;
        EnsureRange(from, to);
        var skip = NormalizeSkip(request.Skip);
        var take = NormalizeTake(request.Take);
        var symbol = Clean(request.Symbol)?.ToUpperInvariant();
        if (symbol?.Length > Limits.Symbol)
            throw Fail(400, "InvalidRequest", $"Symbol cannot be longer than {Limits.Symbol} characters.");
        if (request.Side is TradeSide given && !Enum.IsDefined(given))
            throw Fail(400, "InvalidRequest", "Trade side must be Buy or Sell.");
        var mode = ParseMode(request.Mode);
        var outcome = ParseOutcome(request.Outcome);
        var order = ParseOrder(request.OrderBy);
        var descending = request.Descending ?? true;
        var search = Clean(request.Q);

        using var db = dbFactory.Open();
        var wallet = TryWallet(db, request.Wallet);
        var views = new List<TradeView>();
        foreach (var book in LedgerData.Load(db, Now()))
        {
            if (wallet != null && book.Wallet.Id != wallet.Id)
                continue;
            if (mode is WalletMode wanted && book.Wallet.Mode != wanted)
                continue;
            var lots = book.Replay.LotTrades.ToDictionary(x => x.TradeId);
            foreach (var trade in book.Trades)
            {
                if (symbol != null && trade.Symbol != symbol)
                    continue;
                if (request.Side is TradeSide side && trade.Side != side)
                    continue;
                if (from is DateTime fromAt && trade.ExecutedAt < fromAt)
                    continue;
                if (to is DateTime toAt && trade.ExecutedAt > toAt)
                    continue;
                lots.TryGetValue(trade.Id, out var lot);
                var view = TradeMaps.ToView(trade, book.Wallet, lot);
                if (!MatchesSearch(view, search) || !MatchesOutcome(view, outcome))
                    continue;
                views.Add(view);
            }
        }

        views.Sort((a, b) => CompareTrades(a, b, order, descending));
        return new QueryTradesResponse
        {
            Total = views.Count,
            Skip = skip,
            Take = take,
            Results = views.Skip(skip).Take(take).ToList(),
        };
    }

    public object Any(RebuildProjections request)
    {
        var now = Now();
        using var db = dbFactory.Open();
        using var tx = db.OpenTransaction();
        var wallets = Clean(request.Wallet) == null
            ? db.Select<Wallet>()
            : [RequireWallet(db, request.Wallet)];
        foreach (var wallet in wallets)
            ReplaceProjections(db, wallet.Id, now);

        var positions = 0;
        var snapshots = 0;
        foreach (var wallet in wallets)
        {
            positions += db.Select<Position>(x => x.WalletId == wallet.Id).Count;
            snapshots += db.Select<BalanceSnapshot>(x => x.WalletId == wallet.Id).Count;
        }

        tx.Commit();
        return new RebuildProjectionsResponse
        {
            Wallets = wallets.Count,
            Positions = positions,
            Snapshots = snapshots,
        };
    }

    public object Any(QueryPriceMarks request)
    {
        var from = request.From is DateTime start ? AsUtc(start) : (DateTime?)null;
        var to = request.To is DateTime end ? AsUtc(end) : (DateTime?)null;
        EnsureRange(from, to);
        var skip = NormalizeSkip(request.Skip);
        var take = NormalizeTake(request.Take);
        var symbol = Clean(request.Symbol)?.ToUpperInvariant();

        using var db = dbFactory.Open();
        var wallet = TryWallet(db, request.Wallet);
        var wallets = db.Select<Wallet>().ToDictionary(x => x.Id);
        IEnumerable<PriceMark> rows = db.Select<PriceMark>();
        if (wallet != null)
            rows = rows.Where(x => x.WalletId == null || x.WalletId == wallet.Id);
        if (symbol != null)
            rows = rows.Where(x => x.Symbol == symbol);
        if (from is DateTime fromAt)
            rows = rows.Where(x => x.At >= fromAt);
        if (to is DateTime toAt)
            rows = rows.Where(x => x.At <= toAt);

        var ordered = rows.OrderByDescending(x => x.At).ThenByDescending(x => x.Id).ToList();
        return new QueryPriceMarksResponse
        {
            Total = ordered.Count,
            Skip = skip,
            Take = take,
            Results = ordered.Skip(skip).Take(take).Select(mark => ToView(mark, wallets)).ToList(),
        };
    }

    public object Any(RecordTrade request)
    {
        var draft = NormalizeTrade(request);
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                return WriteTrade(draft);
            }
            catch (UniqueHit) when (attempt == 1)
            {
            }
        }

        throw Fail(409, "DifferentValues",
            $"Trade client id '{draft.ClientId}' on wallet '{draft.Wallet}' already exists with different values.");
    }

    public object Any(RecordTransfer request)
    {
        var draft = NormalizeTransfer(request);
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                return WriteTransfer(draft);
            }
            catch (UniqueHit) when (attempt == 1)
            {
            }
        }

        throw Fail(409, "DifferentValues",
            $"Transfer client id '{draft.ClientId}' on wallet '{draft.Wallet}' already exists with different values.");
    }

    public object Any(RecordPriceMark request)
    {
        var saved = WriteMarks([NormalizeMark(request.Symbol, request.Price, request.At, request.Wallet, request.ClientId, request.Source)], prefixErrors: false);
        var mark = saved[0];
        return new RecordPriceMarkResponse { Result = mark.Mark, AlreadyRecorded = mark.AlreadyRecorded };
    }

    public object Any(RecordPriceMarks request)
    {
        if (request.Marks == null || request.Marks.Count == 0)
            throw Fail(400, "InvalidRequest", "At least one price mark is required.");
        if (request.Marks.Count > MaxMarks)
            throw Fail(400, "InvalidRequest", $"A batch can contain at most {MaxMarks} price marks.");

        var drafts = request.Marks
            .Select(mark => NormalizeMark(mark.Symbol, mark.Price, mark.At, mark.Wallet, mark.ClientId, mark.Source))
            .ToList();
        var saved = WriteMarks(drafts, prefixErrors: true);
        return new RecordPriceMarksResponse
        {
            Results = saved.Select(x => new RecordedPriceMark
            {
                Result = x.Mark,
                AlreadyRecorded = x.AlreadyRecorded,
            }).ToList(),
        };
    }

    CreateWalletResponse WriteWallet(WalletDraft draft)
    {
        var now = Now();
        using var db = dbFactory.Open();
        using var tx = db.OpenTransaction();
        var existing = db.Single<Wallet>(x => x.Key == draft.Key);
        if (existing != null)
        {
            if (!SameWallet(existing, draft))
                throw Fail(409, "DifferentValues", $"Wallet '{draft.Key}' already exists with different values.");
            tx.Commit();
            return new CreateWalletResponse { Result = existing, AlreadyExists = true };
        }

        var wallet = new Wallet
        {
            Key = draft.Key,
            Name = draft.Name,
            Mode = draft.Mode,
            Venue = draft.Venue,
            BaseCurrency = draft.BaseCurrency,
            Note = draft.Note,
            IsArchived = false,
            SortOrder = 0,
            CreatedAt = now,
        };
        try
        {
            wallet.Id = (int)db.Insert(wallet, selectIdentity: true);
        }
        catch (Exception ex) when (IsUnique(ex))
        {
            throw new UniqueHit();
        }

        var stored = db.SingleById<Wallet>(wallet.Id);
        tx.Commit();
        return new CreateWalletResponse { Result = stored };
    }

    RecordTradeResponse WriteTrade(TradeDraft draft)
    {
        var now = Now();
        using var db = dbFactory.Open();
        using var tx = db.OpenTransaction();
        var wallet = RequireWallet(db, draft.Wallet);
        var existing = db.Single<Trade>(x => x.WalletId == wallet.Id && x.ClientId == draft.ClientId);
        if (existing != null)
        {
            if (!SameTrade(existing, draft))
                throw Fail(409, "DifferentValues",
                    $"Trade client id '{draft.ClientId}' on wallet '{wallet.Key}' already exists with different values.");
            var replay = ReadBalances(db, wallet);
            tx.Commit();
            return new RecordTradeResponse { Result = existing, AlreadyRecorded = true, Balance = replay };
        }

        var feeCurrency = draft.ProvidedFeeCurrency ?? wallet.BaseCurrency;
        if (feeCurrency != wallet.BaseCurrency)
            throw Fail(400, "InvalidRequest",
                $"Fee currency '{feeCurrency}' does not match the {wallet.BaseCurrency} base currency of wallet '{wallet.Key}'.");

        var trade = new Trade
        {
            WalletId = wallet.Id,
            ClientId = draft.ClientId,
            Symbol = draft.Symbol,
            Title = draft.Title,
            Side = draft.Side,
            Qty = draft.Qty,
            Price = draft.Price,
            Fee = draft.Fee,
            FeeCurrency = feeCurrency,
            ExecutedAt = draft.ExecutedAt,
            OrderRef = draft.OrderRef,
            TargetPrice = draft.TargetPrice,
            Note = draft.Note,
            RecordedAt = now,
            Id = SyntheticId,
        };
        EnsureTradeAllowed(db, wallet, trade);
        trade.Id = 0;
        try
        {
            trade.Id = (int)db.Insert(trade, selectIdentity: true);
        }
        catch (Exception ex) when (IsUnique(ex))
        {
            throw new UniqueHit();
        }

        ReplaceProjections(db, wallet.Id, now);
        var balance = ReadBalances(db, wallet);
        var stored = db.SingleById<Trade>(trade.Id);
        tx.Commit();
        return new RecordTradeResponse
        {
            Result = stored,
            AlreadyRecorded = false,
            Balance = balance,
        };
    }

    RecordTransferResponse WriteTransfer(TransferDraft draft)
    {
        var now = Now();
        using var db = dbFactory.Open();
        using var tx = db.OpenTransaction();
        var wallet = RequireWallet(db, draft.Wallet);
        var existing = db.Single<Transfer>(x => x.WalletId == wallet.Id && x.ClientId == draft.ClientId);
        if (existing != null)
        {
            if (!SameTransfer(existing, draft))
                throw Fail(409, "DifferentValues",
                    $"Transfer client id '{draft.ClientId}' on wallet '{wallet.Key}' already exists with different values.");
            var replay = ReadBalances(db, wallet);
            tx.Commit();
            return new RecordTransferResponse { Result = existing, AlreadyRecorded = true, Balance = replay };
        }

        var currency = draft.ProvidedCurrency ?? wallet.BaseCurrency;
        if (currency != wallet.BaseCurrency)
            throw Fail(400, "InvalidRequest",
                $"Transfer currency '{currency}' does not match the {wallet.BaseCurrency} base currency of wallet '{wallet.Key}'.");

        var transfer = new Transfer
        {
            WalletId = wallet.Id,
            ClientId = draft.ClientId,
            Kind = draft.Kind,
            Amount = draft.Amount,
            Currency = currency,
            At = draft.At,
            Note = draft.Note,
            RecordedAt = now,
            Id = SyntheticId,
        };
        EnsureTransferAllowed(db, wallet, transfer);
        transfer.Id = 0;
        try
        {
            transfer.Id = (int)db.Insert(transfer, selectIdentity: true);
        }
        catch (Exception ex) when (IsUnique(ex))
        {
            throw new UniqueHit();
        }

        ReplaceProjections(db, wallet.Id, now);
        var balance = ReadBalances(db, wallet);
        var stored = db.SingleById<Transfer>(transfer.Id);
        tx.Commit();
        return new RecordTransferResponse
        {
            Result = stored,
            AlreadyRecorded = false,
            Balance = balance,
        };
    }

    List<MarkSave> WriteMarks(IReadOnlyList<MarkDraft> drafts, bool prefixErrors)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                return WriteMarksOnce(drafts, prefixErrors);
            }
            catch (UniqueHit) when (attempt == 1)
            {
            }
        }

        throw Fail(409, "DifferentValues", "A price mark client id was recorded concurrently with different values.");
    }

    List<MarkSave> WriteMarksOnce(IReadOnlyList<MarkDraft> drafts, bool prefixErrors)
    {
        var now = Now();
        using var db = dbFactory.Open();
        using var tx = db.OpenTransaction();
        var saved = new List<MarkSave>(drafts.Count);
        for (var i = 0; i < drafts.Count; i++)
        {
            try
            {
                saved.Add(SaveMark(db, drafts[i], now));
            }
            catch (HttpError ex) when (prefixErrors)
            {
                throw new HttpError(ex.Status, ex.ErrorCode, $"Price mark {i + 1}: {ex.Message}");
            }
        }

        if (saved.Any(x => !x.AlreadyRecorded))
        {
            if (saved.Any(x => !x.AlreadyRecorded && x.Global))
            {
                foreach (var wallet in db.Select<Wallet>())
                    ReplaceProjections(db, wallet.Id, now);
            }
            else
            {
                foreach (var walletId in saved.Where(x => !x.AlreadyRecorded && x.WalletId != null).Select(x => x.WalletId!.Value).Distinct())
                    ReplaceProjections(db, walletId, now);
            }
        }

        tx.Commit();
        return saved;
    }

    MarkSave SaveMark(IDbConnection db, MarkDraft draft, DateTime now)
    {
        if (draft.ClientId != null)
        {
            var existing = db.Single<PriceMark>(x => x.ClientId == draft.ClientId);
            if (existing != null)
            {
                if (!SameMark(db, existing, draft))
                    throw Fail(409, "DifferentValues",
                        $"Price mark client id '{draft.ClientId}' already exists with different values.");
                return new MarkSave { Mark = existing, AlreadyRecorded = true, Global = existing.WalletId == null, WalletId = existing.WalletId };
            }
        }

        int? walletId = null;
        if (draft.Wallet != null)
            walletId = RequireWallet(db, draft.Wallet).Id;

        var mark = new PriceMark
        {
            ClientId = draft.ClientId,
            Symbol = draft.Symbol,
            Price = draft.Price,
            At = draft.At,
            Source = draft.Source,
            WalletId = walletId,
            RecordedAt = now,
        };
        try
        {
            mark.Id = (int)db.Insert(mark, selectIdentity: true);
        }
        catch (Exception ex) when (IsUnique(ex))
        {
            throw new UniqueHit();
        }

        return new MarkSave
        {
            Mark = db.SingleById<PriceMark>(mark.Id),
            AlreadyRecorded = false,
            Global = walletId == null,
            WalletId = walletId,
        };
    }

    void EnsureTradeAllowed(IDbConnection db, Wallet wallet, Trade candidate)
    {
        var trades = db.Select<Trade>(x => x.WalletId == wallet.Id);
        var transfers = db.Select<Transfer>(x => x.WalletId == wallet.Id);
        var marks = ApplicableMarks(db, wallet.Id);
        var before = ReplayOrCorrupt(wallet, trades, transfers, marks);
        var after = LedgerBook.Replay(wallet.Id, trades.Append(candidate), transfers, marks, Now());
        if (after.Oversold)
            throw Fail(400, "Oversell",
                $"Sell of {Num(candidate.Qty)} {candidate.Symbol} exceeds the open quantity of {Num(after.OpenQty)} in wallet '{wallet.Key}'.");

        if (candidate.Side == TradeSide.Buy && after.Cash < 0 && after.HasDeposits)
        {
            var cost = candidate.Qty * candidate.Price + candidate.Fee;
            throw Fail(400, "InsufficientCash",
                $"Buy costs {Num(cost)} {wallet.BaseCurrency}, more than the cash of {Num(before.Cash)} {wallet.BaseCurrency} in wallet '{wallet.Key}'.");
        }

        if (candidate.Side == TradeSide.Sell && after.HasDeposits && after.Cash < 0 && after.Cash < before.Cash)
            throw Fail(400, "InsufficientCash", $"This sell would leave wallet '{wallet.Key}' with negative cash.");
    }

    void EnsureTransferAllowed(IDbConnection db, Wallet wallet, Transfer candidate)
    {
        var trades = db.Select<Trade>(x => x.WalletId == wallet.Id);
        var transfers = db.Select<Transfer>(x => x.WalletId == wallet.Id);
        var marks = ApplicableMarks(db, wallet.Id);
        var before = ReplayOrCorrupt(wallet, trades, transfers, marks);
        if (candidate.Kind == TransferKind.Sweep && candidate.Amount > before.Cash)
            throw Fail(400, "InsufficientCash",
                $"Sweep of {Num(candidate.Amount)} {wallet.BaseCurrency} exceeds the cash of {Num(before.Cash)} {wallet.BaseCurrency} in wallet '{wallet.Key}'.");
        if (candidate.Kind == TransferKind.Withdrawal && candidate.Amount > before.Cash)
            throw Fail(400, "InsufficientCash",
                $"Withdrawal of {Num(candidate.Amount)} {wallet.BaseCurrency} exceeds the cash of {Num(before.Cash)} {wallet.BaseCurrency} in wallet '{wallet.Key}'.");
    }

    LedgerReplay ReplayOrCorrupt(Wallet wallet, IEnumerable<Trade> trades, IEnumerable<Transfer> transfers, IEnumerable<PriceMark> marks)
    {
        var replay = LedgerBook.Replay(wallet.Id, trades, transfers, marks, Now());
        if (replay.Oversold)
            throw Fail(500, "LedgerCorrupt",
                $"Stored trades oversell {replay.OversellSymbol} in wallet '{wallet.Key}'.");
        return replay;
    }

    List<PriceMark> ApplicableMarks(IDbConnection db, int walletId) =>
        db.Select<PriceMark>(x => x.WalletId == null || x.WalletId == walletId);

    void ReplaceProjections(IDbConnection db, int walletId, DateTime updatedAt)
    {
        var wallet = db.SingleById<Wallet>(walletId);
        var replay = LedgerBook.Replay(
            walletId,
            db.Select<Trade>(x => x.WalletId == walletId),
            db.Select<Transfer>(x => x.WalletId == walletId),
            ApplicableMarks(db, walletId),
            updatedAt);
        if (replay.Oversold)
            throw Fail(500, "LedgerCorrupt",
                $"Stored trades oversell {replay.OversellSymbol} in wallet '{wallet.Key}'.");

        db.Delete<Position>(x => x.WalletId == walletId);
        db.Delete<BalanceSnapshot>(x => x.WalletId == walletId);
        foreach (var position in replay.Positions)
            db.Insert(position);
        foreach (var snapshot in replay.Snapshots)
            db.Insert(snapshot);
    }

    WalletBalances ReadBalances(IDbConnection db, Wallet wallet)
    {
        var positions = db.Select<Position>(x => x.WalletId == wallet.Id);
        var snapshot = db.Select<BalanceSnapshot>(x => x.WalletId == wallet.Id)
            .OrderByDescending(x => x.Id)
            .FirstOrDefault();
        var hasDeposits = db.Exists<Transfer>(x => x.WalletId == wallet.Id && x.Kind == TransferKind.Deposit);
        var cash = snapshot?.Cash ?? 0;
        decimal? unrealized = 0;
        var valuedAtCost = false;
        var open = new List<PositionBalance>();
        foreach (var position in positions.OrderBy(x => x.Symbol, StringComparer.Ordinal))
        {
            if (position.Qty <= 0)
                continue;

            decimal market;
            decimal? rowUnrealized;
            var rowAtCost = position.LastMarkPrice is null;
            if (position.LastMarkPrice is decimal mark)
            {
                market = position.Qty * mark;
                rowUnrealized = market - position.CostBasis;
            }
            else
            {
                market = position.CostBasis;
                rowUnrealized = null;
                valuedAtCost = true;
            }

            if (rowUnrealized is null)
                unrealized = null;
            else if (unrealized is decimal sum)
                unrealized += rowUnrealized.Value;

            decimal? percent = rowUnrealized is decimal pnl && position.CostBasis != 0
                ? pnl / position.CostBasis * 100m
                : null;
            open.Add(new PositionBalance
            {
                Symbol = position.Symbol,
                Qty = position.Qty,
                CostBasis = position.CostBasis,
                AvgPrice = position.AvgPrice,
                LastMarkPrice = position.LastMarkPrice,
                LastMarkAt = position.LastMarkAt,
                UnrealizedPnl = rowUnrealized,
                UnrealizedPnlPercent = percent,
                MarketValue = market,
                ValuedAtCost = rowAtCost,
                RealizedPnl = position.RealizedPnl,
                FeesPaid = position.FeesPaid,
            });
        }

        if (open.Count == 0)
            unrealized = 0;

        return new WalletBalances
        {
            Id = wallet.Id,
            Key = wallet.Key,
            Name = wallet.Name,
            Mode = wallet.Mode,
            BaseCurrency = wallet.BaseCurrency,
            IsArchived = wallet.IsArchived,
            Cash = cash,
            Swept = snapshot?.Swept ?? 0,
            Fees = positions.Sum(x => x.FeesPaid),
            RealizedPnl = positions.Sum(x => x.RealizedPnl),
            UnrealizedPnl = unrealized,
            PositionsValue = snapshot?.PositionsValue ?? 0,
            TotalValue = snapshot?.TotalValue ?? 0,
            CashIsNegative = cash < 0,
            HasDeposits = hasDeposits,
            ValuedAtCost = valuedAtCost,
            Positions = open,
        };
    }

    static PriceMarkView ToView(PriceMark mark, Dictionary<int, Wallet> wallets) => new()
    {
        Id = mark.Id,
        ClientId = mark.ClientId,
        Symbol = mark.Symbol,
        Price = mark.Price,
        At = mark.At,
        Source = mark.Source,
        WalletId = mark.WalletId,
        Wallet = mark.WalletId is int id && wallets.TryGetValue(id, out var wallet) ? wallet.Key : null,
        RecordedAt = mark.RecordedAt,
    };

    Wallet RequireWallet(IDbConnection db, string? key)
    {
        var normalized = RequireKey(key);
        return db.Single<Wallet>(x => x.Key == normalized)
            ?? throw Fail(404, "UnknownWallet", $"Unknown wallet '{normalized}'.");
    }

    Wallet? TryWallet(IDbConnection db, string? key)
    {
        if (Clean(key) == null)
            return null;
        return RequireWallet(db, key);
    }

    WalletDraft NormalizeWallet(CreateWallet request)
    {
        var currency = (Clean(request.BaseCurrency) ?? "USD").ToUpperInvariant();
        if (currency.Length > Limits.Currency)
            throw Fail(400, "InvalidRequest", $"Base currency cannot be longer than {Limits.Currency} characters.");
        var venue = Clean(request.Venue);
        if (venue?.Length > Limits.Venue)
            throw Fail(400, "InvalidRequest", $"Venue cannot be longer than {Limits.Venue} characters.");
        return new WalletDraft(
            RequireKey(request.Key),
            RequireText(request.Name, "Wallet name", Limits.Name),
            RequireMode(request.Mode),
            venue,
            currency,
            RequireNote(request.Note));
    }

    TradeDraft NormalizeTrade(RecordTrade request)
    {
        var qty = request.Qty;
        var price = request.Price;
        var fee = request.Fee;
        RequirePositive(qty, "Quantity");
        RequirePositive(price, "Price");
        RequireNonNegative(fee, "Fee");
        try
        {
            _ = qty * price + fee;
        }
        catch (OverflowException)
        {
            throw Fail(400, "InvalidRequest", "Quantity, price, and fee are too large to record.");
        }

        var providedFeeCurrency = Clean(request.FeeCurrency)?.ToUpperInvariant();
        if (providedFeeCurrency?.Length > Limits.Currency)
            throw Fail(400, "InvalidRequest", $"Fee currency cannot be longer than {Limits.Currency} characters.");

        if (request.TargetPrice is decimal target && target <= 0)
            throw Fail(400, "InvalidRequest", "Target price must be greater than zero.");

        var now = Now();
        return new TradeDraft(
            RequireKey(request.Wallet),
            RequireText(request.ClientId, "Client id", Limits.ClientId),
            RequireSymbol(request.Symbol),
            RequireSide(request.Side),
            qty,
            price,
            fee,
            providedFeeCurrency,
            request.ExecutedAt is DateTime at ? AsUtc(at) : null,
            request.ExecutedAt is DateTime executed ? AsUtc(executed) : now,
            RequireOptional(request.Title, "Title", Limits.Title),
            RequireOptional(request.OrderRef, "Order ref", Limits.OrderRef),
            request.TargetPrice,
            RequireNote(request.Note));
    }

    TransferDraft NormalizeTransfer(RecordTransfer request)
    {
        RequirePositive(request.Amount, "Amount");
        var currency = Clean(request.Currency)?.ToUpperInvariant();
        if (currency?.Length > Limits.Currency)
            throw Fail(400, "InvalidRequest", $"Currency cannot be longer than {Limits.Currency} characters.");
        var now = Now();
        return new TransferDraft(
            RequireKey(request.Wallet),
            RequireText(request.ClientId, "Client id", Limits.ClientId),
            RequireKind(request.Kind),
            request.Amount,
            currency,
            request.At is DateTime at ? AsUtc(at) : null,
            request.At is DateTime when ? AsUtc(when) : now,
            RequireNote(request.Note));
    }

    MarkDraft NormalizeMark(string? symbol, decimal price, DateTime? at, string? wallet, string? clientId, string? source)
    {
        RequirePositive(price, "Price");
        var key = Clean(wallet);
        if (key?.Length > Limits.Key)
            throw Fail(400, "InvalidRequest", $"Wallet key cannot be longer than {Limits.Key} characters.");
        var id = Clean(clientId);
        if (id?.Length > Limits.ClientId)
            throw Fail(400, "InvalidRequest", $"Client id cannot be longer than {Limits.ClientId} characters.");
        var now = Now();
        return new MarkDraft(
            RequireSymbol(symbol),
            price,
            at is DateTime given ? AsUtc(given) : null,
            at is DateTime when ? AsUtc(when) : now,
            key,
            id,
            RequireOptional(source, "Source", Limits.Source));
    }

    static bool SameWallet(Wallet row, WalletDraft draft) =>
        row.Name == draft.Name
        && row.Mode == draft.Mode
        && Clean(row.Venue) == draft.Venue
        && string.Equals(row.BaseCurrency, draft.BaseCurrency, StringComparison.Ordinal)
        && Clean(row.Note) == draft.Note;

    static bool SameTrade(Trade row, TradeDraft draft)
    {
        if (!string.Equals(row.Symbol, draft.Symbol, StringComparison.Ordinal))
            return false;
        if (row.Side != draft.Side || row.Qty != draft.Qty || row.Price != draft.Price || row.Fee != draft.Fee)
            return false;
        if (Clean(row.Title) != draft.Title || Clean(row.OrderRef) != draft.OrderRef || Clean(row.Note) != draft.Note)
            return false;
        if (row.TargetPrice != draft.TargetPrice)
            return false;
        if (draft.ProvidedExecutedAt is DateTime at && AsUtc(row.ExecutedAt) != at)
            return false;
        if (draft.ProvidedFeeCurrency != null && !string.Equals(row.FeeCurrency, draft.ProvidedFeeCurrency, StringComparison.Ordinal))
            return false;
        return true;
    }

    static bool SameTransfer(Transfer row, TransferDraft draft)
    {
        if (row.Kind != draft.Kind || row.Amount != draft.Amount || Clean(row.Note) != draft.Note)
            return false;
        if (draft.ProvidedAt is DateTime at && AsUtc(row.At) != at)
            return false;
        if (draft.ProvidedCurrency != null && !string.Equals(row.Currency, draft.ProvidedCurrency, StringComparison.Ordinal))
            return false;
        return true;
    }

    static bool SameMark(IDbConnection db, PriceMark row, MarkDraft draft)
    {
        if (!string.Equals(row.Symbol, draft.Symbol, StringComparison.Ordinal) || row.Price != draft.Price)
            return false;
        if (Clean(row.Source) != draft.Source)
            return false;
        string? rowWallet = null;
        if (row.WalletId is int id)
            rowWallet = db.SingleById<Wallet>(id)?.Key;
        if (!string.Equals(rowWallet, draft.Wallet, StringComparison.Ordinal))
            return false;
        if (draft.ProvidedAt is DateTime at && AsUtc(row.At) != at)
            return false;
        return true;
    }

    static WalletMode RequireMode(WalletMode? mode)
    {
        if (mode is not WalletMode value || !Enum.IsDefined(value))
            throw Fail(400, "InvalidRequest", "Wallet mode is required. Use Paper or Real.");
        return value;
    }

    static TradeSide RequireSide(TradeSide? side)
    {
        if (side is not TradeSide value || !Enum.IsDefined(value))
            throw Fail(400, "InvalidRequest", "Trade side is required. Use Buy or Sell.");
        return value;
    }

    static TransferKind RequireKind(TransferKind? kind)
    {
        if (kind is not TransferKind value || !Enum.IsDefined(value))
            throw Fail(400, "InvalidRequest", "Transfer kind is required. Use Deposit, Withdrawal, or Sweep.");
        return value;
    }

    static string RequireKey(string? value) => RequireText(value, "Wallet key", Limits.Key);

    static string RequireSymbol(string? value) => RequireText(value, "Symbol", Limits.Symbol).ToUpperInvariant();

    static string RequireText(string? value, string label, int max)
    {
        var text = Clean(value);
        if (text == null)
            throw Fail(400, "InvalidRequest", $"{label} is required.");
        if (text.Length > max)
            throw Fail(400, "InvalidRequest", $"{label} cannot be longer than {max} characters.");
        return text;
    }

    static string? RequireOptional(string? value, string label, int max)
    {
        var text = Clean(value);
        if (text?.Length > max)
            throw Fail(400, "InvalidRequest", $"{label} cannot be longer than {max} characters.");
        return text;
    }

    static string? RequireNote(string? value) => RequireOptional(value, "Note", Limits.Note);

    static void RequirePositive(decimal value, string label)
    {
        if (value <= 0)
            throw Fail(400, "InvalidRequest", $"{label} must be greater than zero.");
    }

    static void RequireNonNegative(decimal value, string label)
    {
        if (value < 0)
            throw Fail(400, "InvalidRequest", $"{label} cannot be negative.");
    }

    static WalletMode? ParseMode(string? mode)
    {
        var text = Clean(mode);
        if (text == null || text.Equals("all", StringComparison.OrdinalIgnoreCase))
            return null;
        if (text.Equals("paper", StringComparison.OrdinalIgnoreCase))
            return WalletMode.Paper;
        if (text.Equals("real", StringComparison.OrdinalIgnoreCase))
            return WalletMode.Real;
        throw Fail(400, "InvalidRequest", "Mode must be all, paper, or real.");
    }

    static string? ParseOutcome(string? outcome)
    {
        var text = Clean(outcome)?.ToLowerInvariant();
        if (text == null)
            return null;
        if (text is "won" or "lost" or PnlTerms.Open or PnlTerms.Closed)
            return text;
        throw Fail(400, "InvalidRequest", "Outcome must be won, lost, open, or closed.");
    }

    static string ParseOrder(string? order)
    {
        var text = Clean(order)?.ToLowerInvariant() ?? "time";
        if (text is "time" or "symbol" or "wallet" or "pnl" or "fee" or "qty" or "price")
            return text;
        throw Fail(400, "InvalidRequest", "Order by must be time, symbol, wallet, pnl, fee, qty, or price.");
    }

    static bool MatchesSearch(TradeView view, string? search)
    {
        if (search == null)
            return true;
        return Has(view.Symbol, search)
            || Has(view.Title, search)
            || Has(view.Note, search)
            || Has(view.Wallet, search)
            || Has(view.ClientId, search)
            || Has(view.OrderRef, search);
    }

    static bool Has(string? value, string search) =>
        value != null && value.Contains(search, StringComparison.OrdinalIgnoreCase);

    static bool MatchesOutcome(TradeView view, string? outcome) => outcome switch
    {
        null => true,
        "won" => view.Pnl > 0,
        "lost" => view.Pnl < 0,
        PnlTerms.Open => view.Status == PnlTerms.Open,
        PnlTerms.Closed => view.Status == PnlTerms.Closed,
        _ => true,
    };

    static int CompareTrades(TradeView left, TradeView right, string order, bool descending)
    {
        var cmp = order switch
        {
            "symbol" => string.Compare(left.Symbol, right.Symbol, StringComparison.OrdinalIgnoreCase),
            "wallet" => string.Compare(left.Wallet, right.Wallet, StringComparison.OrdinalIgnoreCase),
            "pnl" => ComparePnl(left.Pnl, right.Pnl, descending),
            "fee" => left.Fee.CompareTo(right.Fee),
            "qty" => left.Qty.CompareTo(right.Qty),
            "price" => left.Price.CompareTo(right.Price),
            _ => left.ExecutedAt.CompareTo(right.ExecutedAt),
        };
        if (order != "pnl" && descending)
            cmp = -cmp;
        if (cmp != 0)
            return cmp;
        return right.Id.CompareTo(left.Id);
    }

    static int ComparePnl(decimal? left, decimal? right, bool descending)
    {
        if (left is null && right is null)
            return 0;
        if (left is null)
            return 1;
        if (right is null)
            return -1;
        var cmp = left.Value.CompareTo(right.Value);
        return descending ? -cmp : cmp;
    }

    static string? ParseKind(string? kind)
    {
        var text = Clean(kind)?.ToLowerInvariant();
        if (text == null)
            return null;
        if (!HistoryKinds.Contains(text))
            throw Fail(400, "InvalidRequest",
                $"Unknown history kind '{kind}'. Expected buy, sell, deposit, withdrawal, or sweep.");
        return text;
    }

    static void EnsureRange(DateTime? from, DateTime? to)
    {
        if (from is DateTime start && to is DateTime end && start > end)
            throw Fail(400, "InvalidRequest", "From is after to.");
    }

    static int NormalizeSkip(int? skip)
    {
        if (skip is null)
            return 0;
        if (skip < 0)
            throw Fail(400, "InvalidRequest", "Skip cannot be negative.");
        return skip.Value;
    }

    static int NormalizeTake(int? take)
    {
        if (take is null)
            return DefaultTake;
        if (take < 0)
            throw Fail(400, "InvalidRequest", "Take cannot be negative.");
        if (take > MaxTake)
            throw Fail(400, "InvalidRequest", $"Take cannot exceed {MaxTake}.");
        return take.Value;
    }

    static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim();
    }

    static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    DateTime Now() => AsUtc(clock.UtcNow);

    static string Num(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

    static bool IsUnique(Exception ex) =>
        ex.ToString().Contains("UNIQUE constraint failed", StringComparison.Ordinal);

    static HttpError Fail(int status, string code, string message) => new(status, code, message);

    sealed class UniqueHit : Exception;

    sealed record WalletDraft(string Key, string Name, WalletMode Mode, string? Venue, string BaseCurrency, string? Note);

    sealed record TradeDraft(
        string Wallet,
        string ClientId,
        string Symbol,
        TradeSide Side,
        decimal Qty,
        decimal Price,
        decimal Fee,
        string? ProvidedFeeCurrency,
        DateTime? ProvidedExecutedAt,
        DateTime ExecutedAt,
        string? Title,
        string? OrderRef,
        decimal? TargetPrice,
        string? Note);

    sealed record TransferDraft(
        string Wallet,
        string ClientId,
        TransferKind Kind,
        decimal Amount,
        string? ProvidedCurrency,
        DateTime? ProvidedAt,
        DateTime At,
        string? Note);

    sealed record MarkDraft(
        string Symbol,
        decimal Price,
        DateTime? ProvidedAt,
        DateTime At,
        string? Wallet,
        string? ClientId,
        string? Source);

    sealed class MarkSave
    {
        public PriceMark Mark { get; init; } = new();
        public bool AlreadyRecorded { get; init; }
        public bool Global { get; init; }
        public int? WalletId { get; init; }
    }

    static class Limits
    {
        public const int Key = 128;
        public const int Name = 200;
        public const int Venue = 64;
        public const int Currency = 16;
        public const int ClientId = 128;
        public const int Symbol = 64;
        public const int Title = 200;
        public const int OrderRef = 128;
        public const int Source = 64;
        public const int Note = 4000;
    }
}
