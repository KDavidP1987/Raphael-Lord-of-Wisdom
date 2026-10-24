using System;
using System.Collections.Generic;
using Raphael.Config;
using Raphael.Resources;
using Raphael.UI.Framework.CustomLib.Panel;
using Raphael.UI.Framework.CustomLib.Util;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using Raphael.UI.Framework.UniverseLib.UI.Panels;
using Raphael.UI.ModContent.Data;
using Raphael.Utils;
using TMPro;
using UnityEngine;
using UIBase = Raphael.UI.Framework.UniverseLib.UI.UIBase;

namespace Raphael.UI.ModContent;

/// <summary>0.74.0 (PLAN C6/C8): where a Browse… button wants the picked row written.</summary>
public sealed class CatalogBrowseTarget
{
    /// <summary>The form field that receives the value.</summary>
    public InputFieldRef Input;
    /// <summary>The form (card or page) the field lives on. Filling is refused when it is gone or hidden.</summary>
    public GameObject Owner;
    public BrowseKinds Kinds = BrowseKinds.Both;
    public BrowseFormat Format = BrowseFormat.Name;
    /// <summary>The search text the reference opens with.</summary>
    public string StartFilter = "";
    /// <summary>Shown in the reference's header ("Filling: …").</summary>
    public string Label = "a form field";

    internal bool IsAlive()
    {
        try
        {
            if (Input?.Component == null) return false;
            if (!Input.Component.gameObject.activeInHierarchy) return false;
            if (Owner != null && !Owner.activeInHierarchy) return false;
            return true;
        }
        catch { return false; }
    }
}

/// <summary>
/// 0.74.0: the controls of the unit and object reference, built from one <see cref="GameCatalogView"/> into any
/// container. The Settings and Help tab and the floating overlay each own one; nothing is reparented.
/// </summary>
internal sealed class GameCatalogBrowser
{
    private const float DebounceSeconds = 0.15f;

    public readonly GameCatalogView View;
    private readonly string _prefix;
    private readonly bool _overlay;

    /// <summary>Set by the overlay while a Browse… target is active: rows show "Use" instead of the copy buttons.</summary>
    public Func<CatalogRow, bool> CanPick;
    public Action<CatalogRow> OnPick;
    /// <summary>Extra text for the status line (the overlay's fill messages).</summary>
    public string Notice = "";

    private GameObject _root, _rows, _pager, _unitFilters, _objFilters;
    private InputFieldRef _search;
    private TextMeshProUGUI _status;
    private ButtonRef _tabUnits, _tabObjects, _tabAll, _vb, _minion, _buildable, _container, _groupBy;
    private TMP_Dropdown _factionDd, _groupDd, _subDd;
    private readonly List<string> _factions = new(), _groups = new(), _subs = new();
    private string _pending;
    private float _pendingAt;
    private bool _settingSearch;

    public GameCatalogBrowser(GameCatalogView view, string namePrefix, bool overlay)
    {
        View = view ?? new GameCatalogView();
        _prefix = namePrefix;
        _overlay = overlay;
    }

    private int Font(int s) => _overlay ? Theme.ScaledOverlay(s) : Theme.ScaledUI(s);
    private int H(int h) => _overlay ? Theme.ScaledOverlayHeight(h) : Theme.ScaledHeight(h);
    private int W(int w) => Theme.ScaledWidth(w);
    private int MinW => _overlay ? 220 : 360;

    public bool Alive
    {
        get { try { return _root != null && _search?.Component != null; } catch { return false; } }
    }

    // ------------------------------------------------------------------ build

    /// <summary>Build the controls into <paramref name="parent"/>. <paramref name="rowsParent"/> receives the result
    /// rows (the overlay passes its scroll content; the tab passes null and rows go under the controls).</summary>
    public void Build(GameObject parent, GameObject rowsParent = null)
    {
        _root = parent;

        // Search + type tabs.
        var searchRow = Row(parent, "Search");
        _search = UIFactory.CreateInputField(searchRow, _prefix + "Search", "Search by name, internal name or GUID…");
        UIFactory.SetLayoutElement(_search.GameObject, minWidth: 140, preferredWidth: 260, flexibleWidth: 1,
            minHeight: H(26), preferredHeight: H(28), flexibleHeight: 0);
        _search.OnValueChanged += v =>
        {
            if (_settingSearch) return;
            _pending = v ?? "";
            _pendingAt = Time.realtimeSinceStartup + DebounceSeconds;
        };
        Button(searchRow, "Clear", "Clear", "Clear the search.", () => SetSearch(""), 56);

        var tabRow = Row(parent, "Tabs");
        _tabUnits = Button(tabRow, "TabUnits", "Units", "Characters: enemies, V Bloods, minions, NPCs.", () => { View.Tab = CatalogTab.Units; Render(); }, 76);
        _tabObjects = Button(tabRow, "TabObjects", "Objects", "World and castle objects: buildables, containers, resource nodes, trees.", () => { View.Tab = CatalogTab.Objects; Render(); }, 76);
        _tabAll = Button(tabRow, "TabAll", "All", "Units and objects together (search and Group by only).", () => { View.Tab = CatalogTab.All; Render(); }, 56);
        _groupBy = Button(tabRow, "GroupBy", "", "Group results by faction (units) or category (objects).", () => { View.GroupBy = !View.GroupBy; Render(); }, 110);

        // Unit filters.
        _unitFilters = Row(parent, "UnitFilters");
        _vb = Button(_unitFilters, "VBlood", "", "Only V Blood bosses.", () => { View.VBloodOnly = !View.VBloodOnly; Render(); }, 92);
        _minion = Button(_unitFilters, "Minion", "", "Only minions (summons and spawned helpers).", () => { View.MinionOnly = !View.MinionOnly; Render(); }, 84);
        _factions.Clear(); _factions.Add("Any faction"); _factions.AddRange(GameCatalogView.Factions());
        _factionDd = Dropdown(_unitFilters, "Faction", _factions, i => { View.Faction = i <= 0 ? "" : _factions[i]; Render(); }, 150);

        // Object filters.
        _objFilters = Row(parent, "ObjectFilters");
        _groups.Clear(); _groups.Add("Any category"); _groups.AddRange(GameCatalogView.ObjectGroups());
        _groupDd = Dropdown(_objFilters, "Category", _groups, i =>
        {
            View.ObjectGroup = i <= 0 ? "" : _groups[i];
            RefillSubGroups();
            Render();
        }, 120);
        _subs.Clear(); _subs.Add("Any sub-group");
        _subDd = Dropdown(_objFilters, "SubGroup", _subs, i => { View.ObjectSubGroup = i <= 0 ? "" : _subs[i]; Render(); }, 140);
        _buildable = Button(_objFilters, "Buildable", "", "Only objects with a castle blueprint.", () => { View.BuildableOnly = !View.BuildableOnly; Render(); }, 86);
        _container = Button(_objFilters, "Container", "", "Only objects with an inventory.", () => { View.ContainerOnly = !View.ContainerOnly; Render(); }, 86);

        _status = Label(parent, "Status", "", 11, wrap: true);

        var rowsHost = rowsParent ?? parent;
        _rows = UIFactory.CreateVerticalGroup(rowsHost, _prefix + "Rows",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_rows, minWidth: MinW, preferredWidth: 400, flexibleWidth: 1, minHeight: 20, flexibleHeight: 0);

        _pager = Row(rowsHost, "Pager");

        SyncSearchBox();
        RefillSubGroups();
        Render();
    }

    /// <summary>Set the search text now (no debounce) and show it in the box.</summary>
    public void SetSearch(string text)
    {
        _pending = null;
        View.Text = text ?? "";
        SyncSearchBox();
        Render();
    }

    /// <summary>Re-show the view's state in the controls (after a Browse target reset the filters).</summary>
    public void SyncControls()
    {
        SyncSearchBox();
        SelectIn(_factionDd, _factions, View.Faction);
        SelectIn(_groupDd, _groups, View.ObjectGroup);
        RefillSubGroups();
        Render();
    }

    private void SyncSearchBox()
    {
        if (_search?.Component == null) return;
        if (_search.Text == View.Text) return;
        _settingSearch = true;
        try { _search.Text = View.Text; } finally { _settingSearch = false; }
    }

    private void RefillSubGroups()
    {
        if (_subDd == null) return;
        _subs.Clear(); _subs.Add("Any sub-group");
        _subs.AddRange(GameCatalogView.SubGroupsFor(View.ObjectGroup));
        SetOptions(_subDd, _subs);
        SelectIn(_subDd, _subs, View.ObjectSubGroup);
        try { _subDd.gameObject.SetActive(!string.IsNullOrEmpty(View.ObjectGroup) && _subs.Count > 1); } catch { }
    }

    // ------------------------------------------------------------------ per frame

    /// <summary>Host ticker. Returns false once the controls are destroyed (the host then unregisters).</summary>
    public bool Tick()
    {
        if (!Alive) return false;
        try
        {
            if (!_root.activeInHierarchy) return true;
            if (_pending != null && Time.realtimeSinceStartup >= _pendingAt)
            {
                View.Text = _pending;
                _pending = null;
            }
            if (View.NeedsRefresh) Render();
        }
        catch (Exception ex) { LogUtils.LogDebug($"[GameCatalog] tick: {ex.Message}"); }
        return true;
    }

    // ------------------------------------------------------------------ render

    public void Render()
    {
        if (!Alive || _rows == null) return;
        View.Refresh();

        var tab = View.Tab;
        SetOn(_tabUnits, tab == CatalogTab.Units, "Units");
        SetOn(_tabObjects, tab == CatalogTab.Objects, "Objects");
        SetOn(_tabAll, tab == CatalogTab.All, "All");
        Show(_tabUnits, View.TabAllowed(CatalogTab.Units));
        Show(_tabObjects, View.TabAllowed(CatalogTab.Objects));
        Show(_tabAll, View.TabAllowed(CatalogTab.All));
        SetOn(_groupBy, View.GroupBy, "Group by");
        SetOn(_vb, View.VBloodOnly, "V Blood");
        SetOn(_minion, View.MinionOnly, "Minion");
        SetOn(_buildable, View.BuildableOnly, "Buildable");
        SetOn(_container, View.ContainerOnly, "Container");
        try { _unitFilters.SetActive(tab == CatalogTab.Units); _objFilters.SetActive(tab == CatalogTab.Objects); } catch { }

        ClearChildren(_rows);
        ClearChildren(_pager);

        int total = View.Total;
        string status;
        if (GameCatalog.Count == 0) status = "<color=#FFB070>The reference table did not load (see LogOutput.log).</color>";
        else if (total == 0) status = $"<color={Theme.MutedBodyHex}>No matches.</color>";
        else if (View.GroupBy) status = $"<color=#90EE90>{total}</color> matches in {View.Groups.Count} groups. Open one to see its rows.";
        else status = $"<color=#90EE90>{total}</color> matches · page {View.Page + 1}/{View.PageCount}";
        if (!GameCatalogNames.Done) status += $"  <color={Theme.MutedBodyHex}>(reading names from your game…)</color>";
        if (!string.IsNullOrEmpty(Notice)) status = Notice + "\n" + status;
        _status.text = status;

        if (View.GroupBy) RenderGroups(); else RenderFlat();
    }

    private void RenderFlat()
    {
        foreach (var r in View.PageRows()) AddRow(_rows, r);
        if (View.PageCount <= 1) return;
        if (View.CanPrev) Button(_pager, "Prev", "← Prev", "Previous page.", () => { View.PrevPage(); Render(); }, 76);
        var lbl = Label(_pager, "Page", $"{View.Page + 1}/{View.PageCount}", 12, wrap: false);
        lbl.alignment = TextAlignmentOptions.Center;
        if (View.CanNext) Button(_pager, "Next", "Next →", "Next page.", () => { View.NextPage(); Render(); }, 76);
    }

    private void RenderGroups()
    {
        string open = View.OpenGroup;
        foreach (var g in View.Groups)
        {
            var key = g.Key;
            bool isOpen = key == open;
            var head = Button(_rows, "Group_" + key, $"{(isOpen ? "-" : "+")}  {g.Title}  ({g.Total})",
                isOpen ? "Close this group." : "Open this group (closes the open one).",
                () => { View.ToggleGroup(key); Render(); }, 0);
            var t = head.Component.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null) t.alignment = TextAlignmentOptions.MidlineLeft;
            if (!isOpen) continue;
            foreach (var r in View.OpenRows()) AddRow(_rows, r);
            if (View.CanShowMore)
                Button(_rows, "ShowMore", $"Show {GameCatalogView.ShowMoreStep} more", "Show more rows of this group.", () => { View.ShowMore(); Render(); }, 0);
            else if (View.MustRefine)
                Label(_rows, "Refine", $"<color={Theme.MutedBodyHex}>Refine your search to see more ({g.Total} in this group).</color>", 11, wrap: true);
        }
    }

    private void AddRow(GameObject parent, CatalogRow r)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "Row_" + r.Guid,
            forceExpandWidth: false, forceExpandHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row, minWidth: MinW, preferredWidth: 400, flexibleWidth: 1, minHeight: H(34), flexibleHeight: 0);

        var text = UIFactory.CreateVerticalGroup(row, "Text",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 0, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(text, minWidth: 120, preferredWidth: 280, flexibleWidth: 1, minHeight: H(32), flexibleHeight: 0);

        string name = Escape(r.DisplayName);
        if (r.NameSource == NameSource.PrefabName)
            name += $" <color={Theme.MutedBodyHex}><size=80%>(from prefab name)</size></color>";
        Label(text, "Name", name, 12, wrap: false);
        Label(text, "Detail", $"<color={Theme.MutedBodyHex}>{Escape(GameCatalogView.Detail(r))}</color>", 10, wrap: false);

        if (OnPick != null && (CanPick == null || CanPick(r)))
        {
            var row0 = r;
            Button(row, "Use", "Use", "Write this into the form field you browsed from.", () => OnPick(row0), 52);
        }
        else
        {
            string internalName = r.InternalName, guid = r.Guid.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Button(row, "CopyName", "Copy name", "Copy the internal name (" + internalName + ").", () => Copy(internalName), 82);
            Button(row, "CopyGuid", "Copy GUID", "Copy the PrefabGUID (" + guid + ").", () => Copy(guid), 82);
        }
    }

    private static void Copy(string s)
    {
        try { GUIUtility.systemCopyBuffer = s; } catch { }
    }

    // TMP rich text: names come from game text or prefab names; strip tag brackets so they cannot style the row.
    private static string Escape(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "‹").Replace(">", "›");

    // ------------------------------------------------------------------ small builders

    private GameObject Row(GameObject parent, string name)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, _prefix + name,
            forceExpandWidth: false, forceExpandHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(row, minWidth: MinW, preferredWidth: 400, flexibleWidth: 1,
            minHeight: H(28), preferredHeight: H(30), flexibleHeight: 0);
        return row;
    }

    private ButtonRef Button(GameObject parent, string name, string label, string tooltip, Action onClick, int width)
    {
        var btn = UIFactory.CreateButton(parent, _prefix + name, label);
        if (width > 0)
            UIFactory.SetLayoutElement(btn.GameObject, minWidth: W(width), preferredWidth: W(width), flexibleWidth: 0,
                minHeight: H(24), preferredHeight: H(26), flexibleHeight: 0);
        else
            UIFactory.SetLayoutElement(btn.GameObject, minWidth: MinW, preferredWidth: 400, flexibleWidth: 1,
                minHeight: H(24), preferredHeight: H(26), flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null)
        {
            t.fontSize = Font(11);
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

    private TextMeshProUGUI Label(GameObject parent, string name, string text, int size, bool wrap)
    {
        var lbl = UIFactory.CreateLabel(parent, _prefix + name, text, TextAlignmentOptions.MidlineLeft, color: null, fontSize: Font(size));
        UIFactory.SetLayoutElement(lbl.GameObject, minWidth: 100, preferredWidth: 300, flexibleWidth: 1,
            minHeight: H(size + 6), flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = wrap;
        lbl.TextMesh.overflowMode = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
        if (wrap)
        {
            var fitter = lbl.GameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        }
        return lbl.TextMesh;
    }

    private TMP_Dropdown Dropdown(GameObject parent, string name, List<string> options, Action<int> onChanged, int width)
    {
        var go = UIFactory.CreateDropdown(parent, _prefix + name, out var dd, options.Count > 0 ? options[0] : "", Font(12),
            i => { try { onChanged(i); } catch (Exception ex) { LogUtils.LogError($"[GameCatalog] dropdown '{name}' threw: {ex}"); } },
            options.ToArray());
        UIFactory.SetLayoutElement(go, minWidth: W(width), preferredWidth: W(width), flexibleWidth: 1,
            minHeight: H(26), preferredHeight: H(28), flexibleHeight: 0);
        try
        {
            if (dd.captionText != null) { dd.captionText.enableWordWrapping = false; dd.captionText.overflowMode = TextOverflowModes.Ellipsis; }
            if (dd.itemText != null) { dd.itemText.enableWordWrapping = false; dd.itemText.overflowMode = TextOverflowModes.Ellipsis; }
        }
        catch { }
        Raphael.UI.Forms.FormDropdownRegistry.Register(dd);
        return dd;
    }

    // IL2CPP-safe option refill: mutate .options directly (see MainPanel.Faust SetFaustDropdownOptions).
    private static void SetOptions(TMP_Dropdown dd, List<string> opts)
    {
        if (dd == null) return;
        try
        {
            dd.options.Clear();
            foreach (var o in opts) dd.options.Add(new TMP_Dropdown.OptionData(o));
            dd.RefreshShownValue();
        }
        catch { }
    }

    private static void SelectIn(TMP_Dropdown dd, List<string> opts, string value)
    {
        if (dd == null) return;
        int i = string.IsNullOrEmpty(value) ? 0 : opts.FindIndex(o => string.Equals(o, value, StringComparison.OrdinalIgnoreCase));
        if (i < 0) i = 0;
        try { dd.SetValueWithoutNotify(i); dd.RefreshShownValue(); } catch { }
    }

    private static void SetOn(ButtonRef b, bool on, string label)
    {
        if (b?.Component == null) return;
        var t = b.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.text = on ? $"<color=#90EE90>{label}: on</color>" : label;
    }

    private static void Show(ButtonRef b, bool show)
    {
        try { b?.GameObject.SetActive(show); } catch { }
    }

    private static void ClearChildren(GameObject go)
    {
        if (go == null) return;
        var t = go.transform;
        for (int i = t.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
    }
}

/// <summary>
/// 0.74.0 (PLAN C5/C6/C8): the floating unit and object reference. Opened from the reference tab ("Open floating
/// reference") or from a Browse… button next to a form field; with a Browse target, "Use" writes the row into that
/// field (the newest Browse replaces the target). A field that was closed is never written.
///
/// Not registered in BCHubUIManager (outside this change's file list): it is created on first open under the main
/// panel's UIBase, and recreated if that UIBase was rebuilt (server change).
/// </summary>
public class GameCatalogOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "GameCatalogOverlay";
    public override PanelType PanelType => PanelType.GameCatalogOverlay;
    public override int MinWidth => 300;
    public override int MinHeight => 300;
    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot => new(0.5f, 0.5f);
    public override Vector2 DefaultPosition => new(Owner.Scaler.m_ReferenceResolution.x * 0.5f - 300f, 120f);
    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.TransparencyToAlpha(Settings.UITransparency);

    private static GameCatalogOverlayPanel _instance;

    private readonly GameCatalogView _view = new();
    private GameCatalogBrowser _browser;
    private CatalogBrowseTarget _target;
    private TextMeshProUGUI _targetLabel;
    private ButtonRef _stopBtn;
    private Action _ticker;
    private bool _loggedClosedTarget;

    public GameCatalogOverlayPanel(UIBase owner) : base(owner) { }

    /// <summary>Open (creating on first use) the floating reference. <paramref name="target"/> null = plain
    /// reference with copy buttons; otherwise it replaces any earlier Browse target.</summary>
    internal static void Open(UIBase owner, CatalogBrowseTarget target)
    {
        if (owner == null) return;
        try
        {
            if (_instance != null && (_instance.Owner != owner || _instance.UIRoot == null)) _instance = null;
        }
        catch { _instance = null; }
        if (_instance == null)
        {
            _instance = new GameCatalogOverlayPanel(owner);
            _instance.SetActive(false);
        }
        _instance.Show(target);
    }

    internal static bool IsOpen
    {
        get { try { return _instance != null && _instance.UIRoot != null && _instance.Enabled; } catch { return false; } }
    }

    // Called by BCHubUIManager: Reset (leaving a server) destroys the panel; SetUIActive hides it with the rest of the UI
    // and brings it back only if it was open.
    private static bool _wasOpen;

    internal static void Teardown()
    {
        try { if (_instance != null) { _instance.Reset(); _instance.Destroy(); } }
        catch { /* already gone with its canvas */ }
        _instance = null;
        _wasOpen = false;
        GameCatalogNames.Reset();   // a new world gets a fresh resolver pass
    }

    internal static void SetUIActive(bool active)
    {
        try
        {
            if (_instance == null || _instance.UIRoot == null) return;
            if (!active) { _wasOpen = _instance.Enabled; _instance.SetActive(false); }
            else if (_wasOpen) _instance.SetActive(true);
        }
        catch { _instance = null; }
    }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();
        SetTitle("Units & Objects");
        var rootVlg = ContentRoot.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (rootVlg != null) rootVlg.childForceExpandHeight = false;

        // Header: title, target line, Stop filling, Close (the title bar is hidden on overlays).
        var head = UIFactory.CreateHorizontalGroup(ContentRoot, "CatOvHead",
            forceExpandWidth: false, forceExpandHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(head, minWidth: 260, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledOverlayHeight(26), preferredHeight: Theme.ScaledOverlayHeight(28), flexibleHeight: 0);
        _targetLabel = UIFactory.CreateLabel(head, "CatOvTarget", "", TextAlignmentOptions.MidlineLeft, color: null,
            fontSize: Theme.ScaledOverlay(12)).TextMesh;
        UIFactory.SetLayoutElement(_targetLabel.gameObject, minWidth: 120, preferredWidth: 260, flexibleWidth: 1,
            minHeight: Theme.ScaledOverlayHeight(24), flexibleHeight: 0);
        _targetLabel.enableWordWrapping = false;
        _targetLabel.overflowMode = TextOverflowModes.Ellipsis;
        _stopBtn = HeadButton(head, "CatOvStop", "Stop filling", "Stop writing into the form field; rows show the copy buttons again.",
            () => SetTarget(null), 92);
        HeadButton(head, "CatOvClose", "Close", "Close the floating reference.", () => SetActive(false), 60);

        // Controls stay above; rows scroll.
        var controls = UIFactory.CreateVerticalGroup(ContentRoot, "CatOvControls",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 3, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(controls, minWidth: 260, preferredWidth: 400, flexibleWidth: 1, flexibleHeight: 0);

        var scroll = UIFactory.CreateScrollView(ContentRoot, "CatOvScroll", out var scrollContent, out _, color: new Color(0f, 0f, 0f, 0f));
        UIFactory.SetLayoutElement(scroll, minWidth: 260, preferredWidth: 400, flexibleWidth: 1, minHeight: 120, preferredHeight: 300, flexibleHeight: 1);
        var bg = scroll.GetComponent<UnityEngine.UI.Image>();
        if (bg != null) bg.color = new Color(0f, 0f, 0f, 0f);

        _browser = new GameCatalogBrowser(_view, "CatOv", overlay: true)
        {
            CanPick = r => _target != null && CatalogBrowse.Allows(_target.Kinds, r.Kind),
        };
        _browser.Build(controls, scrollContent);

        _ticker = () =>
        {
            if (_browser == null || !_browser.Tick())
            {
                Behaviors.CoreUpdateBehavior.Actions.Remove(_ticker);
                return;
            }
            // A Browse target whose form closed: drop it the next time the overlay notices (C8).
            if (_target != null && Enabled && !_target.IsAlive()) TargetGone();
        };
        Behaviors.CoreUpdateBehavior.Actions.Add(_ticker);
        UpdateTargetLine();
    }

    private void Show(CatalogBrowseTarget target)
    {
        GameCatalogNames.Start();
        if (target != null)
        {
            _view.AllowedKinds = target.Kinds;
            _view.Tab = CatalogBrowse.StartTab(target.Kinds);
            _view.ResetFilters(target.StartFilter ?? "");
            SetTarget(target);
            _browser?.SyncControls();
        }
        else if (_target != null)
        {
            // Opened from Help: an earlier Browse target must not keep its "Use" buttons.
            SetTarget(null);
            _browser?.SyncControls();
        }
        if (!Enabled) SetActive(true);
        else { UIRoot.transform.SetAsLastSibling(); }
        try { EnsureValidSize(); EnsureValidPosition(); } catch { }
        _browser?.Render();
    }

    private void SetTarget(CatalogBrowseTarget target)
    {
        _target = target;
        _loggedClosedTarget = false;
        if (target == null) _view.AllowedKinds = BrowseKinds.Both;
        if (_browser != null)
        {
            _browser.OnPick = target == null ? null : Pick;
            _browser.Notice = "";
        }
        UpdateTargetLine();
        _browser?.Render();
    }

    private void UpdateTargetLine()
    {
        if (_targetLabel == null) return;
        if (_target == null)
        {
            _targetLabel.text = $"<color={Theme.MutedBodyHex}>Reference (no form field selected)</color>";
            try { _stopBtn?.GameObject.SetActive(false); } catch { }
            return;
        }
        string what = _target.Format switch
        {
            BrowseFormat.Guid => "the GUID",
            BrowseFormat.ShortName => "the name without CHAR_",
            _ => "the internal name",
        };
        _targetLabel.text = $"Filling: <b>{_target.Label}</b> <color={Theme.MutedBodyHex}>(writes {what})</color>";
        try { _stopBtn?.GameObject.SetActive(true); } catch { }
    }

    private void Pick(CatalogRow row)
    {
        if (_target == null || row == null) return;
        if (!_target.IsAlive()) { TargetGone(); return; }
        if (!CatalogBrowse.Allows(_target.Kinds, row.Kind)) return;
        string value = CatalogBrowse.Value(row, _target.Format);
        try
        {
            _target.Input.Text = value;
            _browser.Notice = $"<color=#90EE90>Filled {_target.Label}: {value}</color>";
        }
        catch (Exception ex)
        {
            LogUtils.LogDebug($"[GameCatalog] fill failed: {ex.Message}");
            TargetGone();
            return;
        }
        _browser.Render();
    }

    private void TargetGone()
    {
        if (!_loggedClosedTarget)
        {
            _loggedClosedTarget = true;
            LogUtils.LogDebug("[GameCatalog] Browse target closed; dropped it.");
        }
        _target = null;
        _view.AllowedKinds = BrowseKinds.Both;
        if (_browser != null)
        {
            _browser.OnPick = null;
            _browser.Notice = "<color=#FFB070>That form was closed; pick a field again.</color>";
        }
        UpdateTargetLine();
        _browser?.Render();
    }

    private ButtonRef HeadButton(GameObject parent, string name, string label, string tooltip, Action onClick, int width)
    {
        var btn = UIFactory.CreateButton(parent, name, label);
        UIFactory.SetLayoutElement(btn.GameObject, minWidth: width, preferredWidth: width, flexibleWidth: 0,
            minHeight: Theme.ScaledOverlayHeight(24), preferredHeight: Theme.ScaledOverlayHeight(26), flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) { t.fontSize = Theme.ScaledOverlay(11); t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = false; }
        if (!string.IsNullOrEmpty(tooltip)) TooltipHover.Attach(btn.GameObject, tooltip);
        btn.OnClick = () => { try { onClick(); } catch (Exception ex) { LogUtils.LogError($"[GameCatalog] '{name}' threw: {ex}"); } };
        return btn;
    }

    internal override void Reset()
    {
        if (_ticker != null) Behaviors.CoreUpdateBehavior.Actions.Remove(_ticker);
        _target = null;
        if (_instance == this) _instance = null;
    }
}
