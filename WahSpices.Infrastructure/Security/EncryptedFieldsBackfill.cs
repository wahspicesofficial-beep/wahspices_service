using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WahSpices.Infrastructure.Data;

namespace WahSpices.Infrastructure.Security;

/// <summary>
/// One-time (but safe to run on every start) job that encrypts legacy plaintext values in the encrypted columns,
/// and re-encrypts values written with an older key id after a key rotation.
/// Idempotent: values that already start with "enc:v1:{currentKeyId}:" are skipped; each UPDATE is guarded by
/// "WHERE Id=@id AND col=@old" so a concurrent edit is never overwritten.
/// </summary>
public static class EncryptedFieldsBackfill
{
    public static void Run(AppDbContext db, ILogger logger)
    {
        var enc = FieldEncryptor.Current ?? throw new InvalidOperationException("Field encryption is not configured.");
        var currentPrefix = $"{FieldEncryptor.Prefix}{enc.CurrentKeyId}:";
        int encrypted = 0, rotated = 0, failed = 0;

        var conn = db.Database.GetDbConnection();
        var wasOpen = conn.State == ConnectionState.Open;
        if (!wasOpen) conn.Open();
        try
        {
            foreach (var (clr, props) in EncryptedFields.Map)
            {
                var et = db.Model.FindEntityType(clr);
                if (et == null) continue;
                var table = et.GetTableName()!;
                var schema = et.GetSchema() ?? "dbo";
                var storeId = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(table, et.GetSchema());
                var idCol = et.FindPrimaryKey()!.Properties[0].GetColumnName(storeId)!;

                foreach (var prop in props)
                {
                    var col = et.FindProperty(prop)!.GetColumnName(storeId)!;
                    var purpose = EncryptedFields.Purpose(clr, prop);

                    var rows = new List<(object Id, string Value)>();
                    using (var cmd = conn.CreateCommand())
                    {
                        // Only rows that are NOT already on the current key. LIKE with an escaped literal prefix.
                        cmd.CommandText = $"SELECT [{idCol}], [{col}] FROM [{schema}].[{table}] WHERE [{col}] IS NOT NULL AND [{col}] <> N'' AND LEFT([{col}], @plen) <> @prefix";
                        AddParam(cmd, "@plen", currentPrefix.Length);
                        AddParam(cmd, "@prefix", currentPrefix);
                        using var r = cmd.ExecuteReader();
                        while (r.Read()) rows.Add((r.GetValue(0), r.GetString(1)));
                    }

                    int colEnc = 0, colRot = 0;
                    foreach (var (id, value) in rows)
                    {
                        string plaintext;
                        var wasEncrypted = FieldEncryptor.IsEncrypted(value);
                        if (wasEncrypted)
                        {
                            if (!enc.TryDecrypt(value, purpose, out plaintext)) { failed++; continue; }   // unknown key — leave as is
                        }
                        else plaintext = value;

                        var newValue = enc.Encrypt(plaintext, purpose);
                        using var up = conn.CreateCommand();
                        up.CommandText = $"UPDATE [{schema}].[{table}] SET [{col}] = @v WHERE [{idCol}] = @id AND [{col}] = @old";
                        AddParam(up, "@v", newValue);
                        AddParam(up, "@id", id);
                        AddParam(up, "@old", value);
                        if (up.ExecuteNonQuery() == 1) { if (wasEncrypted) colRot++; else colEnc++; }
                    }
                    if (colEnc + colRot > 0)
                        logger.LogInformation("Encryption backfill: {Table}.{Column} — {Encrypted} plaintext value(s) encrypted, {Rotated} re-keyed",
                            table, col, colEnc, colRot);
                    encrypted += colEnc; rotated += colRot;
                }
            }
        }
        finally
        {
            if (!wasOpen) conn.Close();
        }

        if (failed > 0)
            logger.LogError("Encryption backfill: {Failed} value(s) could not be decrypted with any configured key (missing Security:PreviousDataEncryptionKeys entry?) — left unchanged", failed);
        logger.LogInformation("Encryption backfill done (key {KeyId}): {Encrypted} encrypted, {Rotated} re-keyed, {Failed} undecryptable",
            enc.CurrentKeyId, encrypted, rotated, failed);
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
