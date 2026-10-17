using System;
using System.Globalization;

namespace Raphael.Services.Nyar;

// 0.72.0: the `plugin=` version Nyarlathotep sends in its handshake ("0.13.0-rc.3"), read only to word messages for a
// server bug that a known release fixed. Gating stays on `api` (contract §2). Pure; unit-tested.
internal readonly struct NyarVersion
{
    public readonly int Major, Minor, Patch, Rc;   // Rc = 0 for a release
    public readonly bool Valid;

    private NyarVersion(int a, int b, int c, int rc) { Major = a; Minor = b; Patch = c; Rc = rc; Valid = true; }

    /// <summary>"major.minor.patch" or "major.minor.patch-rc.N"; anything else is not Valid.</summary>
    public static NyarVersion Parse(string s)
    {
        var v = (s ?? "").Trim();
        int rc = 0;
        int dash = v.IndexOf('-');
        if (dash >= 0)
        {
            var pre = v.Substring(dash + 1);
            if (!pre.StartsWith("rc.", StringComparison.Ordinal) || !Num(pre.Substring(3), out rc) || rc < 1) return default;
            v = v.Substring(0, dash);
        }
        var p = v.Split('.');
        if (p.Length != 3 || !Num(p[0], out var a) || !Num(p[1], out var b) || !Num(p[2], out var c)) return default;
        return new NyarVersion(a, b, c, rc);
    }

    /// <summary>The apocalypse "marches on the starting admin" start bug (badarg admin_location) was fixed in
    /// 0.13.0-rc.2 (Nyarlathotep d492bd5). Apocalypse arrived in 0.13.0, so only 0.13.0 prereleases below rc.2 have it;
    /// every other version (and an unreadable one) gets the generic wording.</summary>
    public static bool HasAdminLocationBug(string plugin)
    {
        var v = Parse(plugin);
        return v.Valid && v.Major == 0 && v.Minor == 13 && v.Patch == 0 && v.Rc is >= 1 and < 2;
    }

    private static bool Num(string s, out int n)
        => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n) && s.Length is > 0 and <= 6;
}
