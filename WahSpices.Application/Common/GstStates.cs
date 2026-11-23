namespace WahSpices.Application.Common;

/// <summary>
/// Indian GST state / UT codes (first two digits of a GSTIN). Used everywhere place of supply is decided so that
/// "Gujarat", "gujarat ", "GJ", "24" and "24-Gujarat" all mean the same state.
/// </summary>
public static class GstStates
{
    private static readonly (string Code, string Name, string Abbr)[] All =
    {
        ("01", "Jammu and Kashmir", "JK"), ("02", "Himachal Pradesh", "HP"), ("03", "Punjab", "PB"),
        ("04", "Chandigarh", "CH"), ("05", "Uttarakhand", "UK"), ("06", "Haryana", "HR"), ("07", "Delhi", "DL"),
        ("08", "Rajasthan", "RJ"), ("09", "Uttar Pradesh", "UP"), ("10", "Bihar", "BR"), ("11", "Sikkim", "SK"),
        ("12", "Arunachal Pradesh", "AR"), ("13", "Nagaland", "NL"), ("14", "Manipur", "MN"), ("15", "Mizoram", "MZ"),
        ("16", "Tripura", "TR"), ("17", "Meghalaya", "ML"), ("18", "Assam", "AS"), ("19", "West Bengal", "WB"),
        ("20", "Jharkhand", "JH"), ("21", "Odisha", "OD"), ("22", "Chhattisgarh", "CG"), ("23", "Madhya Pradesh", "MP"),
        ("24", "Gujarat", "GJ"), ("26", "Dadra and Nagar Haveli and Daman and Diu", "DN"), ("27", "Maharashtra", "MH"),
        ("29", "Karnataka", "KA"), ("30", "Goa", "GA"), ("31", "Lakshadweep", "LD"), ("32", "Kerala", "KL"),
        ("33", "Tamil Nadu", "TN"), ("34", "Puducherry", "PY"), ("35", "Andaman and Nicobar Islands", "AN"),
        ("36", "Telangana", "TS"), ("37", "Andhra Pradesh", "AP"), ("38", "Ladakh", "LA"), ("97", "Other Territory", "OT"),
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["J&K"] = "01", ["Jammu & Kashmir"] = "01", ["New Delhi"] = "07", ["NCT of Delhi"] = "07", ["Orissa"] = "21",
        ["Pondicherry"] = "34", ["Uttaranchal"] = "05", ["Daman and Diu"] = "26", ["Dadra and Nagar Haveli"] = "26",
        ["Andaman & Nicobar Islands"] = "35", ["Telengana"] = "36", ["25"] = "26",
    };

    private static string Squash(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>Two-digit GST state code for a state name / abbreviation / code / "24-Gujarat"; null when unknown.</summary>
    public static string? CodeOf(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;
        var s = state.Trim();
        // "24-Gujarat", "24 Gujarat", "24"
        var digits = new string(s.TakeWhile(char.IsDigit).ToArray());
        if (digits.Length is 1 or 2)
        {
            var code = digits.PadLeft(2, '0');
            if (Aliases.TryGetValue(code, out var a)) code = a;
            if (All.Any(x => x.Code == code)) return code;
        }
        if (Aliases.TryGetValue(s, out var alias)) return alias;
        var sq = Squash(s.Replace("&", "and", StringComparison.Ordinal));
        foreach (var x in All)
            if (Squash(x.Name) == sq || x.Abbr.Equals(s, StringComparison.OrdinalIgnoreCase)) return x.Code;
        return null;
    }

    /// <summary>Official name for a code (or any recognised spelling); null when unknown.</summary>
    public static string? NameOf(string? codeOrState)
    {
        var c = CodeOf(codeOrState);
        return c == null ? null : All.First(x => x.Code == c).Name;
    }

    /// <summary>GSTR-1 style place of supply: "24-Gujarat" (falls back to the raw text when unknown).</summary>
    public static string Format(string? state)
    {
        var c = CodeOf(state);
        return c == null ? (state ?? "").Trim() : $"{c}-{All.First(x => x.Code == c).Name}";
    }

    /// <summary>
    /// True when both sides are the same state (→ CGST + SGST). Compares GST codes when both are recognised,
    /// otherwise a case/space-insensitive name comparison. Blank on either side = same state (domestic default).
    /// </summary>
    public static bool SameState(string? supplier, string? customer)
    {
        if (string.IsNullOrWhiteSpace(supplier) || string.IsNullOrWhiteSpace(customer)) return true;
        var a = CodeOf(supplier); var b = CodeOf(customer);
        if (a != null && b != null) return a == b;
        return Squash(supplier) == Squash(customer);
    }

    public static IEnumerable<(string Code, string Name)> List() => All.Select(x => (x.Code, x.Name));
}
