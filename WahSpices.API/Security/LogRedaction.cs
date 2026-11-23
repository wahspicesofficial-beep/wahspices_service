using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace WahSpices.API.Security;

/// <summary>
/// Serilog enricher that scrubs secrets before any sink sees the event:
///  - properties whose NAME looks sensitive (password, otp, token, secret, totp, cvv, card, pan, aadhar, account …)
///    are replaced by "***" — including nested properties of destructured objects ({@Request});
///  - any string value that LOOKS like a JWT, a bearer header, a card/bank/Aadhaar-length digit run, or a
///    "password=…"/"secret=…" pair is masked wherever it appears.
/// Literal text in a message template can't be changed — never put secrets in template text.
/// </summary>
public sealed partial class RedactionEnricher : ILogEventEnricher
{
    private const string Mask = "***";

    [GeneratedRegex(@"(?i)(pass(word)?|pwd|secret|otp|token|totp|cvv|cvc|pin|apikey|api_key|authorization|cookie|aadha?ar|pan(number)?|account(no|number)?|ifsc|backupcodes?|signature)$|^(otp|pan|cvv)$")]
    private static partial Regex SensitiveName();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"(?i)\b(bearer|basic)\s+[A-Za-z0-9\-._~+/]{16,}=*")]
    private static partial Regex AuthHeader();

    [GeneratedRegex(@"(?<![\dA-Za-z/])(\d[ -]?){12,19}(?![\dA-Za-z])")]
    private static partial Regex LongDigits();   // card numbers (13-19), Aadhaar (12), bank accounts (12+); 10-digit phones are kept

    [GeneratedRegex(@"(?i)\b(password|pwd|secret|otp|token|apikey|api_key)\s*[=:]\s*[^\s;,&""']+")]
    private static partial Regex KeyValueSecret();

    [GeneratedRegex(@"enc:v1:[A-Za-z0-9]+:[A-Za-z0-9_\-]+")]
    private static partial Regex Ciphertext();

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (name, value) in logEvent.Properties.ToList())
        {
            var scrubbed = Scrub(name, value);
            if (!ReferenceEquals(scrubbed, value))
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, scrubbed));
        }
    }

    private static LogEventPropertyValue Scrub(string name, LogEventPropertyValue value)
    {
        if (SensitiveName().IsMatch(name) && value is not StructureValue && value is not SequenceValue && value is not DictionaryValue)
            return new ScalarValue(Mask);

        switch (value)
        {
            case ScalarValue { Value: string s }:
                var r = ScrubText(s);
                return ReferenceEquals(r, s) ? value : new ScalarValue(r);
            case StructureValue sv:
                var props = sv.Properties.Select(p => new LogEventProperty(p.Name, Scrub(p.Name, p.Value))).ToList();
                return new StructureValue(props, sv.TypeTag);
            case SequenceValue seq:
                return new SequenceValue(seq.Elements.Select(e => Scrub("", e)));
            case DictionaryValue dv:
                return new DictionaryValue(dv.Elements.Select(kv =>
                    new KeyValuePair<ScalarValue, LogEventPropertyValue>(kv.Key, Scrub(kv.Key.Value?.ToString() ?? "", kv.Value))));
            default:
                return value;
        }
    }

    public static string ScrubText(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var r = Jwt().Replace(s, Mask);
        r = AuthHeader().Replace(r, m => m.Groups[1].Value + " " + Mask);
        r = KeyValueSecret().Replace(r, m => m.Groups[1].Value + "=" + Mask);
        r = Ciphertext().Replace(r, "enc:***");
        r = LongDigits().Replace(r, Mask);
        return r == s ? s : r;
    }
}
