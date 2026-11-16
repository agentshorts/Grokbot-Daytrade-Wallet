using ServiceStack;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

public class PredictionBookingServices(IDbConnectionFactory dbFactory, IPredictionBookings bookings) : Service
{
    public object Any(GetPredictionBookings request)
    {
        _ = request;
        using var db = dbFactory.Open();
        var keys = db.Select<Wallet>().Select(wallet => wallet.Key);
        return new GetPredictionBookingsResponse
        {
            Results = PredictionBookingReport.List(bookings.Snapshot(), keys),
        };
    }
}
