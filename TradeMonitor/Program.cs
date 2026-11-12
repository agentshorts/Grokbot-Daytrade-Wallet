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
// Routing has already selected /wallets and /trades as ServiceStack endpoints.
// Clear that match before authorization, or a browser navigation is challenged
// as an API call and never reaches the Vue page.
app.Use(async (context, next) =>
{
    var request = context.Request;
    if (!SpaDocument.ShouldServe(request.Method, request.Path.Value ?? "/", request.Headers.Accept.ToString(), request.Query.ContainsKey("format")))
    {
        await next();
        return;
    }

    context.SetEndpoint(null);
    if (app.Environment.IsDevelopment())
    {
        await nodeProxy.HttpToNode(context);
        return;
    }

    if (!app.Environment.IsEnvironment("Testing"))
    {
        var root = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
        var index = Path.Combine(root, "index.html");
        if (File.Exists(index))
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(index);
            return;
        }
    }

    await next();
});

app.UseAuthorization();
app.MapRazorPages();

app.UseMiddleware<McpRequestLogMiddleware>();
app.UseMiddleware<McpAuthMiddleware>();

app.UseServiceStack(new AppHost(), options => {
    options.MapEndpoints();
});

app.MapMcp("/mcp");

// Proxy HMR WebSocket and fallback routes to Node dev server in Development
if (app.Environment.IsDevelopment())
{
    app.RunNodeProcess(nodeProxy, "../TradeMonitor.Client"); // Start Node if not running
    app.UseWebSockets();
    app.MapViteHmr(nodeProxy);
    app.MapFallbackToNode(nodeProxy); // Fallback to Node dev server in development
}
else
{
    app.MapFallbackToFile("index.html"); // Fallback to index.html in production (TradeMonitor.Client/dist > wwwroot)
}

LedgerHealthEndpoint.Map(app);

app.Run();

public partial class Program { }
