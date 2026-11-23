using System.Collections.Concurrent;
using WahSpices.Infrastructure.Security;

namespace WahSpices.API.Security;

public static class SecurityStartup
{
    private static readonly ConcurrentDictionary<string, byte> ReportedDecryptFailures = new();

    /// <summary>Validate secrets (fail closed in Production) and load the field-encryption key ring.</summary>
    public static void ValidateAndConfigure(IConfiguration config, IHostEnvironment env)
    {
        SecretsValidator.Validate(config, env);
        var enc = FieldEncryptor.Configure(config);
        FieldEncryptor.OnDecryptFailure = what =>
        {
            if (ReportedDecryptFailures.TryAdd(what, 0))
                Serilog.Log.Error("Field decryption failed for {Field} — wrong or missing data-encryption key? Value left encrypted.", what);
        };
        Serilog.Log.Information("Field encryption ready (AES-256-GCM, current key id {KeyId})", enc.CurrentKeyId);
    }
}
