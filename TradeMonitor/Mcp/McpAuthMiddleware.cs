using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using ServiceStack.Configuration;
using TradeMonitor.Data;

namespace TradeMonitor;

/// <summary>
/// Requires a live admin API key on /mcp. The key is read from Authorization: Bearer,
/// X-Api-Key, or the apikey query parameter, then checked with ApiKeysFeature.
/// </summary>
public class McpAuthMiddleware(RequestDelegate next, ILogger<McpAuthMiddleware> log)
{
    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> users)
    {
        if (!context.Request.Path.StartsWithSegments("/mcp"))
        {
            await next(context);
            return;
        }

        var token = ReadToken(context.Request);
        if (string.IsNullOrEmpty(token))
        {
            await Reject(context, StatusCodes.Status401Unauthorized, "A valid API key is required.");
            return;
        }

        var feature = HostContext.AppHost.GetPlugin<ApiKeysFeature>();
        if (feature == null)
        {
            await Reject(context, StatusCodes.Status401Unauthorized, "A valid API key is required.");
            return;
        }

        ApiKeysFeature.ApiKey? apiKey;
        try
        {
            using var db = feature.OpenDb();
            apiKey = feature.GetApiKey(db, token);
        }
        catch (Exception ex)
        {
            log.LogWarning("MCP API key lookup failed: {Message}", ex.Message);
            await Reject(context, StatusCodes.Status401Unauthorized, "A valid API key is required.");
            return;
        }

        if (apiKey == null || apiKey.CancelledDate != null || apiKey.ExpiryDate is DateTime expiry && expiry < DateTime.UtcNow)
        {
            await Reject(context, StatusCodes.Status401Unauthorized, "A valid API key is required.");
            return;
        }

        if (string.IsNullOrEmpty(apiKey.UserId))
        {
            await Reject(context, StatusCodes.Status403Forbidden, "Admin role is required.");
            return;
        }

        var user = await users.FindByIdAsync(apiKey.UserId);
        if (user == null)
        {
            await Reject(context, StatusCodes.Status401Unauthorized, "A valid API key is required.");
            return;
        }

        var roles = await users.GetRolesAsync(user);
        if (!roles.Contains(RoleNames.Admin))
        {
            await Reject(context, StatusCodes.Status403Forbidden, "Admin role is required.");
            return;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id),
            new(ClaimTypes.Role, RoleNames.Admin),
        };
        if (!string.IsNullOrEmpty(user.Email))
            claims.Add(new Claim(ClaimTypes.Email, user.Email));

        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "ApiKey"));
        await next(context);
    }

    public static string? ReadToken(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var bearer = authorization["Bearer ".Length..].Trim();
            if (bearer.Length > 0)
                return bearer;
        }

        if (request.Headers.TryGetValue("X-Api-Key", out var header))
        {
            var value = header.ToString().Trim();
            if (value.Length > 0)
                return value;
        }

        if (request.Query.TryGetValue("apikey", out var query))
        {
            var value = query.ToString().Trim();
            if (value.Length > 0)
                return value;
        }

        return null;
    }

    static async Task Reject(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(message);
    }
}
