using System.Text;
using Microsoft.Data.SqlClient;
using WahSpices.Infrastructure.Security;

namespace WahSpices.API.Security;

/// <summary>
/// Runs first thing at startup. Secrets never live in appsettings.json — they come from user-secrets (Development)
/// or environment variables (Production, e.g. Jwt__SecretKey). In Production the app REFUSES TO START when a
/// required secret is missing, still a placeholder, or obviously weak. In Development it fails only on what the
/// app can't run without (DB, JWT, data key) and warns about the rest. See SECURITY.md.
/// </summary>
public static class SecretsValidator
{
    private static readonly string[] PlaceholderMarkers =
        ["SET-IN-", "SET_IN_", "YOUR_", "YOUR-", "XXXXXXXX", "CHANGE_ME", "CHANGEME", "PLACEHOLDER", "WAH-SPICES-SUPER-SECRET"];

    public static void Validate(IConfiguration config, IHostEnvironment env)
    {
        var prod = env.IsProduction();
        var errors = new List<string>();
        var warnings = new List<string>();

        void Require(string key, bool requiredInDev, string? why = null)
        {
            var v = config[key];
            string? problem = string.IsNullOrWhiteSpace(v) ? "is missing"
                : IsPlaceholder(v) ? "is still a placeholder" : null;
            if (problem == null) return;
            var msg = $"{key} {problem}{(why != null ? $" ({why})" : "")}";
            if (prod || requiredInDev) errors.Add(msg); else warnings.Add(msg);
        }

        // ── Database ──
        Require("ConnectionStrings:DefaultConnection", true);
        var cs = config.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(cs) && !IsPlaceholder(cs))
        {
            try
            {
                var b = new SqlConnectionStringBuilder(cs);
                if (prod && b.Encrypt == SqlConnectionEncryptOption.Optional) errors.Add("ConnectionStrings:DefaultConnection must use Encrypt=True in Production");
                if (prod && b.TrustServerCertificate) errors.Add("ConnectionStrings:DefaultConnection must use TrustServerCertificate=False in Production (use a valid TLS certificate)");
                if (prod && string.Equals(b.UserID, "sa", StringComparison.OrdinalIgnoreCase)) errors.Add("ConnectionStrings:DefaultConnection must not use the 'sa' login in Production (use the least-privilege app login)");
                if (!prod && string.Equals(b.UserID, "sa", StringComparison.OrdinalIgnoreCase)) warnings.Add("Development connection string uses 'sa' — use the least-privilege app login (SECURITY.md)");
            }
            catch (ArgumentException) { errors.Add("ConnectionStrings:DefaultConnection is not a valid SQL Server connection string"); }
        }

        // ── JWT ──
        Require("Jwt:SecretKey", true);
        var jwt = config["Jwt:SecretKey"];
        if (!string.IsNullOrWhiteSpace(jwt) && !IsPlaceholder(jwt) && Encoding.UTF8.GetByteCount(jwt) < 64)
            (prod ? errors : warnings).Add("Jwt:SecretKey is too short — use at least 64 random bytes (base64)");

        // ── Field encryption key ──
        if (config["Security:DataEncryptionKey"] is { } dek && IsPlaceholder(dek)) errors.Add("Security:DataEncryptionKey is still a placeholder");
        else try { FieldEncryptor.ParseKey(config["Security:DataEncryptionKey"], "Security:DataEncryptionKey"); }
        catch (InvalidOperationException ex) { errors.Add(ex.Message); }

        // ── Payments (Razorpay) ──
        Require("Razorpay:KeyId", false, "online payments");
        Require("Razorpay:KeySecret", false, "online payments + signature verification");

        // ── Email ──
        var mode = config["Email:DeliveryMode"];
        if (!string.Equals(mode, "Outbox", StringComparison.OrdinalIgnoreCase) && !string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            Require("Email:Username", false, "SMTP");
            Require("Email:Password", false, "SMTP");
        }
        if (prod && string.Equals(mode, "Outbox", StringComparison.OrdinalIgnoreCase))
            errors.Add("Email:DeliveryMode=Outbox writes emails (OTPs, reset links) to disk — not allowed in Production");

        // ── Optional integrations: only checked when configured ──
        if (!string.IsNullOrWhiteSpace(config["Sms:ApiKey"]) && IsPlaceholder(config["Sms:ApiKey"]!))
            warnings.Add("Sms:ApiKey is a placeholder (SMS OTP provider not configured)");
        if (!string.IsNullOrWhiteSpace(config["Google:ClientId"]) && IsPlaceholder(config["Google:ClientId"]!))
            warnings.Add("Google:ClientId is a placeholder (Google sign-in disabled)");

        foreach (var w in warnings) Serilog.Log.Warning("Config check: {Warning}", w);
        if (errors.Count > 0)
        {
            var text = $"Refusing to start ({env.EnvironmentName}): insecure or missing secrets:\n  - " + string.Join("\n  - ", errors) +
                       "\nSet them via `dotnet user-secrets set <key> <value>` (Development) or environment variables such as Jwt__SecretKey (Production). See SECURITY.md.";
            Serilog.Log.Fatal("{Error}", text);
            throw new InvalidOperationException(text);
        }
    }

    public static bool IsPlaceholder(string v) =>
        PlaceholderMarkers.Any(m => v.Contains(m, StringComparison.OrdinalIgnoreCase));
}
