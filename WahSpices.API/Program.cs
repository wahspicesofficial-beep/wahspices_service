using Microsoft.AspNetCore.Hosting;
using Serilog;
using WahSpices.API.Extensions;
using WahSpices.API.Middleware;
using WahSpices.API.Services;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Kestrel — explicit IP + Port binding ─────────────
builder.WebHost.ConfigureKestrel(options =>
{
    // HTTP on 0.0.0.0:<Port> — default 5056; override with --Port=5061 or the Port setting
    options.ListenAnyIP(builder.Configuration.GetValue<int?>("Port") ?? 5056);
    // options.ListenAnyIP(7237, o => o.UseHttps());  // HTTPS — uncomment if needed
});
builder.AddHttpSecurity();   // limits, CORS "AllowFrontend", HSTS, forwarded headers, abuse guard (Extensions/HttpSecurityExtensions.cs)

// ── Serilog ──────────────────────────────────────────
builder.AddSerilog();

// ── Secrets + field encryption (refuses to start in Production with missing/placeholder secrets) ──
WahSpices.API.Security.SecurityStartup.ValidateAndConfigure(builder.Configuration, builder.Environment);

// ── Services ─────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opt =>
    {
        opt.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        opt.JsonSerializerOptions.WriteIndented = false;
        opt.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        opt.JsonSerializerOptions.Converters.Add(new WahSpices.API.Json.UtcDateTimeConverter());
    });

builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddRateLimiting(builder.Configuration);
builder.Services.AddSwagger();

// ── CORS ─── policy "AllowFrontend" is registered by AddHttpSecurity (origins from Security:Cors / AllowedOrigins)

// ── Health Checks ────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "sqlserver");

// ── Dependency Injection — application services ──────────
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<ICartService, CartService>();

// ProductService implements both IProductService + IAdminProductService
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<IProductService>(sp => sp.GetRequiredService<ProductService>());
builder.Services.AddScoped<IAdminProductService>(sp => sp.GetRequiredService<ProductService>());

builder.Services.AddSingleton<ITaxCalculationService, TaxCalculationService>();
builder.Services.AddScoped<IReturnService, ReturnService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IEmailService, EmailService>();
// Transactional email: templates + business details (cached) + in-process queue drained by a background worker with retries
builder.Services.AddSingleton<WahSpices.Infrastructure.Email.IEmailQueue, WahSpices.Infrastructure.Email.EmailQueue>();
builder.Services.AddSingleton<WahSpices.Infrastructure.Email.IEmailBrandProvider, WahSpices.Infrastructure.Email.EmailBrandProvider>();
builder.Services.AddSingleton<WahSpices.Infrastructure.Email.IOrderEmails, WahSpices.Infrastructure.Email.OrderEmails>();
builder.Services.AddScoped<WahSpices.Infrastructure.Email.IEmailComposer, WahSpices.Infrastructure.Email.EmailComposer>();
builder.Services.AddHostedService<EmailDispatcher>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IFileUploadService, FileUploadService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ReturnPhotoStore>();                     // return photos (App_Data, authenticated only)
builder.Services.AddHostedService<UnpaidOrderExpiryService>();        // cancels unpaid online orders after Orders:UnpaidExpiryMinutes
builder.Services.AddHostedService<SiteContentSeeder>();   // website content defaults (inserts missing keys only)
builder.Services.AddHostedService<RegionSeeder>();        // regional flavours stories (inserts missing regions only)
builder.Services.AddSingleton<CompanyDocStore>();                     // company bills / receipts / licence documents (App_Data, admin only)
builder.Services.AddHostedService<CompanyAlertService>();             // daily licence / warranty renewal alert email (CompanyAlerts:*)

// ── Customer communications: campaigns (email / WhatsApp, throttled), broadcasts + real-time notifications ──
builder.Services.AddSingleton<WahSpices.Infrastructure.Email.ITransactionalEmailCounter, WahSpices.Infrastructure.Email.TransactionalEmailCounter>();
builder.Services.AddSingleton<WahSpices.API.Services.Marketing.UnsubscribeTokens>();
builder.Services.AddSingleton<WahSpices.API.Services.Marketing.CampaignTypeLabels>();
builder.Services.AddScoped<WahSpices.API.Services.Marketing.CampaignService>();
builder.Services.AddScoped<WahSpices.API.Services.Marketing.CampaignContent>();
builder.Services.AddScoped<WahSpices.API.Services.Marketing.BroadcastService>();
builder.Services.AddHttpClient<WahSpices.Infrastructure.Messaging.IWhatsAppSender, WahSpices.Infrastructure.Messaging.WhatsAppCloudSender>(c => c.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddHostedService<WahSpices.API.Services.Marketing.MarketingDispatcherService>();   // drains campaign recipients under channel limits; schedules
// ── Shipping aggregators (Shiprocket …): credentials & behaviour live in the DB (Admin → Shipping → Partners) ─
builder.Services.AddHttpClient(WahSpices.API.Services.Shipping.ShiprocketProvider.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<WahSpices.API.Services.Shipping.IShippingProvider, WahSpices.API.Services.Shipping.ShiprocketProvider>();
builder.Services.AddSingleton<WahSpices.API.Services.Shipping.ShippingProviderRegistry>();
builder.Services.AddScoped<WahSpices.API.Services.Shipping.ShippingService>();
builder.Services.AddHostedService<WahSpices.API.Services.Shipping.ShipmentTrackingService>();   // polls active shipments (TrackingPollMinutes)
builder.Services.AddSignalR(o =>
    {
        o.MaximumReceiveMessageSize = 4 * 1024;   // clients never send anything but pings
        o.EnableDetailedErrors = false;
    })
    .AddJsonProtocol(o =>
    {
        o.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.PayloadSerializerOptions.Converters.Add(new WahSpices.API.Json.UtcDateTimeConverter());
    });

// ── Razorpay HttpClient (typed) ───────────────────────────
builder.Services.AddHttpClient<IPaymentService, PaymentService>(client =>
{
    var keyId     = builder.Configuration["Razorpay:KeyId"]     ?? "";
    var keySecret = builder.Configuration["Razorpay:KeySecret"] ?? "";
    var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{keyId}:{keySecret}"));
    client.BaseAddress = new Uri("https://api.razorpay.com/v1/");
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    client.DefaultRequestHeaders.Accept.Add(
        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
});

var app = builder.Build();

// ── Middleware Pipeline ───────────────────────────────
app.UseMiddleware<WahSpices.API.Security.ProxyClientIpMiddleware>();   // real visitor IP behind the website's /api proxy (shared secret)
app.UseHttpSecurity();                      // security headers, method/body limits, HSTS+HTTPS (prod), abuse guard
app.UseMiddleware<ExceptionMiddleware>();   // catches all exceptions

if (app.SwaggerEnabled())   // Development only (or Security:SwaggerEnabled=true)
{
    app.UseSwagger();
    app.UseSwaggerUI(opt =>
    {
        opt.SwaggerEndpoint("/swagger/v1/swagger.json", "WAH! Spices API v1");
        opt.RoutePrefix = "swagger";
        opt.DisplayRequestDuration();
    });
}

app.UseMiddleware<UploadsGuardMiddleware>();   // /uploads: images/videos only, nosniff + sandbox CSP
app.UseStaticFiles(app.UploadsStaticFileOptions());   // serves wwwroot/uploads/** as /uploads/** (media only, cached, nosniff)
app.UseCors("AllowFrontend");
app.UseForwardedHeaders();   // X-Forwarded-For only from Security:TrustedProxies
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseRateLimiter();        // after auth so limits can be per customer / per admin
app.UseAuthorization();
app.UseMiddleware<AuditLogMiddleware>();
app.MapControllers();
app.MapHub<WahSpices.API.Hubs.NotificationsHub>(WahSpices.API.Hubs.NotificationsHub.Path, o =>
{
    o.CloseOnAuthenticationExpiration = true;   // 15-min access token expires → client reconnects with a refreshed one
    o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets | Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
}).RequireCors(WahSpices.API.Extensions.HttpSecurityExtensions.CorsPolicyName);
app.MapHealthChecks("/health", app.HealthCheckOptions());

// ── Print all URLs Kestrel is listening on ───────────
app.Lifetime.ApplicationStarted.Register(() =>
{
    var urls = app.Urls.ToList();
    var firstHttp = urls.FirstOrDefault(u => u.StartsWith("http://")) ?? urls.FirstOrDefault() ?? "http://localhost:5056";

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("╔══════════════════════════════════════════════════════╗");
    Console.WriteLine("║          WAH! Spices API is running 🌶️              ║");
    Console.WriteLine("╠══════════════════════════════════════════════════════╣");
    foreach (var url in urls)
        Console.WriteLine($"║  ► {url,-50}║");
    Console.WriteLine($"║  ► {firstHttp + "/swagger",-50}║");
    Console.WriteLine("╚══════════════════════════════════════════════════════╝");
    Console.ResetColor();
    Console.WriteLine();
});

Log.Information("WAH! API starting — Environment: {Env}", app.Environment.EnvironmentName);

// ── Ensure uploads directory exists under wwwroot ────
var env = app.Services.GetRequiredService<IWebHostEnvironment>();
var uploadsRoot = Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "uploads");
foreach (var sub in new[] { "blog", "recipes", "marketing" })
    Directory.CreateDirectory(Path.Combine(uploadsRoot, sub));

// ── Auto-create missing purchase tables ──────────────
using (var scope = app.Services.CreateScope())
{
    var db     = scope.ServiceProvider.GetRequiredService<WahSpices.Infrastructure.Data.AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");
    WahSpices.Infrastructure.Data.DbInitializer.EnsureTables(db, logger);
    WahSpices.Infrastructure.Data.MasterDataSeeder.Seed(db, logger);
    WahSpices.Infrastructure.Data.HsnSeeder.Seed(db, logger);   // spice & masala HSN codes, first run only
    WahSpices.Infrastructure.Security.EncryptedFieldsBackfill.Run(db, logger);   // encrypt legacy plaintext / re-key
}

app.Run();
