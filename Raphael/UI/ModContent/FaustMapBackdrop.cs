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
    private static bool _tried;

    private static string ImagePath =>
        Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME, "worldmap.png");

    // The on-disk path the user drops worldmap.png into (shown in the UI so they can find it).
    public static string ImageFilePath => ImagePath;

    // True when the user has supplied a usable image AND wants it drawn.
    public static bool HasImage => Settings.FaustWorldMapImage && LoadSprite() != null;

    // The loaded image's pixel aspect ratio (width / height), or 1.0 (square) when there's no image. Callers use
    // this to give the render rectangle the same aspect so the map isn't stretched (the V Rising map is square).
    public static float ImageAspect
    {
        get
        {
            var s = LoadSprite();
            if (s == null || s.texture == null || s.texture.height == 0) return 1f;
            return (float)s.texture.width / s.texture.height;
        }
    }

    // True when SOME backdrop (image or grid) will draw — i.e. callers should use the fixed map rectangle.
    public static bool Active => Settings.FaustWorldMapGrid || HasImage;

    // A human-readable status line for the underlay card, so a black board isn't mysterious.
    public static string StatusLine()
    {
        var spr = LoadSprite();
        if (spr != null && spr.texture != null)
            return $"<color=#8FBF6F>Map image loaded</color> — {spr.texture.width}×{spr.texture.height}px.";
        if (!Settings.FaustWorldMapImage)
            return "<color=#B0B0B0>Map image is turned off (grid only). Enable it above to use worldmap.png.</color>";
        if (!File.Exists(ImagePath))
            return $"<color=#FFB070>No map image yet</color> — drop a PNG named <b>worldmap.png</b> into <b>BepInEx/config/Raphael/</b>, then click <b>Reload image</b>. (Until then you'll see the grid only.)";
        return "<color=#FF8080>worldmap.png is present but couldn't be loaded</color> — make sure it's a valid PNG, then Reload image.";
    }

    // Forget the cached sprite so a freshly dropped-in worldmap.png is picked up without a game restart.
    // 0.65.6 LEAK FIX: the sprite + texture are HideAndDontSave, so Resources.UnloadUnusedAssets never reclaims
    // them — just dropping the reference leaked a full world-map texture per Reload/Capture. Destroy both.
    // Safe: both callers (Reload image / Capture in MainPanel.Faust) synchronously RefreshFaustMapUnderlays(),
    // which ClearChildren()s every board that could hold an Image using this sprite (_faustHeatGo, _faustWsMapGo).
    public static void Reset()
    {
        var old = _sprite;
        _sprite = null;
        _tried = false;
        DestroyPair(old, null);
    }

    // Best-effort destroy of a sprite and its texture (either may be null / already destroyed).
    private static void DestroyPair(Sprite sprite, Texture2D tex)
    {
        try { if (sprite && tex == null) tex = sprite.texture; } catch { }
        try { if (sprite) UnityEngine.Object.Destroy(sprite); } catch { }
        try { if (tex) UnityEngine.Object.Destroy(tex); } catch { }
    }

    private static Sprite LoadSprite()
    {
        if (_tried) return _sprite;
        _tried = true;
        // 0.65.6: build into locals and commit to _sprite only as the last step; anything not committed
        // (LoadImage failure, an exception mid-setup) is destroyed in finally instead of leaking.
        Texture2D tex = null;
        Sprite sprite = null;
        bool committed = false;
        try
        {
            var path = ImagePath;
            if (!File.Exists(path)) return null;
            var data = File.ReadAllBytes(path);
            tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            // IL2CPP: ImageConversion.LoadImage accepts a managed byte[] via the interop implicit conversion.
            if (!ImageConversion.LoadImage(tex, data)) return null;
            tex.hideFlags = HideFlags.HideAndDontSave;
            sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            LogUtils.LogInfo($"[Faust] world-map image loaded ({tex.width}x{tex.height}) from {path}");
            _sprite = sprite;
            committed = true;
            return _sprite;
        }
        catch (Exception e)
        {
            LogUtils.LogWarning($"[Faust] world-map image load failed: {e.Message}");
            return null;
        }
        finally
        {
            if (!committed) DestroyPair(sprite, tex);
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
        var spr = Settings.FaustWorldMapImage ? LoadSprite() : null;
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
