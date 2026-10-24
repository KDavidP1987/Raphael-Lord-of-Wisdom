using System.Globalization;

namespace Raphael.Services;

// 0.73.0: pure encoder for one VampireCommandFramework argument (PLAN-ADDENDUM §1 "Quoting"). No Unity references:
// this file is linked into tests/Raphael.Logic.Tests.
//
// VCF splits a chat command in Utility.GetParts (LearningMods/VampireCommandFramework-main/VCF.Core/Common/Utility.cs:18-60):
//   - it splits ONLY on ASCII ' ' outside double quotes (not tabs, not U+00A0);
//   - a '"' toggles quoting and is dropped;
//   - a backslash followed by '"' is an escaped quote (so a trailing backslash inside "..." swallows the closing quote).
// So a value is safe as one token if it has no '"', no backslash and no control character, and is wrapped in quotes
// exactly when it contains an ASCII space. Anything else is refused rather than escaped.
public static class VcfArg
{
    /// <summary>Encode <paramref name="raw"/> as one VCF token. Trims; refuses empty / whitespace-only values and any
    /// '"', '\' or control character (CR, LF and tab included); wraps in double quotes only if the value has a space.</summary>
    public static bool TryEncode(string raw, out string token, out string error)
    {
        token = null;
        // 0.73.0 post-build review: refused characters are checked on the raw value, before trimming, so a line
        // break or tab at either end is refused rather than trimmed away.
        foreach (var ch in raw ?? "")
        {
            if (ch == '"') { error = "can't contain a double quote"; return false; }
            if (ch == '\\') { error = "can't contain a backslash"; return false; }
            if (char.IsControl(ch)) { error = "can't contain a tab or line break"; return false; }
            var cat = char.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.LineSeparator || cat == UnicodeCategory.ParagraphSeparator)
            { error = "can't contain a line break"; return false; }
        }
        var v = (raw ?? "").Trim();
        if (v.Length == 0) { error = "is empty"; return false; }
        token = v.IndexOf(' ') >= 0 ? "\"" + v + "\"" : v;
        error = null;
        return true;
    }

    /// <summary>Parse a decimal typed by the player: trims, accepts '.' or ',' as the decimal separator, no thousands
    /// separators and no exponent ("1e-1" is refused), refuses NaN / infinity, inclusive [min, max], at most
    /// <paramref name="maxDecimals"/> decimal places. Emits invariant text ("0.5", never "0,5"; "-0" becomes "0").</summary>
    public static bool TryDecimal(string raw, double min, double max, int maxDecimals, out string invariantText)
    {
        invariantText = null;
        var v = (raw ?? "").Trim().Replace(',', '.');
        if (v.Length == 0) return false;
        int dot = v.IndexOf('.');
        if (dot >= 0)
        {
            if (v.IndexOf('.', dot + 1) >= 0) return false;            // "1.000.5", "0,5.1"
            if (v.Length - dot - 1 > maxDecimals) return false;        // too precise
        }
        if (!double.TryParse(v, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                             CultureInfo.InvariantCulture, out var d)) return false;
        if (double.IsNaN(d) || double.IsInfinity(d)) return false;
        if (d < min || d > max) return false;
        if (d == 0) d = 0;                                             // no "-0"
        var fmt = maxDecimals > 0 ? "0." + new string('#', maxDecimals) : "0";
        invariantText = d.ToString(fmt, CultureInfo.InvariantCulture);
        return true;
    }
}
