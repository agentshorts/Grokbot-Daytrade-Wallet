using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using TradeMonitor.Data;
using TradeMonitor.ServiceInterface;

AppHost.RegisterKey();

Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "App_Data"));

McpDotEnv.Load();

var builder = WebApplication.CreateBuilder(args);
McpConfiguredKey.AddLocalSources(builder.Configuration);
LoopbackBinding.Apply(builder);
var services = builder.Services;

var mcpJson = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
mcpJson.Converters.Add(new JsonStringEnumConverter());
services.AddHttpContextAccessor();
services.AddMcpServer(options =>
{
    options.ServerInstructions =
        "TradeMonitor ledger on 127.0.0.1. Record fills and cash that already happened. " +
        "This server does not place orders, move money, or call a venue. " +
        "Prices are quote currency per unit. Amounts are in the wallet base currency, USD unless the wallet says otherwise. " +
        "Times are UTC ISO-8601. client_id is an idempotency key: repeat the same values to retry, and do not reuse it for a different fill.";
})
.WithTools<LedgerMcpTools>(mcpJson)
.WithHttpTransport(options => options.Stateless = true);
services.AddHostedService<McpConfiguredKeyHostedService>();

services.AddAuthorization();
services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();
services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("App_Data"));

services.AddDatabaseDeveloperPageExceptionFilter();

services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

services.AddRazorPages();

services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
// Uncomment to send emails with SMTP, configure SMTP with "SmtpConfig" in appsettings.json
// services.AddSingleton<IEmailSender<ApplicationUser>, EmailSender>();
services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, AdditionalUserClaimsPrincipalFactory>();

// Register all services
services.AddServiceStack(typeof(MyServices).Assembly);

var app = builder.Build();
var nodeProxy = new NodeProxy("http://127.0.0.1:5173") {
    Log = app.Logger
};

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseMigrationsEndPoint();
        
    app.MapNotFoundToNode(nodeProxy);
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapCleanUrls();

// The host PC listens on http://127.0.0.1:5050. Redirecting that HTTP
// binding to HTTPS fails /api/health when no certificate is configured.
if (!app.Environment.IsEnvironment("Testing") && LoopbackBinding.UsesHttps(app.Configuration))
    app.UseHttpsRedirection();
app.UseAuthorization();
app.MapRazorPages();

app.UseMiddleware<McpRequestLogMiddleware>();
app.UseMiddleware<McpAuthMiddleware>();

app.UseServiceStack(new AppHost(), options => {
    options.MapEndpoints();
});

app.MapMcp("/mcp");

// Unknown /api paths stay on the API. They must not fall through to the Vue shell.
app.MapFallback("/api", ApiMiss).WithOrder(int.MaxValue - 2);
app.MapFallback("/api/{**path}", ApiMiss).WithOrder(int.MaxValue - 1);

// Proxy HMR WebSocket and fallback routes to Node dev server in Development
if (app.Environment.IsDevelopment())
{
    app.RunNodeProcess(nodeProxy, "../TradeMonitor.Client"); // Start Node if not running
    app.UseWebSockets();
    app.MapViteHmr(nodeProxy);
    // Kalshi tickers include a dot (KXBTCD-26NOV1617-T64999.99). The default
    // {*path:nonfile} fallback treats that as a static file and never reaches Node.
    app.MapFallback("/predictions/{**rest}", nodeProxy.HttpToNode)
        .WithOrder(int.MaxValue - 1)
        .WithMetadata(new HttpMethodMetadata(["GET", "HEAD"]));
    app.MapFallbackToNode(nodeProxy); // Fallback to Node dev server in development
}
else
{
    // Same dotted-ticker case. The pattern is used as written, so the dot stays
    // in the route. An existing file under /predictions is served as itself.
    // /api stays on the API fallbacks registered above.
    app.MapFallback("/predictions/{**rest}", PredictionShell(app))
        .WithOrder(int.MaxValue - 1)
        .WithMetadata(new HttpMethodMetadata(["GET", "HEAD"]));
    app.MapFallbackToFile("index.html"); // Fallback to index.html in production (TradeMonitor.Client/dist > wwwroot)
}

LedgerHealthEndpoint.Map(app);

app.Run();

public partial class Program
{
    // Routing matches this fallback before the static-file middleware, which then
    // skips the request. Serve a real file when one exists, otherwise index.html.
    static RequestDelegate PredictionShell(IEndpointRouteBuilder endpoints)
    {
        var branch = endpoints.CreateApplicationBuilder();
        branch.Use(next => context =>
        {
            var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
            var relative = (context.Request.Path.Value ?? "/").TrimStart('/');
            var file = env.WebRootFileProvider.GetFileInfo(relative);
            if (!file.Exists || file.IsDirectory)
                context.Request.Path = "/index.html";
            context.SetEndpoint(null);
            return next(context);
        });
        branch.UseStaticFiles();
        return branch.Build();
    }

    static Task ApiMiss(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync("""{"responseStatus":{"errorCode":"NotFound","message":"No API at this path."}}""");
    }
}
