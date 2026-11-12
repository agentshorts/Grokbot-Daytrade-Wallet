using ModelContextProtocol;
using ServiceStack.Web;

namespace TradeMonitor;

/// <summary>
/// MCP tools call the same ServiceStack services as the UI. They do not call the network.
/// </summary>
public abstract class McpServiceBase(IHttpContextAccessor httpContextAccessor)
{
    protected IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;

    protected IRequest GetRequest()
    {
        var http = HttpContextAccessor.HttpContext
            ?? throw new McpException("MCP tools run inside an HTTP request.");
        return http.ToRequest();
    }

    protected IServiceGateway GetGateway() => HostContext.AppHost.GetServiceGateway(GetRequest());

    protected async Task<T> SendAsync<T>(IReturn<T> request) where T : class
    {
        try
        {
            var response = await GetGateway().SendAsync(request);
            if (response == null)
                throw new McpException("The ledger service returned no result.");
            return response;
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpException(Describe(ex));
        }
    }

    public static string Describe(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is WebServiceException web)
            {
                var message = web.ResponseStatus?.Message;
                if (!string.IsNullOrWhiteSpace(message))
                    return message;
                if (!string.IsNullOrWhiteSpace(web.ErrorMessage))
                    return web.ErrorMessage;
            }

            if (current is HttpError http && !string.IsNullOrWhiteSpace(http.Message))
                return http.Message;
        }

        return string.IsNullOrWhiteSpace(ex.Message) ? "The ledger request failed." : ex.Message;
    }
}
