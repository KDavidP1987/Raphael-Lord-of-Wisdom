using System;
using Raphael.Resources;
using Raphael.UI.Framework.CustomLib.Util;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using Raphael.Utils;
using TMPro;
using UnityEngine;

namespace Raphael.UI.ModContent;

// 0.74.0 (PLAN C5): Settings and Help → Reference: Units & Objects. Every unit and world object with its PrefabGUID,
// searchable and filterable, with copy buttons. The floating copy (GameCatalogOverlayPanel) is opened from here or
// from a Browse… button beside a form field (AddCatalogBrowseButton below), and writes the picked row into it.
public partial class MainPanel
{
    // Kept across tab rebuilds so the search and filters survive a page rebuild.
    private readonly GameCatalogView _gameCatalogTabView = new();
    private GameCatalogBrowser _gameCatalogTabBrowser;
    private Action _gameCatalogTabTicker;

    private void BuildGameCatalogTab(GameObject page)
    {
        AddGuideSection(page, "Reference: Units & Objects",
            "Every unit (enemies, V Bloods, minions, NPCs) and world object (castle buildables, containers, resource " +
            "nodes, trees) in the game, with its internal name and PrefabGUID. Search by name, internal name or GUID. " +
            "Names come from your game's own text once Raphael has read them; a row marked <i>(from prefab name)</i> " +
            "shows a readable form of the internal name instead. Copy name / Copy GUID put the value on the clipboard. " +
            "A <b>Browse…</b> button beside a form field opens the floating reference and fills that field.");

        var top = AddCard(page, "GameCatalogTopCard");
        var row = UIFactory.CreateHorizontalGroup(top, "GameCatalogTopRow",
            forceExpandWidth: false, forceExpandHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(row, minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        AddCatalogButton(row, "GameCatalogOpenOverlay", "Open floating reference",
            "Open the reference in its own draggable window, to keep it beside another tab.", OpenGameCatalogOverlay, 190);
        AddBodyText(top, $"{GameCatalog.Count} rows · game data {GameCatalogData.GameDataVersion}.", Theme.ScaledUI(11));

        var card = AddCard(page, "GameCatalogCard");
        _gameCatalogTabBrowser = new GameCatalogBrowser(_gameCatalogTabView, "GameCatalogTab", overlay: false);
        _gameCatalogTabBrowser.Build(card);

        if (_gameCatalogTabTicker == null)
        {
            _gameCatalogTabTicker = () =>
            {
                var b = _gameCatalogTabBrowser;
                if (b == null || !b.Tick())
                {
                    _gameCatalogTabBrowser = null;
                    Behaviors.CoreUpdateBehavior.Actions.Remove(_gameCatalogTabTicker);
                    _gameCatalogTabTicker = null;
                }
            };
            Behaviors.CoreUpdateBehavior.Actions.Add(_gameCatalogTabTicker);
        }
    }

    private void OnGameCatalogTabShown()
    {
        GameCatalogNames.Start();
        _gameCatalogTabBrowser?.Render();
    }

    // ------------------------------------------------------------------ entry points (also used by the Nyar session)

    /// <summary>Open the floating reference with no form target (rows show copy buttons).</summary>
    internal void OpenGameCatalogOverlay() => GameCatalogOverlayPanel.Open(Owner, null);

    /// <summary>Open the floating reference aimed at a form field; "Use" on a row writes it there.</summary>
    internal void OpenCatalogBrowse(CatalogBrowseTarget target) => GameCatalogOverlayPanel.Open(Owner, target);

    /// <summary>
    /// Add a Browse… button to <paramref name="parent"/> (normally the row that holds <paramref name="input"/>).
    /// Clicking it opens the floating reference on <paramref name="kinds"/>, starting from <paramref name="startFilter"/>
    /// (null = the field's current text), and a picked row writes <paramref name="format"/> into the field.
    /// <paramref name="owner"/> is the form the field lives on (null = <paramref name="parent"/>); a closed or hidden
    /// owner is never written.
    /// </summary>
    internal ButtonRef AddCatalogBrowseButton(GameObject parent, InputFieldRef input, GameObject owner,
        BrowseKinds kinds, BrowseFormat format, string startFilter = null, string fieldLabel = null,
        string tooltip = null, int width = 0)
    {
        string label = CatalogBrowse.ButtonLabel(kinds);
        string what = kinds == BrowseKinds.Unit ? "units" : kinds == BrowseKinds.Object ? "objects" : "units and objects";
        string writes = format switch
        {
            BrowseFormat.Guid => "its PrefabGUID",
            BrowseFormat.ShortName => "its name without CHAR_",
            _ => "its internal name",
        };
        tooltip ??= $"Pick from the reference of {what}; Use writes {writes} into this field.";
        return AddCatalogButton(parent, (input?.GameObject != null ? input.GameObject.name : "Field") + "Browse", label, tooltip, () =>
        {
            string start = startFilter ?? (input?.Text ?? "").Trim();
            OpenCatalogBrowse(new CatalogBrowseTarget
            {
                Input = input,
                Owner = owner ?? parent,
                Kinds = kinds,
                Format = format,
                StartFilter = start,
                Label = fieldLabel ?? "this field",
            });
        }, width > 0 ? width : (kinds == BrowseKinds.Both ? 180 : 80));
    }

    // Own button helper: the Beelz/Uriel helpers are gated on those mods being present; the reference is not.
    private static ButtonRef AddCatalogButton(GameObject parent, string name, string label, string tooltip, Action onClick, int width)
    {
        var btn = UIFactory.CreateButton(parent, name, label);
        UIFactory.SetLayoutElement(btn.GameObject, minWidth: Theme.ScaledWidth(width), preferredWidth: Theme.ScaledWidth(width),
            flexibleWidth: 0, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null)
        {
            t.fontSize = Theme.ScaledUI(12);
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
        }
        if (!string.IsNullOrEmpty(tooltip)) TooltipHover.Attach(btn.GameObject, tooltip);
        btn.OnClick = () =>
        {
            try { onClick(); }
            catch (Exception ex) { LogUtils.LogError($"[GameCatalog] '{name}' threw: {ex}"); }
        };
        return btn;
    }
}
