using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using WahSpices.API.Authorization;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Extensions;

public static class ServiceExtensions
{
    // ── SQL Server + EF Core ─────────────────────────
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(opt =>
            opt.UseSqlServer(
                config.GetConnectionString("DefaultConnection"),
                sql =>
                {
                    sql.CommandTimeout(60);
                    sql.EnableRetryOnFailure(3);
                }
            ).AddInterceptors(new WahSpices.Infrastructure.Services.StockSyncInterceptor())); // keeps Product.InStock in sync with inventory
        return services;
    }

    // ── JWT Authentication ───────────────────────────
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration config)
    {
        var jwtSection = config.GetSection("Jwt");
        var secretKey = WahSpices.API.Security.WahSecurityRegistration.RequireSigningKey(config);   // ≥64 chars in Production

        services.AddAuthentication(opt =>
        {
            opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(opt =>
        {
            opt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(secretKey),
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },   // alg pinned (no "none", no RS/HS confusion)
                RequireSignedTokens = true,
                ValidateIssuer = true,
                ValidIssuer = jwtSection["Issuer"],
                ValidateAudience = true,
                ValidAudience = jwtSection["Audience"],
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };
            opt.Events = new JwtBearerEvents
            {
                // Customers: TokenVersion + refresh-session check on every request (password change / logout-all /
                // block / reset kill tokens instantly). Admins: token must map to a live AdminSession (revoke, idle
                // timeout, IP allow-list). See Security/TokenSecurity.cs.
                OnTokenValidated = WahSpices.API.Security.WahTokenEvents.OnTokenValidated,
                // Browsers can't set headers on WebSocket / SSE requests → SignalR sends ?access_token=. Accepted ONLY on
                // the notifications hub path (never on the REST API, so tokens don't end up in ordinary URLs / logs).
                OnMessageReceived = ctx =>
                {
                    var token = ctx.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments(WahSpices.API.Hubs.NotificationsHub.Path))
                        ctx.Token = token;
                    return Task.CompletedTask;
                },
                OnChallenge = ctx =>
                {
                    ctx.HandleResponse();
                    ctx.Response.StatusCode = 401;
                    ctx.Response.ContentType = "application/json";
                    var reason = ctx.HttpContext.Items[WahSpices.API.Security.WahTokenEvents.FailureReasonKey] as string ?? "Token missing or expired.";
                    return ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false, error = new { code = "UNAUTHORIZED", message = reason }
                    }));
                }
            };
        });

        services.AddAuthorization(opt =>
        {
            opt.AddPolicy("CustomerOnly", p => p.RequireRole("Customer"));
            opt.AddPolicy("AdminOnly",    p => p.RequireRole("Admin", "Manager", "Accountant", "Production", "SalesExec", "HRManager"));
            opt.AddPolicy("SuperAdmin",   p => p.RequireRole("Admin"));
        });

        // Module-level RBAC for every admin API: [RequireModule("Orders")] on controllers/actions,
        // checked against the DB (role permissions + per-user overrides) on each request.
        services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(o => o.Filters.Add<ModulePermissionFilter>());

        return services;
    }

    // ── Rate Limiting + bot protection ───────────────
    // Built-in ASP.NET Core rate limiter (per IP / customer / admin + strict auth/OTP/contact/order rules),
    // trusted-proxy forwarded headers, proof-of-work human proof, admin security policy. See Security/*.cs.
    public static IServiceCollection AddRateLimiting(this IServiceCollection services, IConfiguration config)
        => WahSpices.API.Security.WahSecurityRegistration.AddWahSecurity(services, config);

    // ── Swagger / OpenAPI ────────────────────────────
    public static IServiceCollection AddSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        // Swagger with JWT bearer security — full config via OpenAPI spec
        services.AddSwaggerGen(opt =>
        {
            // Unique schema ids even when two controllers declare request records with the same name
            opt.CustomSchemaIds(t => (t.FullName ?? t.Name).Replace("+", "."));
            // Same route served by content type (e.g. POST /api/returns as JSON or multipart with photos) — document the first
            opt.ResolveConflictingActions(descs => descs.First());
            opt.SwaggerDoc("v1", new()
            {
                Title       = "WAH! Spices API",
                Version     = "v1",
                Description = "REST API for WAH! Spices e-commerce + Admin ERP. Use Bearer token for auth."
            });
        });

        return services;
    }

    // ── Serilog ──────────────────────────────────────
    public static WebApplicationBuilder AddSerilog(this WebApplicationBuilder builder)
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentName()
            .Enrich.With(new WahSpices.API.Security.RedactionEnricher())   // scrub passwords/OTPs/tokens/card data
            .WriteTo.Console()
            .WriteTo.File(
                path: "Logs/wah-api-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();

        builder.Host.UseSerilog();
        return builder;
    }
}
