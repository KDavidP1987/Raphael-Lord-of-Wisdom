using System;
using System.IO;
using BepInEx;
using Raphael.Config;
using Raphael.Utils;
using Raphael.UI.Framework.CustomLib.Util;
using Raphael.UI.Framework.UniverseLib.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Raphael.UI.ModContent;

// Shared "frame of reference" backdrop for the Faust World Map (asset plot) and the Heat Map (§3 follow-up).
// Both used to draw on a bare black board with no sense of where on the map a dot sits. This draws, behind the
// dots:
//   • a drop-in world-map IMAGE (config/Raphael/worldmap.png) the user extracts from the game assets, stretched
//     to the calibrated world rectangle [MinX..MaxX]×[MinZ..MaxZ] (Settings) so dots land in the right place; and
//   • a coordinate GRID + corner axis labels (no asset needed — always available as a reference frame).
// Callers render BOTH boards to that same fixed world rectangle (so the image/grid and the dots share one
// coordinate space) and call Decorate() right after creating the board, BEFORE adding dots, so it sits behind.
internal static class FaustMapBackdrop
{
    private static Sprite _sprite;

    // 0.67.1 PERF: the image is never decoded as a side effect of building UI. A user worldmap.png is typically the
    // full 6080×6080 game map (~37 MP) and decoding it synchronously used to happen inside the MainPanel build
    // (StatusLine() → LoadSprite()), freezing the game on first open even on servers without Faust. Now only the
    // scheduled loader decodes (one frame after a map draw asks for it, after "Loading map image…" has rendered),
    // and it keeps a ≤2048px cached copy on disk so later launches decode ~9× fewer pixels.
    private enum LoadState { Idle, Scheduled, Loading, Done, Failed }
    private static LoadState _loadState = LoadState.Idle;
    private static int _loadGen;                  // bumped by Reset(); a load from an older generation never commits
    private static Action _scheduledLoad;
    private const int CacheMaxDim = 2048;

    /// <summary>Raised on the main thread after the image finished loading (or failed), so boards can redraw.</summary>
    public static event Action ImageLoaded;

    private static string ImagePath =>
        Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME, "worldmap.png");
    private static string CachePath => Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME, "worldmap.cache.png");
    private static string CacheKeyPath => Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME, "worldmap.cache.key");

    // The on-disk path the user drops worldmap.png into (shown in the UI so they can find it).
    public static string ImageFilePath => ImagePath;

    public static bool IsLoaded => _sprite != null;
    public static bool IsLoading => _loadState == LoadState.Scheduled || _loadState == LoadState.Loading;

    // True when the user has supplied a usable image, wants it drawn, AND it has finished loading. Never decodes.
    public static bool HasImage => Settings.FaustWorldMapImage && _sprite != null;

    // True while an image is expected but not loaded yet (enabled, file present, not failed). Map draws call
    // RequestLoad() in this state and show "Loading map image…" until ImageLoaded fires.
    private static bool ImagePending => Settings.FaustWorldMapImage && _sprite == null
        && _loadState != LoadState.Failed && SafeExists(ImagePath);

    // The loaded image's pixel aspect ratio (width / height), or 1.0 (square) when there's no image. Callers use
    // this to give the render rectangle the same aspect so the map isn't stretched (the V Rising map is square).
    public static float ImageAspect
    {
        get
        {
            var s = _sprite;
            if (s == null || s.texture == null || s.texture.height == 0) return 1f;
            return (float)s.texture.width / s.texture.height;
        }
    }

    // True when SOME backdrop (image or grid) will draw — i.e. callers should use the fixed map rectangle. An image
    // that is still loading counts (so the board keeps the map layout); one that failed to load doesn't.
    public static bool Active => Settings.FaustWorldMapGrid || HasImage || ImagePending;

    // Caption fragment for the map boards' footer line.
    public static string BackdropNote()
        => HasImage ? "map image + grid" : (ImagePending ? "loading map image…" : "coordinate grid");

    // A human-readable status line for the underlay card, so a black board isn't mysterious. Never decodes.
    public static string StatusLine()
    {
        var spr = _sprite;
        if (spr != null && spr.texture != null)
            return $"<color=#8FBF6F>Map image loaded</color> — {spr.texture.width}×{spr.texture.height}px.";
        if (!Settings.FaustWorldMapImage)
            return "<color=#B0B0B0>Map image is turned off (grid only). Enable it above to use worldmap.png.</color>";
        if (!SafeExists(ImagePath))
            return $"<color=#FFB070>No map image yet</color> — drop a PNG named <b>worldmap.png</b> into <b>BepInEx/config/Raphael/</b>, then click <b>Reload image</b>. (Until then you'll see the grid only.)";
        if (_loadState == LoadState.Failed)
            return "<color=#FF8080>worldmap.png is present but couldn't be loaded</color> — make sure it's a valid PNG, then Reload image.";
        if (IsLoading)
            return "<color=#B0B0B0>Loading map image…</color>";
        return "<color=#B0B0B0>Map image found — it loads the first time a map is drawn.</color>";
    }

    /// <summary>Schedule ONE load for the next frame (deduped). Safe to call from any draw path, every draw.</summary>
    public static void RequestLoad()
    {
        if (_sprite != null || _loadState != LoadState.Idle) return;
        if (!Settings.FaustWorldMapImage || !SafeExists(ImagePath)) return;
        _loadState = LoadState.Scheduled;
        int gen = _loadGen;
        int armedFrame = Time.frameCount;
        Action tick = null;
        tick = () =>
        {
            if (Time.frameCount <= armedFrame) return;   // let "Loading map image…" render first
            Behaviors.CoreUpdateBehavior.Actions.Remove(tick);
            if (ReferenceEquals(_scheduledLoad, tick)) _scheduledLoad = null;
            if (gen != _loadGen) return;                  // Reset() since scheduling — superseded
            RunLoad(gen);
        };
        _scheduledLoad = tick;
        Behaviors.CoreUpdateBehavior.Actions.Add(tick);
    }

    // Forget the cached sprite so a freshly dropped-in worldmap.png is picked up without a game restart.
    // 0.65.6 LEAK FIX: the sprite + texture are HideAndDontSave, so Resources.UnloadUnusedAssets never reclaims
    // them — just dropping the reference leaked a full world-map texture per Reload/Capture. Destroy both.
    // Safe: both callers (Reload image / Capture in MainPanel.Faust) synchronously RefreshFaustMapUnderlays(),
    // which ClearChildren()s every board that could hold an Image using this sprite (_faustHeatGo, _faustWsMapGo).
    // 0.67.1: also cancels a scheduled load and bumps the generation so an in-flight load can't commit after this.
    public static void Reset()
    {
        if (_scheduledLoad != null)
        {
            Behaviors.CoreUpdateBehavior.Actions.Remove(_scheduledLoad);
            _scheduledLoad = null;
        }
        _loadGen++;
        _loadState = LoadState.Idle;
        var old = _sprite;
        _sprite = null;
        DestroyPair(old, null);
    }

    private static bool SafeExists(string path)
    {
        try { return File.Exists(path); } catch { return false; }
    }

    // Best-effort destroy of a sprite and its texture (either may be null / already destroyed).
    private static void DestroyPair(Sprite sprite, Texture2D tex)
    {
        try { if (sprite && tex == null) tex = sprite.texture; } catch { }
        try { if (sprite) UnityEngine.Object.Destroy(sprite); } catch { }
        try { if (tex) UnityEngine.Object.Destroy(tex); } catch { }
    }

    // The scheduled loader (the ONLY place that decodes). Prefers the ≤2048px cache when its key matches the
    // current worldmap.png; otherwise decodes the original, downscales it on the GPU, keeps only the small copy in
    // memory, and writes the cache for next time. Any cache problem falls back to the full-size image (today's
    // behaviour). Commits only if no Reset() happened meanwhile (generation check), then raises ImageLoaded.
    private static void RunLoad(int gen)
    {
        _loadState = LoadState.Loading;
        // 0.65.6: build into locals and commit to _sprite only as the last step; anything not committed
        // (LoadImage failure, an exception mid-setup, a superseded generation) is destroyed in finally.
        Texture2D tex = null;
        Sprite sprite = null;
        bool committed = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Failure paths throw so they share the one epilogue below that raises ImageLoaded (boards leave
            // "Loading map image…" either way); only a superseded generation returns silently.
            var path = ImagePath;
            if (!File.Exists(path)) throw new FileNotFoundException("worldmap.png disappeared before it could load");
            string key = SourceKey(path);
            bool fromCache = false;

            tex = TryLoadCache(key);
            if (tex != null) fromCache = true;
            else
            {
                tex = DecodePng(File.ReadAllBytes(path));
                if (tex == null) throw new InvalidDataException("not a valid PNG");
                if (Math.Max(tex.width, tex.height) > CacheMaxDim)
                {
                    var small = Downscale(tex, CacheMaxDim);
                    if (small != null)
                    {
                        WriteCache(small, key);
                        UnityEngine.Object.Destroy(tex);   // drop the full-size copy (~148 MB for 6080²)
                        tex = small;
                    }
                }
            }

            if (gen != _loadGen) return;   // superseded by Reset() — finally destroys what we built
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            _sprite = sprite;
            committed = true;
            _loadState = LoadState.Done;
            LogUtils.LogInfo($"[Faust] world-map image loaded ({tex.width}x{tex.height}{(fromCache ? ", map cache used" : "")}) " +
                             $"in {sw.Elapsed.TotalMilliseconds:0} ms from {(fromCache ? CachePath : path)}");
        }
        catch (Exception e)
        {
            LogUtils.LogWarning($"[Faust] world-map image load failed: {e.Message}");
            if (gen == _loadGen) _loadState = LoadState.Failed;
        }
        finally
        {
            if (!committed) DestroyPair(sprite, tex);
            if (gen == _loadGen && _loadState == LoadState.Loading) _loadState = LoadState.Failed;
        }
        if (gen == _loadGen)
        {
            try { ImageLoaded?.Invoke(); }
            catch (Exception e) { LogUtils.LogDebug($"[Faust] ImageLoaded handler threw: {e.Message}"); }
        }
    }

    private static Texture2D DecodePng(byte[] data)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        bool ok = false;
        try
        {
            // IL2CPP: ImageConversion.LoadImage accepts a managed byte[] via the interop implicit conversion.
            ok = ImageConversion.LoadImage(tex, data);
            return ok ? tex : null;
        }
        finally
        {
            if (!ok) UnityEngine.Object.Destroy(tex);   // also on a throw — never leak the native texture
        }
    }

    // Identifies the source image version: size + last-write time. A new worldmap.png (drop-in / Capture) changes it.
    private static string SourceKey(string path)
    {
        var fi = new FileInfo(path);
        return $"{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
    }

    private static Texture2D TryLoadCache(string key)
    {
        Texture2D tex = null;
        try
        {
            if (!File.Exists(CachePath) || !File.Exists(CacheKeyPath)) return null;
            if (File.ReadAllText(CacheKeyPath).Trim() != key) return null;
            tex = DecodePng(File.ReadAllBytes(CachePath));
            if (tex == null) return null;
            if (tex.width <= 0 || tex.height <= 0 || Math.Max(tex.width, tex.height) > CacheMaxDim)
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            return tex;
        }
        catch (Exception e)
        {
            if (tex) UnityEngine.Object.Destroy(tex);
            LogUtils.LogWarning($"[Faust] map cache unreadable, using the full image: {e.Message}");
            return null;
        }
    }

    // GPU downscale to fit maxDim (aspect kept). Same Blit → ReadPixels readback FaustMapCapture uses.
    private static Texture2D Downscale(Texture2D src, int maxDim)
    {
        float f = maxDim / (float)Math.Max(src.width, src.height);
        int w = Math.Max(1, Mathf.RoundToInt(src.width * f));
        int h = Math.Max(1, Mathf.RoundToInt(src.height * f));
        RenderTexture rt = null;
        Texture2D small = null;
        var prev = RenderTexture.active;
        try
        {
            rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.filterMode = FilterMode.Bilinear;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            small = new Texture2D(w, h, TextureFormat.RGBA32, false);
            small.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            small.Apply(false);
            var done = small;
            small = null;   // handed to the caller — don't destroy in finally
            return done;
        }
        catch (Exception e)
        {
            LogUtils.LogWarning($"[Faust] map downscale failed, keeping the full image: {e.Message}");
            return null;
        }
        finally
        {
            RenderTexture.active = prev;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            if (small) UnityEngine.Object.Destroy(small);
        }
    }

    // Write the cache atomically: PNG to .tmp → replace → key file LAST (a missing/old key means "rebuild").
    private static void WriteCache(Texture2D small, string key)
    {
        var tmp = CachePath + ".tmp";
        try
        {
            var encoded = ImageConversion.EncodeToPNG(small);
            if (encoded == null || encoded.Length == 0) return;
            var data = new byte[encoded.Length];
            for (int i = 0; i < encoded.Length; i++) data[i] = encoded[i];
            if (File.Exists(CacheKeyPath)) File.Delete(CacheKeyPath);   // invalidate first; restored below on success
            if (File.Exists(tmp)) File.Delete(tmp);
            File.WriteAllBytes(tmp, data);
            File.Move(tmp, CachePath, true);
            File.WriteAllText(CacheKeyPath, key);
            LogUtils.LogInfo($"[Faust] map cache written ({small.width}x{small.height}) → {CachePath}");
        }
        catch (Exception e)
        {
            LogUtils.LogWarning($"[Faust] map cache write failed (the full image still works): {e.Message}");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    // Force `board` to hold the image's aspect ratio so the parent layout can't stretch the square map (the
    // World/Heat map containers force-expand the board's WIDTH to the panel). We drive WIDTH from a FIXED HEIGHT
    // (HeightControlsWidth): the caller pins the board's preferred HEIGHT, so the parent vertical group reserves
    // exactly that vertical space (no overlap with the filters above / caption below), and the fitter derives the
    // width from it (square when aspect == 1), overriding the force-expand.
    public static void FitBoardToImageAspect(GameObject board)
    {
        var fitter = board.GetComponent<AspectRatioFitter>();
        if (fitter == null) fitter = board.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
        fitter.aspectRatio = Mathf.Clamp(ImageAspect, 0.05f, 20f);
    }

    // Draw the backdrop as children of `board` (representing world rect [wMinX..wMaxX]×[wMinZ..wMaxZ], +X right /
    // +Z up). All children use NORMALIZED anchors so they fill the board at whatever pixel size the layout gives it
    // (the board width is force-expanded by the parent group, so pixel offsets can't be trusted). Returns true if a
    // map image was drawn.
    public static bool Decorate(GameObject board, int boardW, int boardH,
        float wMinX, float wMaxX, float wMinZ, float wMaxZ)
    {
        bool drewImage = false;
        // 0.67.1: never decode here — ask the scheduled loader; ImageLoaded redraws the boards when it's ready.
        if (ImagePending) RequestLoad();
        var spr = Settings.FaustWorldMapImage ? _sprite : null;
        if (spr != null)
        {
            var imgGo = UIFactory.CreateUIObject("MapImg", board);
            var im = imgGo.AddComponent<Image>();
            im.sprite = spr; im.type = Image.Type.Simple; im.preserveAspect = false; im.raycastTarget = false;
            im.color = new Color(1f, 1f, 1f, Settings.FaustWorldMapOpacity);
            var rt = imgGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            drewImage = true;
            // Let the image be the base: dim the board's own black fill so it doesn't mute the art.
            var bg = board.GetComponent<Image>();
            if (bg != null) bg.color = new Color(0f, 0f, 0f, 0.12f);
        }
        if (Settings.FaustWorldMapGrid)
            DrawGrid(board, wMinX, wMaxX, wMinZ, wMaxZ, drewImage);
        return drewImage;
    }

    private static void DrawGrid(GameObject board, float wMinX, float wMaxX, float wMinZ, float wMaxZ, bool overImage)
    {
        const int DIV = 5;
        var line = overImage ? new Color(1f, 1f, 1f, 0.13f) : new Color(0.50f, 0.60f, 0.75f, 0.20f);
        for (int i = 0; i <= DIV; i++)
        {
            float f = i / (float)DIV;
            AddLineV(board, f, line);   // vertical line at horizontal fraction f
            AddLineH(board, f, line);   // horizontal line at vertical fraction f
        }
        // Corner axis labels (world coords), anchored to the board corners. +X→right, +Z→up.
        AddCornerLabel(board, $"({wMinX:0},{wMinZ:0})", new Vector2(0f, 0f), new Vector2(3f, 2f), overImage);     // bottom-left
        AddCornerLabel(board, $"({wMaxX:0},{wMinZ:0})", new Vector2(1f, 0f), new Vector2(-3f, 2f), overImage);    // bottom-right
        AddCornerLabel(board, $"({wMinX:0},{wMaxZ:0})", new Vector2(0f, 1f), new Vector2(3f, -2f), overImage);    // top-left
    }

    private static void AddLineV(GameObject parent, float fx, Color c)
    {
        var go = UIFactory.CreateUIObject("gx", parent);
        var img = go.AddComponent<Image>(); img.color = c; img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(fx, 0f); rt.anchorMax = new Vector2(fx, 1f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1f, 0f); rt.anchoredPosition = Vector2.zero;
    }

    private static void AddLineH(GameObject parent, float fz, Color c)
    {
        var go = UIFactory.CreateUIObject("gz", parent);
        var img = go.AddComponent<Image>(); img.color = c; img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, fz); rt.anchorMax = new Vector2(1f, fz); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(0f, 1f); rt.anchoredPosition = Vector2.zero;
    }

    private static void AddCornerLabel(GameObject parent, string text, Vector2 corner, Vector2 offset, bool overImage)
    {
        var lbl = UIFactory.CreateLabel(parent, "axislbl", text, TextAlignmentOptions.Center, color: null, fontSize: Theme.ScaledUI(10));
        lbl.TextMesh.color = overImage ? new Color(1f, 1f, 1f, 0.85f) : new Color(0.72f, 0.78f, 0.88f, 0.85f);
        var rt = lbl.GameObject.GetComponent<RectTransform>();
        rt.anchorMin = corner; rt.anchorMax = corner; rt.pivot = corner;
        rt.sizeDelta = new Vector2(Theme.ScaledWidth(84), Theme.ScaledHeight(14));
        rt.anchoredPosition = offset;
    }
}
