using System.Globalization;

namespace TradeMonitor.ServiceInterface;

// Parses chart intervals and picks a coarser bucket when a request would return too many candles.
public readonly record struct CandleInterval(int Seconds, string Key)
{
    public const int Cap = 1500;
    public const int MaxSeconds = 28 * 24 * 3600;

    public static readonly int[] Ladder = [60, 120, 300, 900, 1800, 3600, 7200, 10800, 18000, 86400, 604800];

    public static CandleInterval FiveMinutes { get; } = new(300, "5m");

    public static bool TryParse(string? text, bool emptyIsDefault, out CandleInterval interval, out string error)
    {
        interval = FiveMinutes;
        error = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            if (emptyIsDefault)
                return true;
            error = "Interval is required. Use a number and a unit, such as 7m or 4h.";
            return false;
        }

        var key = text.Trim().ToLowerInvariant();
        if (key is "one_minute" or "fifteen_minute" or "one_hour" or "one_day" or "six_hour")
            key = key switch
            {
                "one_minute" => "1m",
                "fifteen_minute" => "15m",
                "one_hour" => "1h",
                "one_day" => "1d",
                _ => "6h",
            };

        if (int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var bare))
        {
            if (bare is 60 or 300 or 900 or 3600 or 21600 or 86400)
            {
                interval = new CandleInterval(bare, Format(bare));
                return true;
            }

            error = $"Interval '{text.Trim()}' needs a unit, such as 7m or 4h.";
            return false;
        }

        if (key.Length < 2 || !char.IsAsciiDigit(key[0]))
        {
            error = $"Interval '{text.Trim()}' is not a number and a unit, such as 7m or 4h.";
            return false;
        }

        var unitAt = 0;
        while (unitAt < key.Length && char.IsAsciiDigit(key[unitAt]))
            unitAt++;
        if (unitAt == 0 || unitAt == key.Length || key[unitAt..].Length != 1)
        {
            error = $"Interval '{text.Trim()}' is not a number and a unit, such as 7m or 4h.";
            return false;
        }

        if (!int.TryParse(key[..unitAt], NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count <= 0)
        {
            error = $"Interval '{text.Trim()}' must be a positive whole number.";
            return false;
        }

        var unitSeconds = key[unitAt] switch
        {
            'm' => 60,
            'h' => 3600,
            'd' => 86400,
            'w' => 604800,
            _ => 0,
        };
        if (unitSeconds == 0)
        {
            error = $"Interval '{text.Trim()}' uses m, h, d, or w.";
            return false;
        }

        var seconds = count * unitSeconds;
        if (seconds < 60 || seconds > MaxSeconds)
        {
            error = $"Interval '{text.Trim()}' must be between 1m and 4w.";
            return false;
        }

        interval = new CandleInterval(seconds, Format(seconds));
        return true;
    }

    public static string Format(int seconds)
    {
        if (seconds % 604800 == 0)
            return seconds / 604800 + "w";
        if (seconds % 86400 == 0)
            return seconds / 86400 + "d";
        if (seconds % 3600 == 0)
            return seconds / 3600 + "h";
        return seconds / 60 + "m";
    }

    // Smallest ladder step that keeps the candle count at or under the cap.
    public static int Coarsen(int requestedSeconds, int candleCount, int cap)
    {
        if (cap <= 0 || candleCount <= cap || requestedSeconds <= 0)
            return requestedSeconds;
        var need = (int)Math.Ceiling(requestedSeconds * (candleCount / (double)cap));
        foreach (var step in Ladder)
        {
            if (step >= need && step >= requestedSeconds)
                return step;
        }

        return Ladder[^1];
    }
}
