using System;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime;
using Raphael.Utils;
using UnityEngine;

namespace Raphael.UI.ModContent;

// Runtime extraction of V Rising's world-map texture (#3). The game ships every asset inside hashed Addressable
// ContentArchives — there are no loose image files to copy — so the only automated route is to grab the texture
// from memory while the game is running and save it as the drop-in worldmap.png.
//
// Strategy: enumerate every loaded Texture2D, score candidates by name ("map"/"world"/…) + size + aspect, LOG the
// top matches (so we can identify the right one even if the auto-pick is off), then blit the best (or a
// name-filtered) texture to a readable RenderTexture, encode PNG, and write it to FaustMapBackdrop's path.
//
// Experimental: the exact map-texture name is undocumented, so this may pick the wrong texture on the first try —
// the logged candidate list + the name filter let the user (or us) home in on the right one. The map texture is
// usually only resident after the in-game map has been opened at least once, so the UI tells the user to press M
// first.
internal static class FaustMapCapture
{
    public static string SavePath => FaustMapBackdrop.ImageFilePath;
    public static string LastReport { get; private set; } = "";

    // Enumerate textures, log the best candidates, and save the chosen one (highest-scoring, or the best whose
    // name contains nameFilter) to worldmap.png. Returns a short status string for the UI.
    public static string Capture(string nameFilter)
    {
        try
        {
            var objs = UnityEngine.Resources.FindObjectsOfTypeAll(Il2CppType.Of<Texture2D>());
            if (objs == null || objs.Length == 0)
                return LastReport = "No textures loaded. Open the in-game map (press M) once, then try again.";

            string nf = (nameFilter ?? "").Trim().ToLowerInvariant();
            var ranked = new System.Collections.Generic.List<(string name, int w, int h, float score, Texture2D tex)>();

            for (int i = 0; i < objs.Length; i++)
            {
                var tex = objs[i]?.TryCast<Texture2D>();
                if (tex == null) continue;
                int w = tex.width, h = tex.height;
                if (w < 128 || h < 128) continue;                       // skip icons / UI chrome
                string nm = tex.name ?? "";
                string lo = nm.ToLowerInvariant();
                if (nf.Length > 0 && !lo.Contains(nf)) continue;        // name filter (when given)

                float score = 0f;
                if (lo.Contains("minimap")) score += 60f;
                if (lo.Contains("map")) score += 100f;
                if (lo.Contains("world")) score += 50f;
                if (lo.Contains("terrain")) score += 25f;
                if (lo.Contains("region")) score += 20f;
                if (lo.Contains("vardoran") || lo.Contains("farbane") || lo.Contains("dunley")) score += 80f;
                if (lo.Contains("icon") || lo.Contains("button") || lo.Contains("font") || lo.Contains("_ui_")) score -= 60f;
                float aspect = (float)Math.Min(w, h) / Math.Max(w, h);  // 1.0 = square (map-like)
                score += aspect * 25f;
                score += Math.Min(w, h) / 256f;                          // prefer larger
                ranked.Add((nm, w, h, score, tex));
            }

            ranked.Sort((a, b) => b.score.CompareTo(a.score));
            var sb = new StringBuilder();
            int top = Math.Min(30, ranked.Count);
            for (int i = 0; i < top; i++)
                sb.AppendLine($"  [{i + 1}] \"{ranked[i].name}\"  {ranked[i].w}x{ranked[i].h}  score={ranked[i].score:0}");
            LogUtils.LogInfo($"[Faust] map-texture candidates (filter='{nameFilter}', {ranked.Count} of {objs.Length} textures):\n{sb}");

            if (ranked.Count == 0)
                return LastReport = $"No texture ≥128px matched filter '{nameFilter}'. See BepInEx log for the full list; clear the filter to capture the best guess.";

            var best = ranked[0];
            var png = ToPng(best.tex);
            if (png == null)
                return LastReport = $"Top match \"{best.name}\" ({best.w}x{best.h}) couldn't be read (GPU-only/protected). See log for other names and try one in the filter.";

            var dir = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(SavePath, png);
            FaustMapBackdrop.Reset();
            return LastReport = $"Saved \"{best.name}\" ({best.w}x{best.h}) → worldmap.png. Click Reload image. If it's not the map, open the BepInEx log, find the right texture name, and type part of it in the filter.";
        }
        catch (Exception e)
        {
            LogUtils.LogWarning($"[Faust] map capture failed: {e}");
            return LastReport = $"Capture failed: {e.Message} (see BepInEx log).";
        }
    }

    // Blit a (possibly compressed / GPU-only) texture into a CPU-readable RenderTexture and encode PNG bytes.
    private static byte[] ToPng(Texture2D tex)
    {
        RenderTexture rt = null;
        Texture2D readable = null; // 0.65.6: full-size CPU copy — destroyed in finally (was leaked per capture)
        var prev = RenderTexture.active;
        try
        {
            rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            readable.Apply(false);
            var encoded = ImageConversion.EncodeToPNG(readable);
            if (encoded == null || encoded.Length == 0) return null;
            var data = new byte[encoded.Length];
            for (int i = 0; i < encoded.Length; i++) data[i] = encoded[i];
            return data;
        }
        catch (Exception e)
        {
            LogUtils.LogWarning($"[Faust] PNG encode failed: {e.Message}");
            return null;
        }
        finally
        {
            RenderTexture.active = prev;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            if (readable) UnityEngine.Object.Destroy(readable);
        }
    }
}
