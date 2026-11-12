using System.Data;
using System.Globalization;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.Converters;

namespace TradeMonitor.Data;

public static class LedgerDialect
{
    static int foreignKeysHooked;

    public static void Configure(IOrmLiteDialectProvider dialect)
    {
        dialect.RegisterConverter<decimal>(new TextDecimalConverter());

        if (dialect.GetConverter<DateTime>() is DateTimeConverter dates)
            dates.DateStyle = DateTimeKind.Utc;

        if (Interlocked.Exchange(ref foreignKeysHooked, 1) == 1)
            return;

        var previous = dialect.OnOpenConnection;
        dialect.OnOpenConnection = conn =>
        {
            previous?.Invoke(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys = ON";
            cmd.ExecuteNonQuery();
        };
    }
}

// SQLite NUMERIC affinity stores DECIMAL as an IEEE double and drops scale.
// Text keeps qty and price exact to at least 8 decimal places.
public class TextDecimalConverter : DecimalConverter
{
    public override string ColumnDefinition => "TEXT";

    public override DbType DbType => DbType.String;

    public override string GetColumnDefinition(int? precision, int? scale) => "TEXT";

    public override object ToDbValue(Type fieldType, object value)
    {
        if (value is null or DBNull)
            return null!;

        return ((decimal)value).ToString(CultureInfo.InvariantCulture);
    }

    public override object FromDbValue(Type fieldType, object value) => value switch
    {
        decimal d => d,
        string s => decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture),
        _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
    };

    public override string ToQuotedString(Type fieldType, object value)
    {
        var text = ((decimal)value).ToString(CultureInfo.InvariantCulture);
        return "'" + text + "'";
    }
}
