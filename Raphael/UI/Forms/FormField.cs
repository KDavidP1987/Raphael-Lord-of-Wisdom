using System;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Raphael.UI.Framework.CustomLib.Util;

namespace Raphael.UI.Forms;

// Form-field hierarchy used by FormBuilder. Each field is responsible for:
//   1. Building its input widget into a parent row (Build)
//   2. Returning its current value as a string for command-template substitution (GetValueString)
//   3. Optionally validating the value before submit (IsValid)
//
// Adding a new field type = subclass FormField and implement those three.
//
// All fields use Name for {token} substitution in the command template,
// Label for the row's text label, and Tooltip for hover help on the input.
public abstract class FormField
{
    public string Name    { get; set; }
    public string Label   { get; set; }
    public string Tooltip { get; set; }

    public abstract void   Build(GameObject row);
    public abstract string GetValueString();
    public virtual  bool   IsValid() => !string.IsNullOrEmpty(GetValueString());
}

// ---------------------------------------------------------------------------
// TextField - bare TMP_InputField wrapper.
// ---------------------------------------------------------------------------
public class TextField : FormField
{
    public string Placeholder { get; set; } = "";
    protected InputFieldRef Input;

    public TextField() { }
    public TextField(string name, string label, string placeholder = "", string tooltip = null)
    {
        Name = name; Label = label; Placeholder = placeholder; Tooltip = tooltip;
    }

    public override void Build(GameObject row)
    {
        Input = UIFactory.CreateInputField(row, $"Field_{Name}", Placeholder);
        UIFactory.SetLayoutElement(Input.GameObject,
            minWidth: 100, preferredWidth: 200, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        if (!string.IsNullOrEmpty(Tooltip))
            TooltipHover.Attach(Input.GameObject, Tooltip);
    }

    public override string GetValueString() => Input?.Text ?? "";
}

// ---------------------------------------------------------------------------
// IntField - text input that validates as integer in [Min, Max].
// ---------------------------------------------------------------------------
public class IntField : TextField
{
    public int Min { get; set; } = int.MinValue;
    public int Max { get; set; } = int.MaxValue;

    public IntField() { }
    public IntField(string name, string label, int min = int.MinValue, int max = int.MaxValue, string tooltip = null)
        : base(name, label, placeholder: "0", tooltip: tooltip)
    {
        Min = min; Max = max;
    }

    // An integer needs only a small box — don't stretch like a free-text/name field. Narrow, fixed
    // width (flex 0), and restrict input to digits so the field reads as "a number goes here".
    public override void Build(GameObject row)
    {
        Input = UIFactory.CreateInputField(row, $"Field_{Name}", Placeholder);
        UIFactory.SetLayoutElement(Input.GameObject,
            minWidth: 56, preferredWidth: 72, flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        // Integer keyboard + reject non-digits (a leading '-' is still allowed for negative Min ranges).
        try { Input.Component.contentType = TMP_InputField.ContentType.IntegerNumber; } catch { }
        if (!string.IsNullOrEmpty(Tooltip))
            TooltipHover.Attach(Input.GameObject, Tooltip);
    }

    public override bool IsValid()
    {
        if (Input == null || string.IsNullOrEmpty(Input.Text)) return false;
        if (!int.TryParse(Input.Text, out var v)) return false;
        return v >= Min && v <= Max;
    }
}

// ---------------------------------------------------------------------------
// PlayerNameField - free-text input wired to Services.PlayerNameCacheService.
// Autocomplete UI will be a later iteration; this stub already feeds names
// the player types into the cache so future autocomplete can suggest them.
// ---------------------------------------------------------------------------
public class PlayerNameField : TextField
{
    public PlayerNameField() : base() { Placeholder = "Player name"; }
    public PlayerNameField(string name, string label, string tooltip = null)
        : base(name, label, "Player name", tooltip) { }
}

// ---------------------------------------------------------------------------
// FormDropdownRegistry - non-generic registry of every active TMP_Dropdown
// created by an EnumField<T>. Lifted out of EnumField<T> because static fields
// on a generic class are per-T (so each EnumField<BloodType>, EnumField<Foo>,
// etc. would have its own list and the per-frame outside-click checker would
// only see dropdowns of one specific T).
// ---------------------------------------------------------------------------
public static class FormDropdownRegistry
{
    private static readonly System.Collections.Generic.List<TMP_Dropdown> _active = new();

    internal static void Register(TMP_Dropdown dd)
    {
        if (dd != null) _active.Add(dd);
    }

    // Reusable scratch list for EventSystem.RaycastAll. V Rising runs IL2CPP,
    // so the RaycastAll overload expects Il2CppSystem.Collections.Generic.List —
    // a managed System.Collections.Generic.List won't satisfy the bridge.
    // Reusing one list across frames also avoids per-click allocations.
    private static readonly Il2CppSystem.Collections.Generic.List<RaycastResult> _raycastHits
        = new Il2CppSystem.Collections.Generic.List<RaycastResult>();

    /// <summary>Per-frame: close any open dropdown when the user clicks outside it.
    /// Registered with CoreUpdateBehavior in Plugin.Load.
    ///
    /// 0.8.2: switched from rect-containment to EventSystem.RaycastAll + ancestry
    /// check. The old approach checked whether the click landed inside the
    /// "Dropdown List" RectTransform, which excluded the scrollbar — clicking
    /// the scrollbar handle counted as "outside" and dismissed the dropdown
    /// mid-scroll. The raycast approach asks "did the click hit anything
    /// rendered by this dropdown's hierarchy?", which catches the scrollbar,
    /// its handle, the items, and any future child widgets.</summary>
    public static void TickCloseOnOutsideClick()
    {
        if (!UnityEngine.Input.GetMouseButtonDown(0)) return;
        if (_active.Count == 0) return;

        var es = EventSystem.current;
        Vector2 mouse = UnityEngine.Input.mousePosition;

        // Raycast once; reuse the hit list across every open dropdown this frame.
        _raycastHits.Clear();
        if (es != null)
        {
            var ped = new PointerEventData(es) { position = mouse };
            es.RaycastAll(ped, _raycastHits);
        }

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var dd = _active[i];
            if (dd == null) { _active.RemoveAt(i); continue; }

            // The runtime-spawned options panel is a child of the dropdown
            // named "Dropdown List" while expanded; absent when collapsed.
            var listTransform = dd.transform.Find("Dropdown List");
            if (listTransform == null) continue;

            // Pass 1: ancestry check via raycast. If the user clicked anything
            // rendered as a descendant of the dropdown or its popup, leave it
            // open. This covers the scrollbar + handle + items naturally.
            bool hitDropdown = false;
            for (int h = 0; h < _raycastHits.Count; h++)
            {
                var go = _raycastHits[h].gameObject;
                if (go == null) continue;
                var t = go.transform;
                while (t != null)
                {
                    if (t == dd.transform || t == listTransform) { hitDropdown = true; break; }
                    t = t.parent;
                }
                if (hitDropdown) break;
            }
            if (hitDropdown) continue;

            // Pass 2: fall back to rect-containment in case the canvas isn't
            // raycast-registered (rare but possible during scene transitions).
            var listRt = listTransform as RectTransform;
            var ddRt   = dd.transform as RectTransform;
            bool overList = listRt != null && UnityEngine.RectTransformUtility.RectangleContainsScreenPoint(listRt, mouse, null);
            bool overDd   = ddRt   != null && UnityEngine.RectTransformUtility.RectangleContainsScreenPoint(ddRt,   mouse, null);
            if (!overList && !overDd) dd.Hide();
        }
    }
}

// ---------------------------------------------------------------------------
// EnumField<T> - TMP_Dropdown populated from Enum.GetNames(typeof(T)).
// Value sent to the server is the enum NAME (not the integer index), which is
// what most Bloodcraft / Kindred commands expect (e.g. ".prestige set X Experience 5").
// ---------------------------------------------------------------------------
public class EnumField<T> : FormField where T : struct, Enum
{
    public T Default { get; set; }
    protected TMP_Dropdown Dropdown;
    protected string[] OptionNames;

    public EnumField() { }
    public EnumField(string name, string label, T defaultValue = default, string tooltip = null)
    {
        Name = name; Label = label; Default = defaultValue; Tooltip = tooltip;
    }

    public override void Build(GameObject row)
    {
        OptionNames = Enum.GetNames(typeof(T));
        var go = UIFactory.CreateDropdown(row, $"Field_{Name}", out var dropdown,
            OptionNames.Length > 0 ? OptionNames[0] : "",
            itemFontSize: Theme.ScaledUI(13),
            onValueChanged: null,
            defaultOptions: OptionNames);
        Dropdown = dropdown;
        try { Dropdown.value = Convert.ToInt32(Default); }
        catch { Dropdown.value = 0; }

        // A dropdown holds a fixed set of short labels — it should NOT stretch to fill the row
        // like a free-text field. flexibleWidth:0 keeps it at its natural width (left-aligned, the
        // rest of the row empty) so numeric/enum forms read tidily. preferredWidth is wide enough
        // for the longest enum names (e.g. "PhysicalPower") without truncation.
        UIFactory.SetLayoutElement(go,
            minWidth: 120, preferredWidth: 168, flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        if (!string.IsNullOrEmpty(Tooltip))
            TooltipHover.Attach(go, Tooltip);

        FormDropdownRegistry.Register(dropdown);
    }

    public override string GetValueString()
    {
        if (Dropdown == null || OptionNames == null || OptionNames.Length == 0) return "";
        int idx = Dropdown.value;
        if (idx < 0 || idx >= OptionNames.Length) return OptionNames[0];
        return OptionNames[idx];
    }
}

// ---------------------------------------------------------------------------
// EnumIndexField<T> - same dropdown UX as EnumField<T>, but emits the 1-based
// dropdown index (e.g. "5") instead of the enum NAME (e.g. "PhysicalPower").
//
// Bloodcraft's `.wep cst <Weapon> <StatIndex>` and `.bl cst <Blood> <StatIndex>`
// commands use a 1-based integer for the stat picker (the command body does
// `--statType` to convert it back to 0-based). Wrapping the dropdown lets the
// user pick a NAMED stat while the form template still substitutes the integer
// the server expects. Pair with picker enums (WeaponBonusStat / BloodBonusStat)
// that mirror Bloodcraft's enum order so dropdown index 0+1=1 maps to enum 1.
// ---------------------------------------------------------------------------
public class EnumIndexField<T> : EnumField<T> where T : struct, Enum
{
    public EnumIndexField() : base() { }
    public EnumIndexField(string name, string label, T defaultValue = default, string tooltip = null)
        : base(name, label, defaultValue, tooltip) { }

    public override string GetValueString()
    {
        if (Dropdown == null || OptionNames == null || OptionNames.Length == 0) return "";
        return (Dropdown.value + 1).ToString();
    }
}

// ---------------------------------------------------------------------------
// BoxNameDropdownField - TMP_Dropdown populated from PlayerStateService.BoxList
// at Build time, refreshed whenever the box list changes. Used by the box-
// mutation forms (Delete box, Rename box's "current name" slot, Move familiar's
// destination slot) so the user picks from a list of EXISTING boxes instead
// of free-typing one and risking a typo.
//
// If the box list is empty when Build runs, we kick off a `.fam boxes` so the
// dropdown auto-fills as soon as the server replies. The subscription stays
// alive for the panel's lifetime — there's no field-disposal hook in the
// FormBuilder, so we null-check the dropdown GameObject before touching it
// (avoids UAF after the field is destroyed by a panel rebuild).
// ---------------------------------------------------------------------------
public class BoxNameDropdownField : FormField
{
    public string Placeholder { get; set; } = "(select a box)";
    /// <summary>0.11.0: when true, Build seeds an extra empty entry at the top so
    /// the user can keep the field blank — useful for optional box pickers.
    /// Default false; the box-mutation forms all REQUIRE a box.</summary>
    public bool AllowEmpty { get; set; }

    protected TMP_Dropdown Dropdown;
    private System.Action _boxListChangedHandler;

    // 0.65.6 LEAK FIX: the static BoxListChanged event rooted every field (and its dropdown wrapper) from every
    // rebuilt MainPanel until the next box-list reply — forever if none came. Live fields are tracked here and
    // detached by MainPanel.Reset() via DetachAll() (all construction sites are in MainPanel).
    private static readonly System.Collections.Generic.List<BoxNameDropdownField> _live = new();

    /// <summary>0.67.1: live-field count, logged by MainPanel's prewarm summary as a leak check.</summary>
    internal static int LiveCount => _live.Count;

    /// <summary>Idempotent: drop the BoxListChanged subscription and forget this field.</summary>
    public void Unsubscribe()
    {
        if (_boxListChangedHandler != null)
        {
            Services.PlayerStateService.BoxListChanged -= _boxListChangedHandler;
            _boxListChangedHandler = null;
        }
        _live.RemoveAll(f => ReferenceEquals(f, this));
    }

    /// <summary>Detach every live field (called from MainPanel.Reset before the panel is destroyed).</summary>
    internal static void DetachAll()
    {
        var snapshot = _live.ToArray();
        _live.Clear();
        foreach (var f in snapshot)
        {
            try { f.Unsubscribe(); f.Dropdown = null; } catch { }
        }
    }

    public BoxNameDropdownField() { }
    public BoxNameDropdownField(string name, string label, string tooltip = null, bool allowEmpty = false)
    {
        Name = name; Label = label; Tooltip = tooltip; AllowEmpty = allowEmpty;
    }

    public override void Build(GameObject row)
    {
        var initialOpts = BuildOptionList();
        var go = UIFactory.CreateDropdown(row, $"Field_{Name}", out var dropdown,
            initialOpts.Length > 0 ? initialOpts[0] : Placeholder,
            itemFontSize: Theme.ScaledUI(13),
            onValueChanged: null,
            defaultOptions: initialOpts);
        Dropdown = dropdown;
        Dropdown.value = 0;

        // Box names can be a touch longer than enum labels, so keep a slightly wider preferred width,
        // but still flexibleWidth:0 — a picker shouldn't stretch the full row width.
        UIFactory.SetLayoutElement(go,
            minWidth: 140, preferredWidth: 200, flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        if (!string.IsNullOrEmpty(Tooltip))
            TooltipHover.Attach(go, Tooltip);

        FormDropdownRegistry.Register(dropdown);

        // Listen for box-list updates so the dropdown stays in sync with
        // server-side changes (.fam ab / .fam db / .fam rb).
        Unsubscribe(); // re-Build of the same instance must not double-subscribe / double-register
        _boxListChangedHandler = OnBoxListChanged;
        Services.PlayerStateService.BoxListChanged += _boxListChangedHandler;
        _live.Add(this);
        // 0.67.1: MainPanel rebuilds single tab pages now — tie this subscription to the page being built so
        // retiring that page detaches it (DetachAll on Reset stays as the panel-wide safety drain).
        PageBuildContext.Register(Unsubscribe);

        // Kick the server for a fresh box list if we don't have one yet — keeps
        // the form usable on the first open before the user clicks Reload.
        if ((Services.PlayerStateService.BoxList == null || Services.PlayerStateService.BoxList.Count == 0)
            && Services.MessageService.IsInitialized)
        {
            Services.MessageService.EnqueueMessage(Services.MessageService.BCCOM_FAM_BOXES);
        }
    }

    private void OnBoxListChanged()
    {
        if (Dropdown == null) { Unsubscribe(); return; }
        // Defensive: the underlying GameObject may have been destroyed by a
        // panel rebuild while our subscription is still wired. Detach and bail.
        try { if (Dropdown.gameObject == null) { Unsubscribe(); Dropdown = null; return; } }
        catch { Unsubscribe(); Dropdown = null; return; }

        // Remember the previously-selected name so the new list keeps the
        // user's selection across a refresh.
        string previous = GetValueString();
        var opts = BuildOptionList();

        // Avoid TMP_Dropdown.AddOptions/ClearOptions because those have
        // IL2CPP-bridge overloads that prefer Il2CppSystem.Collections.Generic.List<string>
        // and the bridge is finicky on this Unity build. Mutate options
        // directly — matches the upstream UIFactory.CreateDropdown pattern.
        Dropdown.options.Clear();
        foreach (var s in opts)
            Dropdown.options.Add(new TMP_Dropdown.OptionData(s));

        int restoreIdx = 0;
        if (!string.IsNullOrEmpty(previous))
        {
            for (int i = 0; i < opts.Length; i++)
            {
                if (string.Equals(opts[i], previous, System.StringComparison.OrdinalIgnoreCase))
                {
                    restoreIdx = i; break;
                }
            }
        }
        Dropdown.value = restoreIdx;
        Dropdown.RefreshShownValue();
    }

    private string[] BuildOptionList()
    {
        var list = new System.Collections.Generic.List<string>();
        if (AllowEmpty) list.Add(""); // empty top entry the user can pick to leave the field blank
        var boxes = Services.PlayerStateService.BoxList;
        if (boxes != null)
        {
            foreach (var b in boxes)
                if (!string.IsNullOrWhiteSpace(b)) list.Add(b);
        }
        if (list.Count == 0) list.Add(Placeholder);
        return list.ToArray();
    }

    public override string GetValueString()
    {
        if (Dropdown == null) return "";
        int idx = Dropdown.value;
        var ilOpts = Dropdown.options;
        if (ilOpts == null || idx < 0 || idx >= ilOpts.Count) return "";
        var text = ilOpts[idx].text ?? "";
        if (string.Equals(text, Placeholder, System.StringComparison.Ordinal)) return "";
        return text;
    }

    public override bool IsValid()
    {
        if (AllowEmpty) return true;
        return !string.IsNullOrEmpty(GetValueString());
    }
}

// ---------------------------------------------------------------------------
// BoolField - toggle. Emits "true"/"false" for commands that take a boolean arg.
// ---------------------------------------------------------------------------
public class BoolField : FormField
{
    public bool Default { get; set; }
    /// <summary>If true, IsValid() requires the toggle to be checked. Use for
    /// confirmation gates on destructive commands (.fam r, etc.).</summary>
    public bool RequireTrue { get; set; }
    protected ToggleRef Toggle;

    public BoolField() { }
    public BoolField(string name, string label, bool defaultValue = false, string tooltip = null, bool requireTrue = false)
    {
        Name = name; Label = label; Default = defaultValue; Tooltip = tooltip; RequireTrue = requireTrue;
    }

    public override void Build(GameObject row)
    {
        Toggle = UIFactory.CreateToggle(row, $"Field_{Name}");
        Toggle.Toggle.isOn = Default;
        Toggle.Text.text = "";
        UIFactory.SetLayoutElement(Toggle.GameObject,
            minWidth: 60, preferredWidth: 80, flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        if (!string.IsNullOrEmpty(Tooltip))
            TooltipHover.Attach(Toggle.GameObject, Tooltip);
    }

    public override string GetValueString() => (Toggle?.Toggle != null && Toggle.Toggle.isOn) ? "true" : "false";
    public override bool   IsValid()
    {
        if (Toggle == null) return false;
        if (RequireTrue && (Toggle.Toggle == null || !Toggle.Toggle.isOn)) return false;
        return true;
    }
}
