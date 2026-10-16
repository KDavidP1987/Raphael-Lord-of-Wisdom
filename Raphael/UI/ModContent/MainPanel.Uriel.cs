using System;
using Raphael.Services.Uriel;
using Raphael.Utils;
using Raphael.UI.Framework.CustomLib.Util;
using Raphael.UI.Framework.UniverseLib.UI;
using Raphael.UI.Framework.UniverseLib.UI.Models;
using Raphael.UI.ModContent.Data;
using TMPro;
using UnityEngine;

namespace Raphael.UI.ModContent;

// 0.26: Uriel tab group (client UI for the server-side storage-share / public-prison / stair-swap /
// object-spawning mod). FOUNDATIONAL LAYER: detection handshake + the tab scaffolding + the
// instructional Quick Start / Help guides + a Settings tab, plus one player tab per sub-feature and
// the admin tabs. Many relays are already functional (they're single chat commands); the richer
// editors (permission/limit/cost panel, parsed stair-style picker, icon/3D object palette, confirm-
// gated admin tools) land in later passes per docs/V0.26_URIEL_NOTES.md.
//
// Object-spawn data comes from UrielState (populated by UrielProtocolService from the [URIEL:*] wire
// stream). Share / stair replies are still human text (chat) until Uriel promotes them to wire —
// the guides explain that, and the buttons relay the commands so the player reads the reply in chat.
public partial class MainPanel
{
    // ---- shared subscription guard for the Uriel tabs ----
    private bool _urielSubscribed;

    // Live UI handles (rebuilt on UrielState change events).
    private TextMeshProUGUI _urielStateReadout;        // Settings tab — connection / state line
    private TextMeshProUGUI _urielObjectsProgressLabel; // Object Spawning tab — collection progress
    private GameObject       _urielUnlockedListGo;      // Object Spawning tab — unlocked-prefab rows

    // Subscribe the Uriel tabs to UrielState change events. Idempotent; paired with UnsubscribeUriel
    // (called from MainPanel.Reset) so handlers don't leak across panel rebuilds.
    private void EnsureUrielSubscribed()
    {
        if (_urielSubscribed) return;
        UrielState.PresenceChanged += RefreshUrielStateReadout;
        UrielState.UnlockedChanged += RebuildUrielUnlockedRows;
        UrielState.UnlockedChanged += RefreshUrielStateReadout;
        UrielState.CatalogChanged  += RebuildUrielCatalogRows;
        UrielProtocolService.AvailabilityChanged += RefreshUrielStateReadout;
        UrielProtocolService.ScanNoteChanged += RebuildUrielCatalogRows;   // live scan status
        UrielProtocolService.ScanNoteChanged += RefreshUrielStateReadout;
        UrielBuildMode.ActiveChanged += RefreshUrielBuildToggle;
        _urielSubscribed = true;
    }

    private void UnsubscribeUriel()
    {
        if (!_urielSubscribed) return;
        UrielState.PresenceChanged -= RefreshUrielStateReadout;
        UrielState.UnlockedChanged -= RebuildUrielUnlockedRows;
        UrielState.UnlockedChanged -= RefreshUrielStateReadout;
        UrielState.CatalogChanged  -= RebuildUrielCatalogRows;
        UrielProtocolService.AvailabilityChanged -= RefreshUrielStateReadout;
        UrielProtocolService.ScanNoteChanged -= RebuildUrielCatalogRows;
        UrielProtocolService.ScanNoteChanged -= RefreshUrielStateReadout;
        UrielBuildMode.ActiveChanged -= RefreshUrielBuildToggle;
        UnsubscribeUrielEvents();
        _urielSubscribed = false;
    }

    // ---- small shared UI helpers (Uriel-local mirrors of the Beelz ones) ----

    private GameObject MakeUrielRow(GameObject parent, string name)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, name,
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 8, padding: new Vector4(2, 2, 2, 2));
        // 0.50: scale the row height with the UI font. AddUrielButton's buttons already
        // grow via Theme.ScaledHeight(30); without scaling the row, childControlHeight
        // clamps them back at Large+ text and they overlap the next row (storage-sharing
        // buttons were the reported case).
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        return row;
    }

    // Plain button (NOT gated on UrielState.Present) so Settings / re-detect work even when Uriel
    // isn't detected. Scales width+height with the font (Theme.ScaledWidth/ScaledHeight) so labels
    // never word-wrap vertically at Large+ text (the 0.24.8 lesson).
    private ButtonRef AddUrielButton(GameObject parent, string name, string label, string tooltip, Action onClick, int width = 130)
    {
        var btn = UIFactory.CreateButton(parent, name, label);
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: Theme.ScaledWidth(width), preferredWidth: Theme.ScaledWidth(width), flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) { t.fontSize = Theme.ScaledUI(12); t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Overflow; }
        if (!string.IsNullOrEmpty(tooltip)) TooltipHover.Attach(btn.GameObject, tooltip);
        btn.OnClick = onClick;
        return btn;
    }

    private void AddUrielBoolToggle(GameObject parent, string name, string label, bool initial, Action<bool> onChanged, string tooltip)
    {
        var row = MakeUrielRow(parent, name + "Row");
        var t = UIFactory.CreateToggle(row, name);
        UIFactory.SetLayoutElement(t.GameObject, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        t.Text.text = label;
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject, minWidth: 320, preferredWidth: 360, flexibleWidth: 1, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        t.Toggle.isOn = initial;
        if (!string.IsNullOrEmpty(tooltip)) TooltipHover.Attach(t.GameObject, tooltip);
        t.OnValueChanged += v => { try { onChanged?.Invoke(v); } catch (Exception ex) { LogUtils.LogError($"[Uriel] toggle '{name}' handler threw: {ex}"); } };
    }

    // A small note paragraph for "this is coming in a later pass" so the scaffold reads honestly.
    private void AddUrielSoonNote(GameObject parent, string text)
    {
        AddBodyText(parent, $"<i>{text}</i>");
    }

    // ============================ PLAYER: STORAGE SHARING ============================

    // Share-builder state (Storage tab).
    private string _urielSharePermission = "givetake";   // take | give | givetake
    private InputFieldRef _urielLimitHoursInput;
    private InputFieldRef _urielLimitStacksInput;
    private InputFieldRef _urielCostItemInput;
    private InputFieldRef _urielCostAmountInput;
    private InputFieldRef _urielFindItemInput;
    private TextMeshProUGUI _urielStorageStatus;
    private ButtonRef _urielPermBtn;

    private void BuildUrielStorageTab(GameObject page)
    {
        var intro = AddCard(page, "UrielStorageIntro");
        AddSectionHeading(intro, "Storage sharing");
        AddBodyText(intro,
            "Share a placed castle chest with everyone on the server. Stand next to the chest you want to " +
            "share, then use the buttons below — they always target the container <b>nearest you</b> (not " +
            "wherever your aim is pointing), so a UI click can't grab the wrong chest.");

        // --- Nearest-container status (client-side detection, no server round-trip) ---
        var statusCard = AddCard(page, "UrielStorageStatus");
        AddSectionHeading(statusCard, "Nearest container");
        _urielStorageStatus = AddBodyText(statusCard, "—", Theme.ScaledUI(12));   // multi-line nearby list — wrap + grow
        var sRow = MakeUrielRow(statusCard, "UrielStorageStatusRow");
        AddUrielButton(sRow, "UrielStorageScan", "Check nearby",
            "Scan for Uriel-shared containers near you (client-side — no server query). Shows what's already public around you.",
            RefreshUrielStorageStatus, 130);
        AddUrielButton(sRow, "UrielStorageInfo", "Server rules (nearest)",
            "Ask the server for the nearest container's full sharing rules — reply shows in chat (.uriel info nearest).",
            () => UrielClient.Info(), 180);
        ButtonRef ovBtn = null;
        ovBtn = AddUrielButton(sRow, "UrielStorageOverlayToggle", UrielSharedOverlayToggleLabel(),
            "Show/hide the draggable “Nearby Public Storage” overlay — a live on-screen list of Uriel-shared containers around you (client-side detection).",
            () => { Plugin.UIManager?.ToggleOverlay(PanelType.UrielSharedOverlay); SetUrielButtonText(ovBtn, UrielSharedOverlayToggleLabel()); }, 150);

        // --- Quick share ---
        var quick = AddCard(page, "UrielStorageQuick");
        AddSectionHeading(quick, "Quick share (nearest chest)");
        var qRow = MakeUrielRow(quick, "UrielStorageQuickRow");
        AddUrielButton(qRow, "UrielShareTake", "Share: take-only",
            "Share the nearest chest so anyone can TAKE from it (.uriel share nearest permission take).",
            () => UrielClient.Share("permission take"), 150);
        AddUrielButton(qRow, "UrielShareGive", "Share: give-only",
            "Share the nearest chest so anyone can DEPOSIT into it (.uriel share nearest permission give).",
            () => UrielClient.Share("permission give"), 150);
        AddUrielButton(qRow, "UrielShareBoth", "Share: give + take",
            "Share the nearest chest for both deposit and withdrawal (.uriel share nearest permission givetake).",
            () => UrielClient.Share("permission givetake"), 160);

        // --- Advanced builder: permission + limits + cost stacked into one command ---
        var build = AddCard(page, "UrielStorageBuild");
        AddSectionHeading(build, "Share with custom rules");
        AddBodyText(build,
            "Set any of these, then press <b>Share with these rules</b>. They're combined into one command " +
            "and applied to the nearest chest. Leave a field blank to skip it (no limit / no cost).");

        var permRow = MakeUrielRow(build, "UrielPermRow");
        AddUrielRowLabel(permRow, "Permission");
        _urielPermBtn = AddUrielButton(permRow, "UrielPermCycle", PermLabel(),
            "Cycle who may use the shared chest: Give + Take → Take-only → Give-only.",
            CycleUrielPermission, 170);

        _urielLimitStacksInput = AddUrielLabeledInput(build, "UrielLimitStacks", "Withdrawal limit (stacks/player)", "blank = none");
        _urielLimitHoursInput  = AddUrielLabeledInput(build, "UrielLimitHours",  "Limit window (hours)",            "blank = none");
        _urielCostItemInput    = AddUrielLabeledInput(build, "UrielCostItem",    "Cost item id (per stack)",         "blank = free");
        _urielCostAmountInput  = AddUrielLabeledInput(build, "UrielCostAmount",  "Cost amount",                      "blank = free");

        var findRow = MakeUrielRow(build, "UrielFindRow");
        _urielFindItemInput = UIFactory.CreateInputField(findRow, "UrielFindItemInput", "Find item id by name…");
        UIFactory.SetLayoutElement(_urielFindItemInput.GameObject, minWidth: 200, preferredWidth: 240, flexibleWidth: 1, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        AddUrielButton(findRow, "UrielFindItem", "Find item id",
            "Look up an item's numeric id by name — results (≤8) show in chat (.uriel finditem <name>). Copy the id into the cost field.",
            () => { var n = _urielFindItemInput?.Text?.Trim(); if (!string.IsNullOrEmpty(n)) UrielClient.FindItem(n); }, 130);

        var applyRow = MakeUrielRow(build, "UrielApplyRow");
        AddUrielButton(applyRow, "UrielShareCustom", "Share with these rules",
            "Combine the settings above into one command and apply to the nearest chest (.uriel share nearest permission … limitwithdrawal … limithours … cost …).",
            ApplyUrielCustomShare, 200);

        // --- Pay chest + my shares ---
        var manage = AddCard(page, "UrielStorageManage");
        AddSectionHeading(manage, "Pay chest & my shares");
        var mRow = MakeUrielRow(manage, "UrielManageRow");
        AddUrielButton(mRow, "UrielPayChest", "Set pay chest",
            "Designate the nearest PRIVATE general chest to receive cost payments from your shared containers (.uriel paychest nearest).",
            () => UrielClient.PayChest(), 130);
        AddUrielButton(mRow, "UrielUnshare", "Unshare nearest",
            "Revert the nearest shared chest back to private (.uriel unshare nearest).",
            () => UrielClient.Unshare(), 130);
        var mRow2 = MakeUrielRow(manage, "UrielManageRow2");
        AddUrielButton(mRow2, "UrielShared", "List my shares",
            "List every container you've shared, with its full rules, in chat (.uriel shared).",
            () => UrielClient.Shared(), 150);
        AddUrielButton(mRow2, "UrielUnshareMine", "Unshare ALL mine",
            "Bulk-revert every container you've shared or control (.uriel unsharemine).",
            () => UrielClient.UnshareMine(), 160);

        RefreshUrielStorageStatus();
    }

    private static string UrielSharedOverlayToggleLabel()
        => (Plugin.UIManager?.IsOverlayOpen(PanelType.UrielSharedOverlay) ?? false) ? "Hide nearby overlay" : "Show nearby overlay";

    private static string UrielSpawnOverlayToggleLabel()
        => (Plugin.UIManager?.IsOverlayOpen(PanelType.UrielObjectSpawnerOverlay) ?? false) ? "Hide spawn overlay" : "Show spawn overlay";

    private static void SetUrielButtonText(ButtonRef btn, string text)
    {
        if (btn?.Component == null) return;
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.text = text;
    }

    private string PermLabel() => "Permission: " + _urielSharePermission switch
    {
        "take" => "Take-only",
        "give" => "Give-only",
        _      => "Give + Take",
    };

    private void CycleUrielPermission()
    {
        _urielSharePermission = _urielSharePermission switch
        {
            "givetake" => "take",
            "take"     => "give",
            _          => "givetake",
        };
        if (_urielPermBtn != null)
        {
            var t = _urielPermBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null) t.text = PermLabel();
        }
    }

    private void ApplyUrielCustomShare()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("permission ").Append(_urielSharePermission);
        if (TryReadPositiveInt(_urielLimitStacksInput, out int stacks)) sb.Append(" limitwithdrawal ").Append(stacks);
        if (TryReadPositiveInt(_urielLimitHoursInput, out int hours))   sb.Append(" limithours ").Append(hours);
        if (TryReadPositiveInt(_urielCostItemInput, out int itemId) && TryReadPositiveInt(_urielCostAmountInput, out int amt))
            sb.Append(" cost ").Append(itemId).Append(' ').Append(amt);
        UrielClient.Share(sb.ToString());
    }

    private void RefreshUrielStorageStatus()
    {
        if (_urielStorageStatus == null) return;
        if (!SharedContainerDetector.Available)
        {
            _urielStorageStatus.text = "<color=#FFB070>Detection unavailable this session.</color> Use “Server rules (nearest)” instead.";
            return;
        }
        var found = SharedContainerDetector.ScanNearby(10f);
        if (found.Count == 0)
        {
            _urielStorageStatus.text = "<color=#A0A0A0>No Uriel-shared containers detected within 10m.</color> (Private chests don't show here — they aren't public.)";
            return;
        }
        var sb = new System.Text.StringBuilder();
        sb.Append($"<color=#90EE90>{found.Count} public container(s) nearby:</color>");
        int shown = 0;
        foreach (var c in found)
        {
            if (shown++ >= 6) { sb.Append($"\n…and {found.Count - 6} more."); break; }
            string kind = c.Kind == SharedContainerDetector.ContainerKind.PrisonCell ? "cell" : "chest";
            sb.Append($"\n• {c.Name} <color={Theme.MutedBodyHex}>({kind}, {c.Distance:0.#}m)</color>");
        }
        _urielStorageStatus.text = sb.ToString();
    }

    // Labeled single-line input row (caption left, field right). Returns the field for value reads.
    private InputFieldRef AddUrielLabeledInput(GameObject parent, string name, string caption, string placeholder)
    {
        var row = MakeUrielRow(parent, name + "Row");
        AddUrielRowLabel(row, caption);
        var input = UIFactory.CreateInputField(row, name, placeholder);
        UIFactory.SetLayoutElement(input.GameObject, minWidth: 110, preferredWidth: 140, flexibleWidth: 0, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        return input;
    }

    private void AddUrielRowLabel(GameObject row, string text)
    {
        var lbl = UIFactory.CreateLabel(row, "Label", $"<color={Theme.MutedBodyHex}>{text}</color>",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(lbl.GameObject, minWidth: 200, preferredWidth: 230, flexibleWidth: 1, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
    }

    private static bool TryReadPositiveInt(InputFieldRef field, out int value)
    {
        value = 0;
        var s = field?.Text?.Trim();
        return !string.IsNullOrEmpty(s) && int.TryParse(s, out value) && value > 0;
    }

    // ============================ PLAYER: PRISONS ============================

    private void BuildUrielPrisonTab(GameObject page)
    {
        var intro = AddCard(page, "UrielPrisonIntro");
        AddSectionHeading(intro, "Public prison cells");
        AddBodyText(intro,
            "Share a prison cell and everyone can feed and extract from it — those surfaces appear natively " +
            "in the cell once it's shared, so there's nothing extra to click for feed/extract. The buttons " +
            "below target the cell <b>nearest you</b>.");

        var actions = AddCard(page, "UrielPrisonActions");
        AddSectionHeading(actions, "Share / unshare the nearest cell");
        var r1 = MakeUrielRow(actions, "UrielPrisonRow1");
        AddUrielButton(r1, "UrielPrisonShare", "Share cell",
            "Share the nearest prison cell so everyone can feed/extract (.uriel share nearest — auto-detects cell vs chest).",
            () => UrielClient.Share(), 130);
        AddUrielButton(r1, "UrielPrisonUnshare", "Unshare cell",
            "Revert the nearest shared cell (.uriel unshare nearest).",
            () => UrielClient.Unshare(), 130);
        AddUrielButton(r1, "UrielPrisonInfo", "Show rules",
            "Show the nearest cell's sharing rules in chat (.uriel info nearest).",
            () => UrielClient.Info(), 120);

        var take = AddCard(page, "UrielPrisonTake");
        AddSectionHeading(take, "Take the prisoner");
        AddBodyText(take,
            "The native “subdue” button is hidden by the game on cells that aren't your castle's, so Raphael " +
            "relays it for you. <b>Have Dominating Presence ready</b> before you click — the charmed prisoner " +
            "follows you and you'll want to get it home before the charm runs out.");
        var r2 = MakeUrielRow(take, "UrielPrisonRow2");
        AddUrielButton(r2, "UrielTakePrisoner", "Take prisoner (nearest cell)",
            "Take the prisoner from the nearest shared cell — it becomes subdued and follows you (.uriel takeprisoner nearest). Ready Dominating Presence first.",
            () => UrielClient.TakePrisoner(), 230);

        AddSpacer(page, 6);
        var soon = AddCard(page, "UrielPrisonSoon");
        AddSectionHeading(soon, "Coming next");
        AddUrielSoonNote(soon,
            "A contextual on-screen “Take Prisoner” button that appears only while you're looking at a shared, " +
            "occupied cell — plus a charm-escort countdown so you know how long you have to get the prisoner " +
            "home — is a follow-up pass.");
    }

    // ============================ PLAYER: STAIRS ============================

    private void BuildUrielStairsTab(GameObject page)
    {
        var intro = AddCard(page, "UrielStairsIntro");
        AddSectionHeading(intro, "Stair restyle");
        AddBodyText(intro,
            "Restyle a placed staircase in-place to any of the six same-shape styles (cosmetic only — the " +
            "shape doesn't change). Some styles are DLC-locked; if you don't own one, Uriel replies with which " +
            $"DLC it needs. Stand next to the stairs, then click a style. {Mono(".uriel stairstyles")} lists the " +
            "current style and which you own.");

        var styles = AddCard(page, "UrielStairsStyles");
        AddSectionHeading(styles, "Swap to a style (nearest stairs)");
        var row = MakeUrielRow(styles, "UrielStairsStyleRow");
        foreach (var style in UrielClient.StairStyles)
        {
            string s = style;   // capture
            AddUrielButton(row, $"UrielStair_{s}", s,
                $"Restyle the nearest staircase to '{s}' (.uriel stairswap {s} nearest). DLC-locked styles reply with the needed DLC.",
                () => UrielClient.StairSwap(s), 95);
        }

        var actions = AddCard(page, "UrielStairsActions");
        var r2 = MakeUrielRow(actions, "UrielStairsRow2");
        AddUrielButton(r2, "UrielStairNext", "Next style",
            "Cycle the nearest staircase to the next style (.uriel stairswap next nearest).",
            () => UrielClient.StairNext(), 120);
        AddUrielButton(r2, "UrielStairStyles", "Show styles",
            "List the nearest stair's shape, current style and the styles you own, in chat (.uriel stairstyles nearest).",
            () => UrielClient.ShowStairStyles(), 120);
        AddUrielButton(r2, "UrielStairRemove", "Remove stairs",
            "Cleanly delete the nearest staircase, leaving connected floors/walls intact (.uriel removestairs nearest).",
            () => UrielClient.RemoveStairs(), 130);

        AddSpacer(page, 6);
        var soon = AddCard(page, "UrielStairsSoon");
        AddSectionHeading(soon, "Coming next");
        AddUrielSoonNote(soon,
            "A visual style picker that greys out the styles you don't own (read from `.uriel stairstyles`) " +
            "lands once Uriel ships the machine `api stairinfo` read.");

        // Admin-only stair cleanup (server enforces admin regardless of this gate).
        AddSpacer(page, 6);
        var adminContent = BeginAdminGate(page);
        AddSectionHeading(adminContent, "Admin: stair cleanup");
        AddBodyText(adminContent,
            "<color=#FF8080>Destructive.</color> <b>Stair purge</b> destroys ALL stair entities within 5m of you " +
            "(cleanup for invisible 'ghost' stairs from older builds) — <b>restart the server afterwards</b>. " +
            "<b>Refresh (diag)</b> dumps the aimed stair's state to the server log (experimental; doesn't change the swap).");
        var saRow = MakeUrielRow(adminContent, "UrielStairAdminRow");
        AddUrielConfirmButton(saRow, "UrielStairPurge", "Stair purge (5m)",
            "Destroy ALL stair entities within 5m of you (.uriel stairpurge). Restart the server afterwards.",
            () => UrielClient.SendUser(".uriel stairpurge"), 150, UrielWipe);
        AddUrielButton(saRow, "UrielStairRefresh", "Refresh (diag)",
            "Dump the aimed staircase's mega-static state to the server log; experimental live-refresh attempt (.uriel stairrefresh).",
            () => UrielClient.SendUser(".uriel stairrefresh"), 130);
    }

    // ============================ PLAYER: OBJECT SPAWNING ============================

    // Object Spawning tab state.
    private string _urielObjCategoryFilter = "";   // "" = all
    private string _urielUnlockedSearch = "";      // text filter (name OR id) for the unlocked list
    private string _urielCatalogSearch  = "";      // text filter (name OR id) for the catalog table
    private GameObject _urielObjFilterRowGo;
    private GameObject _urielCatalogListGo;
    private TextMeshProUGUI _urielCatalogStatus;
    private ButtonRef _urielBuildToggleBtn;
    private const int URIEL_PAGE_SIZE = 50;          // rows per page (the catalog can be ~2000 items)
    // Pagination + spawn-mode state.
    private int _urielUnlockedPage;
    private int _urielCatalogPage;
    private GameObject _urielUnlockedPagerGo;
    private GameObject _urielCatalogPagerGo;
    // Spawn lifecycle controls (shared by both object tabs). Durability cycles
    // indestructible→breakable→smashable; respawn is an independent toggle.
    private string _urielSpawnDurability = UrielClient.DUR_INDESTRUCTIBLE;
    private bool _urielSpawnRespawn;
    private readonly System.Collections.Generic.List<ButtonRef> _urielDurabilityBtns = new();
    private readonly System.Collections.Generic.List<ButtonRef> _urielRespawnBtns = new();

    private void BuildUrielObjectsTab(GameObject page)
    {
        EnsureUrielSubscribed();

        var intro = AddCard(page, "UrielObjectsIntro");
        AddSectionHeading(intro, "Object spawning & collection");
        AddBodyText(intro,
            "Collect world-object prefabs (some are unlocked by destroying them; others by the server's rules) " +
            "and spawn what you've unlocked straight into your plot. Your unlocked list grows as you play. " +
            "World objects have no built-in game icon, so they're grouped by <b>category</b> (use the filter).");

        // Building hotkeys at the TOP of the tab so they're reachable without scrolling past the object
        // table — they're the controls you reach for while actively building (user request 0.29).
        BuildUrielBuildHotkeysCard(page);

        var progress = AddCard(page, "UrielObjectsProgress");
        AddSectionHeading(progress, "Collection progress");
        // AddBodyText (not AddInfoLabel) so the multi-line collection-progress + scan-diagnostic text
        // WRAPS and grows instead of being clipped to a single line.
        _urielObjectsProgressLabel = AddBodyText(progress, "—");
        var pr = MakeUrielRow(progress, "UrielObjProgressRow");
        AddUrielButton(pr, "UrielObjRefresh", "Refresh my unlocked list",
            "Re-pull your unlocked prefabs from the server (.uriel api unlocked).",
            () => UrielProtocolService.StartUnlockedScan(), 190);
        AddUrielButton(pr, "UrielNotifyOn", "Notify on",
            "Turn ON the per-player “you unlocked …” messages (.uriel notify on).",
            () => UrielClient.Notify(true), 100);
        AddUrielButton(pr, "UrielNotifyOff", "Notify off",
            "Turn OFF the per-player unlock messages (.uriel notify off).",
            () => UrielClient.Notify(false), 100);

        AddUrielSpawnModeControls(progress, "Obj");

        // Despawn / rotate are SPAWNING actions (not catalog/informational), so they live here at the top
        // of the Object Spawning tab — one-click remove/rotate of the nearest placed object (user request
        // 0.29; Despawn was moved here from the Object Catalog tab).
        var manageRow = MakeUrielRow(progress, "UrielObjManageRow");
        AddUrielButton(manageRow, "UrielObjDespawn", "Despawn nearest",
            "Remove the spawned object nearest you (.uriel despawn) — a one-click alternative to the Remove build-hotkey.",
            () => UrielClient.Despawn(), 150);
        AddUrielButton(manageRow, "UrielObjRotate", "Rotate nearest",
            "Rotate the spawned object nearest you by 90° (.uriel rotate).",
            () => UrielClient.Rotate(), 130);
        // 0.20.0: flush-to-ground nudges. Each click shifts the nearest placed object up/down by 0.25m
        // so you can stop it floating or sinking (.uriel raise / .uriel lower). Own row — the manage row
        // above is already ~290px of a ~400px HorizontalGroup, so these can't share it without overflow.
        var nudgeRow = MakeUrielRow(progress, "UrielObjNudgeRow");
        AddUrielButton(nudgeRow, "UrielObjRaise", "Raise nearest",
            "Nudge the spawned object nearest you UP by 0.25m so it stops sinking into the ground (.uriel raise).",
            () => UrielClient.Raise(), 150);
        AddUrielButton(nudgeRow, "UrielObjLower", "Lower nearest",
            "Nudge the spawned object nearest you DOWN by 0.25m so it stops floating (.uriel lower).",
            () => UrielClient.Lower(), 150);

        // Quick-build palette overlay (0.29): a small draggable list you can use while walking around your
        // base. The main panel forces the camera aside (objects land off-center); the overlay doesn't.
        var ovRow = MakeUrielRow(progress, "UrielObjOverlayRow");
        ButtonRef spawnOvBtn = null;
        spawnOvBtn = AddUrielButton(ovRow, "UrielObjSpawnOverlayToggle", UrielSpawnOverlayToggleLabel(),
            "Show/hide the draggable spawn palette overlay — cycle a category with ← →, then spawn unlocked objects with one click while you build (no bulky main panel pointing your camera away).",
            () => { Plugin.UIManager?.ToggleOverlay(PanelType.UrielObjectSpawnerOverlay); SetUrielButtonText(spawnOvBtn, UrielSpawnOverlayToggleLabel()); }, 180);

        // Category filter (shared by the unlocked list + the catalog browse below).
        var filterCard = AddCard(page, "UrielObjectsFilter");
        AddSectionHeading(filterCard, "Filter by category");
        // A WRAPPING grid (not a single horizontal row) — there can be ~11 category chips (All + up to 10
        // categories), which would overflow the ~400px content area in a non-wrapping HorizontalGroup.
        // GridLayoutGroup (Flexible constraint) flows chips onto as many rows as needed.
        _urielObjFilterRowGo = UIFactory.CreateGridGroup(filterCard, "UrielObjFilterRow",
            cellSize: new Vector2(Theme.ScaledWidth(92), Theme.ScaledHeight(26)),
            spacing: new Vector2(4, 4), bgColor: new Color(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_urielObjFilterRowGo, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(92), preferredHeight: Theme.ScaledHeight(92), flexibleHeight: 0);
        RebuildUrielCategoryChips();

        var list = AddCard(page, "UrielObjectsList");
        AddSectionHeading(list, "My unlocked objects");
        AddUrielSearchRow(list, "UrielUnlockedSearch",
            v => { _urielUnlockedSearch = v; _urielUnlockedPage = 0; RebuildUrielUnlockedRows(); });
        _urielUnlockedPagerGo = UIFactory.CreateVerticalGroup(list, "UrielUnlockedPager",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 0, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_urielUnlockedPagerGo, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 0, flexibleHeight: 0);
        AddUrielColumnHeader(list);   // aligned Name / Category / ID column headers
        _urielUnlockedListGo = UIFactory.CreateVerticalGroup(list, "UrielUnlockedRows",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_urielUnlockedListGo, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 40, flexibleHeight: 0);

        var tip = AddCard(page, "UrielObjectsCatalogTip");
        AddBodyText(tip,
            "Looking for the full list of <b>everything that exists to collect</b> (with category + ID columns)? " +
            "It now lives on its own <b>Object Catalog</b> tab.");

        RefreshUrielStateReadout();
        RebuildUrielUnlockedRows();
    }

    // ============================ PLAYER: OBJECT CATALOG ============================

    private GameObject _urielCatFilterRowGo;   // catalog tab's category-filter chips

    private void BuildUrielObjectCatalogTab(GameObject page)
    {
        EnsureUrielSubscribed();

        var intro = AddCard(page, "UrielCatalogIntro");
        AddSectionHeading(intro, "Object catalog");
        AddBodyText(intro,
            "Every spawnable object on the server — what you've unlocked shows a <b>Spawn</b> button; the rest " +
            "are <b>locked</b> (still to collect). Loading is heavy the first time (it pages over chat), then " +
            "cached for instant reloads. World objects have no built-in game icon, so rows are grouped by " +
            "<b>category</b> and identified by <b>ID</b> (the spawn id).");

        var controls = AddCard(page, "UrielCatalogControls");
        _urielCatalogStatus = AddBodyText(controls, "—", Theme.ScaledUI(11));   // wraps + grows (long scan notes)
        var br = MakeUrielRow(controls, "UrielCatalogRow");
        AddUrielButton(br, "UrielCatalogLoad", "Load full catalog",
            "Load the full object catalog (cached after the first load). Heavy the first time — it pages over chat.",
            () => { if (!UrielState.CatalogLoaded) UrielProtocolService.StartCatalogScan(); else RebuildUrielCatalogRows(); }, 160);
        AddUrielButton(br, "UrielCatalogRescan", "Re-scan",
            "Force a fresh full-catalog scan from the server (ignores the cache).",
            () => UrielProtocolService.StartCatalogScan(), 90);
        // 0.29.1: the catalog is informational (browse what exists). Durability/Respawn are SPAWN settings
        // and now live only on the Object Spawning tab — the catalog's per-row Spawn buttons read that same
        // shared setting. (Removed AddUrielSpawnModeControls here per tester feedback.)

        var filterCard = AddCard(page, "UrielCatalogFilterCard");
        AddSectionHeading(filterCard, "Filter by category");
        _urielCatFilterRowGo = UIFactory.CreateGridGroup(filterCard, "UrielCatFilterRow",
            cellSize: new Vector2(Theme.ScaledWidth(92), Theme.ScaledHeight(26)),
            spacing: new Vector2(4, 4), bgColor: new Color(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_urielCatFilterRowGo, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: Theme.ScaledHeight(92), preferredHeight: Theme.ScaledHeight(92), flexibleHeight: 0);

        var tableCard = AddCard(page, "UrielCatalogTable");
        AddUrielSearchRow(tableCard, "UrielCatalogSearch",
            v => { _urielCatalogSearch = v; _urielCatalogPage = 0; RebuildUrielCatalogRows(); });
        _urielCatalogPagerGo = UIFactory.CreateVerticalGroup(tableCard, "UrielCatalogPager",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 0, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_urielCatalogPagerGo, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 0, flexibleHeight: 0);
        AddUrielColumnHeader(tableCard);   // aligned Name / Category / ID column headers
        _urielCatalogListGo = UIFactory.CreateVerticalGroup(tableCard, "UrielCatalogRows",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_urielCatalogListGo, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 20, flexibleHeight: 0);

        RebuildUrielCategoryChips();
        RebuildUrielCatalogRows();
    }

    // ---- columnar object table (shared by the unlocked list + the catalog) ----
    // Fixed scaled widths for Category / ID / action so the header cells line up with the data cells at
    // any UI font scale; Name takes the remaining width (flexible) in both header and rows.
    private static int ColCat    => Theme.ScaledWidth(84);
    private static int ColId     => Theme.ScaledWidth(56);
    private static int ColAction => Theme.ScaledWidth(84);

    private void AddUrielColumnHeader(GameObject parent)
    {
        var row = MakeUrielRow(parent, "UrielColHeader");
        AddUrielHeaderCell(row, "Name", Theme.ScaledWidth(110), 1);
        AddUrielHeaderCell(row, "Category", ColCat, 0);
        AddUrielHeaderCell(row, "ID", ColId, 0);
        AddUrielHeaderCell(row, "", ColAction, 0);   // action column (Spawn / locked)
    }

    private void AddUrielHeaderCell(GameObject row, string text, int width, int flex)
    {
        var l = UIFactory.CreateLabel(row, $"H_{(string.IsNullOrEmpty(text) ? "act" : text)}", text,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(l.GameObject, minWidth: width, preferredWidth: width, flexibleWidth: flex, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        l.TextMesh.fontStyle = FontStyles.Bold;
        l.TextMesh.color = new Color(0.90f, 0.72f, 0.36f);   // gold accent, matching section headings
        l.TextMesh.enableWordWrapping = false;
        l.TextMesh.overflowMode = TextOverflowModes.Overflow;
    }

    private void AddUrielTextCell(GameObject row, string richText, int width, int flex)
    {
        var l = UIFactory.CreateLabel(row, "Cell", richText, TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(l.GameObject, minWidth: width, preferredWidth: width, flexibleWidth: flex, minHeight: Theme.ScaledHeight(22), preferredHeight: Theme.ScaledHeight(24), flexibleHeight: 0);
        l.TextMesh.enableWordWrapping = false;
        l.TextMesh.overflowMode = TextOverflowModes.Ellipsis;
    }

    // One object row, columnar. `owned` rows get a Spawn button; un-owned (catalog only) show "locked".
    private void AddUrielObjectRow(GameObject parent, UrielObject o, bool owned)
    {
        var row = MakeUrielRow(parent, $"UrielObjRow_{o.Guid}");
        AddUrielTextCell(row, owned ? o.DisplayName : $"<color=#9A9A9A>{o.DisplayName}</color>", Theme.ScaledWidth(110), 1);
        AddUrielTextCell(row, $"<color={Theme.MutedBodyHex}>{o.Category}</color>", ColCat, 0);
        AddUrielTextCell(row, $"<color={Theme.MutedBodyHex}>{o.Guid}</color>", ColId, 0);
        if (owned)
            AddUrielButton(row, $"UrielSpawn_{o.Guid}", "Spawn",
                $"Spawn {o.DisplayName} at your location ({_urielSpawnDurability}{(_urielSpawnRespawn ? " + respawn" : "")}), then position it with the Move build-hotkey.",
                () => UrielClient.Spawn(o.Guid, _urielSpawnDurability, _urielSpawnRespawn), 84);
        else
            AddUrielTextCell(row, "<color=#888888>locked</color>", ColAction, 0);
    }

    // Durability cycle + respawn toggle, added to a card. Both control how the Spawn buttons place
    // objects (handoff §6 lifecycle flags); shared across both object tabs.
    private void AddUrielSpawnModeControls(GameObject card, string suffix)
    {
        var row = MakeUrielRow(card, $"UrielSpawnModeRow{suffix}");
        AddUrielRowLabel(row, "Spawn as");
        _urielDurabilityBtns.Add(AddUrielButton(row, $"UrielDurability{suffix}", UrielDurabilityLabel(),
            "Cycle the spawned object's durability: Indestructible (default) → Breakable (raid/decay can destroy it; you can't weapon-smash it) → Smashable (breakable AND you can destroy it; not castle-owned, decays, anyone in the territory can damage it). Applies to the Spawn buttons on both object tabs.",
            CycleUrielDurability, 200));
        _urielRespawnBtns.Add(AddUrielButton(row, $"UrielRespawn{suffix}", UrielRespawnLabel(),
            "Toggle auto-respawn: a destroyed object respawns until the castle is gone or you despawn it (applies to breakable/smashable objects).",
            ToggleUrielRespawn, 130));
    }

    private string UrielDurabilityLabel() => "Durability: " + _urielSpawnDurability switch
    {
        UrielClient.DUR_BREAKABLE => "Breakable",
        UrielClient.DUR_SMASHABLE => "Smashable",
        _                         => "Indestructible",
    };
    private string UrielRespawnLabel() => _urielSpawnRespawn ? "Respawn: ON" : "Respawn: off";

    private void CycleUrielDurability()
    {
        _urielSpawnDurability = _urielSpawnDurability switch
        {
            UrielClient.DUR_INDESTRUCTIBLE => UrielClient.DUR_BREAKABLE,
            UrielClient.DUR_BREAKABLE      => UrielClient.DUR_SMASHABLE,
            _                              => UrielClient.DUR_INDESTRUCTIBLE,
        };
        RefreshUrielSpawnModeButtons();
    }
    private void ToggleUrielRespawn() { _urielSpawnRespawn = !_urielSpawnRespawn; RefreshUrielSpawnModeButtons(); }

    private void RefreshUrielSpawnModeButtons()
    {
        foreach (var b in _urielDurabilityBtns) SetUrielButtonText(b, UrielDurabilityLabel());
        foreach (var b in _urielRespawnBtns) SetUrielButtonText(b, UrielRespawnLabel());
    }

    // Build-mode hotkeys: move / rotate / remove the nearest spawned object, gated behind a TEMPORARY
    // session-only toggle (resets OFF every login) so the keys never quietly stay armed during normal
    // play. The binds persist; the mode does not.
    private void BuildUrielBuildHotkeysCard(GameObject page)
    {
        AddSpacer(page, 6);
        var card = AddCard(page, "UrielBuildHotkeys");
        AddSectionHeading(card, "Building hotkeys (temporary)");
        AddBodyText(card,
            "<color=#FF6B6B><b>⚠ For active building only.</b></color> While <b>Build Mode</b> is ON, the keys " +
            "you bind below act on the spawned object <b>nearest you</b> — so <b>turn it OFF when you're done " +
            "building</b> or those keys will keep moving/removing objects during normal play. Build Mode is " +
            "always OFF when you log in; you turn it on per building session. Keys don't fire while you're " +
            "typing or while this panel is open with input-suppression.");

        var toggleRow = MakeUrielRow(card, "UrielBuildToggleRow");
        _urielBuildToggleBtn = AddUrielButton(toggleRow, "UrielBuildToggle", UrielBuildToggleLabel(),
            "Turn Build Mode on/off (session-only — resets off at next login). While ON, your bound keys move/rotate/remove the nearest spawned object.",
            () => UrielBuildMode.Toggle(), 200);
        RefreshUrielBuildToggle();

        AddUrielBuildKeyRow(card, "Move", "Move nearest object to my cursor (.uriel move).",
            () => Config.Settings.UrielBuildMoveKey, Config.Settings.SetUrielBuildMoveKey);
        AddUrielBuildKeyRow(card, "Rotate", "Rotate the nearest object (.uriel rotate).",
            () => Config.Settings.UrielBuildRotateKey, Config.Settings.SetUrielBuildRotateKey);
        AddUrielBuildKeyRow(card, "Remove", "Remove (despawn) the nearest object (.uriel despawn).",
            () => Config.Settings.UrielBuildRemoveKey, Config.Settings.SetUrielBuildRemoveKey);
    }

    private static string UrielBuildToggleLabel()
        => UrielBuildMode.Active ? "Build Mode: ON  ⚠ (click to turn off)" : "Build Mode: OFF";

    private void RefreshUrielBuildToggle()
    {
        if (_urielBuildToggleBtn?.Component == null) return;
        var t = _urielBuildToggleBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null)
        {
            t.text = UrielBuildToggleLabel();
            t.color = UrielBuildMode.Active ? new Color(1f, 0.42f, 0.42f) : Color.white;
        }
    }

    // One labeled build-key row: caption + current-key button (click → press a key; Escape cancels) + Clr.
    // Mirrors AddBeelzKeybindControls. Persists via the supplied get/set on Settings.
    private void AddUrielBuildKeyRow(GameObject parent, string label, string tooltip,
        System.Func<Config.BCHotkey> get, System.Action<Config.BCHotkey> set)
    {
        var row = MakeUrielRow(parent, $"UrielBuildKeyRow_{label}");
        AddUrielRowLabel(row, label);

        ButtonRef setBtn = null;
        System.Action listener = null;

        void Refresh()
        {
            var hk = get();
            SetUrielButtonText(setBtn, hk.IsEmpty ? "+ key" : hk.ToString());
        }

        setBtn = AddUrielButton(row, $"UrielBuildKeySet_{label}", "+ key", tooltip,
            () =>
            {
                if (listener != null) { Behaviors.CoreUpdateBehavior.Actions.Remove(listener); listener = null; }
                SetUrielButtonText(setBtn, "press…");
                listener = () =>
                {
                    try
                    {
                        if (Input.GetKeyDown(KeyCode.Escape))
                        { Behaviors.CoreUpdateBehavior.Actions.Remove(listener); listener = null; Refresh(); return; }
                        KeyCode pressed = KeyCode.None;
                        for (int k = (int)KeyCode.Backspace; k < (int)KeyCode.JoystickButton0; k++)
                        { var kc = (KeyCode)k; if (IsModifierKey(kc)) continue; if (Input.GetKeyDown(kc)) { pressed = kc; break; } }
                        if (pressed == KeyCode.None) return;
                        var mods = new System.Collections.Generic.List<KeyCode>();
                        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) mods.Add(KeyCode.LeftControl);
                        if (Input.GetKey(KeyCode.LeftAlt)     || Input.GetKey(KeyCode.RightAlt))     mods.Add(KeyCode.LeftAlt);
                        if (Input.GetKey(KeyCode.LeftShift)   || Input.GetKey(KeyCode.RightShift))   mods.Add(KeyCode.LeftShift);
                        set(new Config.BCHotkey { MainKey = pressed, Modifiers = mods.Count > 0 ? mods.ToArray() : null });
                        Behaviors.CoreUpdateBehavior.Actions.Remove(listener); listener = null; Refresh();
                    }
                    catch { if (listener != null) { Behaviors.CoreUpdateBehavior.Actions.Remove(listener); listener = null; } Refresh(); }
                };
                Behaviors.CoreUpdateBehavior.Actions.Add(listener);
            }, 90);

        AddUrielButton(row, $"UrielBuildKeyClr_{label}", "Clr", "Clear this build hotkey.",
            () => { if (listener != null) { Behaviors.CoreUpdateBehavior.Actions.Remove(listener); listener = null; } set(Config.BCHotkey.Empty); Refresh(); }, 44);

        Refresh();
    }

    // Build the category filter chips (shared filter applies to BOTH the unlocked list and the catalog;
    // each tab has its own chip row). Categories are whatever's present across unlocked ∪ catalog + "All".
    private void RebuildUrielCategoryChips()
    {
        var cats = new System.Collections.Generic.SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var o in UrielState.Unlocked) if (!string.IsNullOrEmpty(o.Category)) cats.Add(o.Category);
        foreach (var o in UrielState.Catalog)  if (!string.IsNullOrEmpty(o.Category)) cats.Add(o.Category);

        PopulateUrielChips(_urielObjFilterRowGo, cats);
        PopulateUrielChips(_urielCatFilterRowGo, cats);
    }

    private void PopulateUrielChips(GameObject row, System.Collections.Generic.SortedSet<string> cats)
    {
        if (row == null) return;
        ClearChildren(row);
        AddUrielCategoryChip(row, "All", "");
        foreach (var c in cats) AddUrielCategoryChip(row, c, c);
    }

    private void AddUrielCategoryChip(GameObject row, string label, string value)
    {
        bool active = string.Equals(_urielObjCategoryFilter, value, System.StringComparison.OrdinalIgnoreCase);
        AddUrielButton(row, $"UrielCat_{(string.IsNullOrEmpty(value) ? "all" : value)}",
            active ? $"[{label}]" : label,
            $"Show only {(string.IsNullOrEmpty(value) ? "all categories" : value)}.",
            () => { _urielObjCategoryFilter = value; _urielUnlockedPage = 0; _urielCatalogPage = 0; RebuildUrielCategoryChips(); RebuildUrielUnlockedRows(); RebuildUrielCatalogRows(); }, 84);
    }

    private bool UrielPassesFilter(UrielObject o)
        => string.IsNullOrEmpty(_urielObjCategoryFilter)
           || string.Equals(o.Category, _urielObjCategoryFilter, System.StringComparison.OrdinalIgnoreCase);

    // Text search: matches when the query (trimmed) is a substring of the object's display NAME or of its
    // numeric ID. Empty query matches everything. Combined (AND) with the category filter above.
    private bool UrielMatchesSearch(UrielObject o, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var q = query.Trim();
        return o.DisplayName.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0
            || o.Guid.ToString().IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // A labeled live-filter search row (caption + input + Clear). The input registers with InputFieldRef so
    // typing in it locks the game keyboard (no stray casts/moves). onChanged fires once per frame on edits.
    private InputFieldRef AddUrielSearchRow(GameObject parent, string name, System.Action<string> onChanged)
    {
        var row = MakeUrielRow(parent, name + "Row");
        AddUrielRowLabel(row, "Search (name or ID)");
        var input = UIFactory.CreateInputField(row, name, "type to filter…");
        UIFactory.SetLayoutElement(input.GameObject, minWidth: 140, preferredWidth: 200, flexibleWidth: 1, minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        input.OnValueChanged += v => { try { onChanged?.Invoke(v ?? ""); } catch (Exception ex) { LogUtils.LogError($"[Uriel] search '{name}' handler threw: {ex}"); } };
        AddUrielButton(row, name + "Clear", "Clear", "Clear the search filter.", () => { input.Text = ""; }, 60);
        return input;
    }

    private void RebuildUrielUnlockedRows()
    {
        if (_urielUnlockedListGo == null) return;
        RebuildUrielCategoryChips();   // keep chips in sync with newly-arrived data

        if (!UrielState.Present)
        {
            ClearChildren(_urielUnlockedListGo);
            if (_urielUnlockedPagerGo != null) ClearChildren(_urielUnlockedPagerGo);
            AddUrielListNote(_urielUnlockedListGo, "Uriel not detected on this server.");
            return;
        }

        var filtered = new System.Collections.Generic.List<UrielObject>();
        foreach (var o in UrielState.Unlocked) if (UrielPassesFilter(o) && UrielMatchesSearch(o, _urielUnlockedSearch)) filtered.Add(o);

        string empty = UrielState.Unlocked.Count == 0
            ? (UrielState.UnlockedLoaded ? "You haven't unlocked any objects yet." : "Press “Refresh my unlocked list” to load.")
            : (!string.IsNullOrWhiteSpace(_urielUnlockedSearch) ? "No unlocked objects match your search." : "No unlocked objects in this category.");

        RenderUrielPagedList(_urielUnlockedListGo, _urielUnlockedPagerGo, filtered,
            () => _urielUnlockedPage, p => _urielUnlockedPage = p,
            _ => true, RebuildUrielUnlockedRows, empty);
    }

    // Render one page of an object list into listGo + draw a Prev/Next pager into pagerGo. Filtering is
    // done by the caller; this paginates the already-filtered set (50/page) so a 2,000-item catalog
    // doesn't build thousands of rows at once.
    private void RenderUrielPagedList(GameObject listGo, GameObject pagerGo,
        System.Collections.Generic.List<UrielObject> filtered,
        System.Func<int> getPage, System.Action<int> setPage,
        System.Func<UrielObject, bool> isOwned, System.Action rebuild, string emptyMsg)
    {
        if (listGo != null) ClearChildren(listGo);
        if (pagerGo != null) ClearChildren(pagerGo);

        if (filtered.Count == 0) { if (listGo != null) AddUrielListNote(listGo, emptyMsg); return; }

        int pages = (filtered.Count + URIEL_PAGE_SIZE - 1) / URIEL_PAGE_SIZE;
        int page = getPage();
        if (page < 0) page = 0; else if (page >= pages) page = pages - 1;
        if (page != getPage()) setPage(page);

        if (pagerGo != null && pages > 1)
        {
            var prow = MakeUrielRow(pagerGo, "UrielPagerRow");
            int cur = page;   // capture
            if (cur > 0)
                AddUrielButton(prow, "UrielPagePrev", "← Prev", "Previous page.",
                    () => { setPage(cur - 1); rebuild(); }, 80);
            var lbl = UIFactory.CreateLabel(prow, "UrielPageLbl",
                $"<color={Theme.MutedBodyHex}>Page {cur + 1}/{pages}  ·  {filtered.Count} items</color>",
                TextAlignmentOptions.Center, color: null, fontSize: Theme.ScaledUI(12));
            UIFactory.SetLayoutElement(lbl.GameObject, minWidth: 120, preferredWidth: 200, flexibleWidth: 1, minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
            lbl.TextMesh.enableWordWrapping = false;
            lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
            if (cur < pages - 1)
                AddUrielButton(prow, "UrielPageNext", "Next →", "Next page.",
                    () => { setPage(cur + 1); rebuild(); }, 80);
        }

        int start = page * URIEL_PAGE_SIZE;
        int end = System.Math.Min(start + URIEL_PAGE_SIZE, filtered.Count);
        for (int i = start; i < end; i++)
            AddUrielObjectRow(listGo, filtered[i], isOwned(filtered[i]));
    }

    private void RebuildUrielCatalogRows()
    {
        // Status line.
        if (_urielCatalogStatus != null)
        {
            if (!string.IsNullOrEmpty(UrielProtocolService.ScanNote))
                _urielCatalogStatus.text = $"<color=#FFD75A>{UrielProtocolService.ScanNote}</color>";
            else if (UrielProtocolService.CatalogScanInProgress)
                _urielCatalogStatus.text = $"<color=#FFD75A>Scanning… {UrielProtocolService.CatalogScanLoaded} loaded</color>";
            else if (!UrielState.CatalogLoaded)
                _urielCatalogStatus.text = "<color=#A0A0A0>Not loaded — press “Load full catalog”.</color>";
            else
                _urielCatalogStatus.text = $"<color=#90EE90>{UrielState.Catalog.Count} objects</color>" +
                    (string.IsNullOrEmpty(UrielState.CatalogCacheInfo) ? "" : $"  <color={Theme.MutedBodyHex}>(cached — Re-scan to refresh)</color>");
        }

        if (_urielCatalogListGo == null) return;
        if (!UrielState.CatalogLoaded)
        {
            ClearChildren(_urielCatalogListGo);
            if (_urielCatalogPagerGo != null) ClearChildren(_urielCatalogPagerGo);
            return;
        }

        var unlocked = new System.Collections.Generic.HashSet<int>();
        foreach (var u in UrielState.Unlocked) unlocked.Add(u.Guid);

        var filtered = new System.Collections.Generic.List<UrielObject>();
        foreach (var o in UrielState.Catalog) if (UrielPassesFilter(o) && UrielMatchesSearch(o, _urielCatalogSearch)) filtered.Add(o);

        RenderUrielPagedList(_urielCatalogListGo, _urielCatalogPagerGo, filtered,
            () => _urielCatalogPage, p => _urielCatalogPage = p,
            o => unlocked.Contains(o.Guid), RebuildUrielCatalogRows,
            !string.IsNullOrWhiteSpace(_urielCatalogSearch) ? "No objects match your search." : "No objects in this category.");
    }

    private void AddUrielListNote(GameObject parent, string text)
    {
        var l = UIFactory.CreateLabel(parent, "UrielListNote", $"<color={Theme.MutedBodyHex}>{text}</color>",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(l.GameObject, minWidth: 360, preferredWidth: 400, flexibleWidth: 1, minHeight: 22, flexibleHeight: 0);
        l.TextMesh.enableWordWrapping = false;
        l.TextMesh.overflowMode = TextOverflowModes.Overflow;
    }

    // ============================ CLIENT: SETTINGS ============================

    private void BuildUrielSettingsTab(GameObject page)
    {
        EnsureUrielSubscribed();

        // --- Diagnostics ---
        var diagCard = AddCard(page, "UrielSettingsDiagCard");
        AddSectionHeading(diagCard, "Diagnostics");
        AddBodyText(diagCard,
            "Turn this on when testing or reporting an issue. It writes a verbose trace of every Uriel command " +
            $"sent and every {Mono("[URIEL:*]")} reply received to the BepInEx log ({Mono("LogOutput.log")}) — so " +
            "you can tell the mod author exactly what fired and what came back. Filter the log for " +
            $"{Mono("[Uriel]")}.");
        AddUrielBoolToggle(diagCard, "UrielDiagnosticsToggle",
            "Enable diagnostic details (verbose Uriel command/reply logging)",
            Config.Settings.UrielDiagnostics,
            v => { Config.Settings.SetUrielDiagnostics(v); LogUtils.LogInfo($"[Uriel] diagnostic details -> {(v ? "ON" : "OFF")}."); RefreshUrielStateReadout(); RebuildUrielUnlockedRows(); },
            "ON: Raphael logs a [Uriel][diag] command/reply trace to LogOutput.log and shows object IDs in the unlocked list. OFF: no extra logging. Takes effect immediately. Default OFF.");

        // --- Live connection / state readout ---
        AddSpacer(page, 6);
        var statusCard = AddCard(page, "UrielSettingsStatusCard");
        AddSectionHeading(statusCard, "Connection & state");
        _urielStateReadout = AddBodyText(statusCard, "—", Theme.ScaledUI(12));   // multi-line state — wrap + grow
        AddBodyText(statusCard,
            $"<color={Theme.MutedBodyHex}>Re-detect / re-hydrate lives at <b>Settings and Help → Connection</b> — " +
            "it's always reachable, so you can recover detection even when this Uriel group is hidden (it hides " +
            "when the server isn't running Uriel, which is exactly when you'd want to re-detect).</color>");

        // --- Availability (read-only here on purpose; mutating it from inside the group could hide this tab) ---
        AddSpacer(page, 6);
        var availCard = AddCard(page, "UrielSettingsAvailCard");
        AddSectionHeading(availCard, "Uriel tab group");
        AddBodyText(availCard,
            $"This tab group is currently set to <b>{UrielAvailabilityWord()}</b>. <b>Auto</b> shows the Uriel " +
            "tabs only when the server answers the handshake (default; most servers don't have Uriel). To force " +
            "them visible on a server where they aren't auto-detected, set <b>UrielAvailability = On</b> in " +
            "kdpen.Raphael.cfg (or use Force-enable on the greyed URIEL rail header). It's changed there, " +
            "not here, so you can't accidentally hide the group you're currently viewing.");

        RefreshUrielStateReadout();
    }

    private static string UrielAvailabilityWord() => Config.Settings.UrielAvailability switch
    {
        Config.Settings.ModAvailability.On  => "On",
        Config.Settings.ModAvailability.Off => "Off",
        _                                   => "Auto",
    };

    private void RefreshUrielStateReadout()
    {
        if (_urielObjectsProgressLabel != null)
        {
            if (UrielState.Present && UrielState.ObjectSpawnEnabled)
            {
                int denom = UrielState.DiscoverablePrefabs;
                int n     = UrielState.UnlockedCount;
                bool fullMode = string.Equals(UrielState.DiscoveryMode, "Full", StringComparison.OrdinalIgnoreCase);

                if (!UrielState.CollectionEnabled || fullMode)
                {
                    // Collection off / Full mode: every object is available to everyone, so a
                    // "collected / discoverable" fraction is meaningless (it read >100% because the unlocked
                    // count includes non-discoverable objects). Show the available count instead.
                    _urielObjectsProgressLabel.text =
                        $"<b>{n}</b> object(s) available to you" +
                        (fullMode ? "  ·  mode <b>Full</b> (everything unlocked)" : "  ·  collection <b>off</b>");
                }
                else
                {
                    // Discovery mode: progress is measured against the DISCOVERABLE set only. The real
                    // numerator is how many DISCOVERABLE objects the player has (disc=1 among their unlocked
                    // rows) — NOT the raw unlocked count `n`, which also counts non-discoverable (granted /
                    // always-available) objects and so can exceed the discoverable total (the old ">100%" bug).
                    int discColl;
                    if (UrielState.Unlocked.Count > 0)
                    {
                        int c = 0;
                        foreach (var o in UrielState.Unlocked) if (o.Discoverable) c++;
                        discColl = denom > 0 ? Math.Min(denom, c) : c;
                    }
                    else discColl = denom > 0 ? (int)Math.Round(UrielState.UnlockedPct / 100f * denom) : 0;

                    float pct = denom > 0 ? 100f * discColl / denom : 0f;
                    _urielObjectsProgressLabel.text =
                        (denom > 0 ? $"Discoverable found <b>{discColl}</b> / {denom}  ·  <b>{pct:0.#}%</b>"
                                   : $"Unlocked <b>{n}</b>") +
                        (n > discColl ? $"  ·  {n} unlocked total" : "") +
                        (string.IsNullOrEmpty(UrielState.DiscoveryMode) ? "" : $"  ·  mode {UrielState.DiscoveryMode}");
                }
            }
            else if (UrielState.Present)
                _urielObjectsProgressLabel.text = "Object collection is disabled on this server.";
            else
                _urielObjectsProgressLabel.text = "Uriel not detected on this server.";

            // Append the live scan diagnostic (waiting / no-reply / disabled / loading) when present.
            if (!string.IsNullOrEmpty(UrielProtocolService.ScanNote))
                _urielObjectsProgressLabel.text += $"\n<color=#FFD75A>{UrielProtocolService.ScanNote}</color>";
        }

        if (_urielStateReadout != null)
        {
            var sb = new System.Text.StringBuilder();
            if (UrielState.Present)
            {
                sb.Append($"<color=#90EE90>Uriel detected.</color>  API v{UrielState.ApiVersion}");
                if (!string.IsNullOrEmpty(UrielState.PluginVersion)) sb.Append($"  ·  plugin {UrielState.PluginVersion}");
                sb.Append($"\nObject spawning: <b>{(UrielState.ObjectSpawnEnabled ? "on" : "off")}</b>");
                if (UrielState.ObjectSpawnEnabled)
                    sb.Append($"   Collection: <b>{(UrielState.CollectionEnabled ? "on" : "off")}</b>   Unlocked: {UrielState.UnlockedCount}");
            }
            else
            {
                sb.Append(UrielProtocolService.DetectionGaveUp
                    ? "<color=#FFB070>Uriel not detected on this server.</color> If it IS installed, set the tab group to On (cfg), then use Connection → Re-detect."
                    : "<color=#FFD75A>Looking for Uriel…</color> If nothing appears shortly, the server doesn't have the mod.");
            }
            sb.Append($"\n\nDiagnostic details: <b>{(Config.Settings.UrielDiagnostics ? "ON" : "off")}</b>");
            if (!Config.Settings.UrielDiagnostics && Config.Settings.DiagnosticMode)
                sb.Append("  <color=#9FD0FF>(on via global Diagnostic mode)</color>");
            _urielStateReadout.text = sb.ToString();
        }
    }

    // ============================ ADMIN: shared helpers + state ============================

    // Two-click confirm (mirrors AddBeelzConfirmButton, minus the Beelz-presence gate so it works on a
    // Uriel-only server). First click relabels to "Confirm?"; a second click within 3s fires.
    private string _urielPendingConfirmKey;
    private float  _urielPendingConfirmDeadline = -1f;
    private const float URIEL_CONFIRM_WINDOW = 3f;
    private static readonly Color UrielDanger = new Color(0.55f, 0.18f, 0.18f, 1f);
    private static readonly Color UrielWipe   = new Color(0.65f, 0.12f, 0.12f, 1f);

    // Admin target state (autocomplete fields fill these; the guards below read them).
    private string _urielSharePlayer = "";
    private string _urielObjPlayer = "";
    private string _urielObjPrefab = "";          // chosen prefab GUID (or a raw typed name/guid)
    private string _urielGrantAllSubset = "all";  // all | destructible | indestructible
    private ButtonRef _urielGrantAllSubsetBtn;

    // Spawn-conditions (objcfg / objcfgglobal) admin fields — per-object/global player spawn limits.
    private string _urielCfgPrefab = "";
    private InputFieldRef _urielCfgMaxInput;
    private InputFieldRef _urielCfgCostAmtInput;
    private InputFieldRef _urielCfgCostItemInput;
    private InputFieldRef _urielCfgYOffsetInput;  // 0.20.0: per-object/global height correction (metres, may be negative)

    private ButtonRef AddUrielConfirmButton(GameObject parent, string name, string label, string tooltip,
                                            Action action, int width = 120, Color? color = null)
    {
        var btn = color.HasValue
            ? UIFactory.CreateButton(parent, name, label, color.Value)
            : UIFactory.CreateButton(parent, name, label);
        int w = Theme.ScaledWidth(width);
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: w, preferredWidth: w, flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) { t.fontSize = Theme.ScaledUI(11); t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Overflow; }
        if (!string.IsNullOrEmpty(tooltip)) TooltipHover.Attach(btn.GameObject, tooltip + "  (Two-click confirm — click again within 3s.)");
        string key = name;
        btn.OnClick = () =>
        {
            float now = Time.realtimeSinceStartup;
            var lbl = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
            bool armed = _urielPendingConfirmKey == key && now <= _urielPendingConfirmDeadline;
            if (armed) { _urielPendingConfirmKey = null; _urielPendingConfirmDeadline = -1f; if (lbl != null) lbl.text = label; try { action(); } catch (Exception ex) { LogUtils.LogError($"[Uriel] admin action '{name}' threw: {ex}"); } }
            else { _urielPendingConfirmKey = key; _urielPendingConfirmDeadline = now + URIEL_CONFIRM_WINDOW; if (lbl != null) lbl.text = "Confirm?"; }
        };
        return btn;
    }

    private static bool ReqUriel(string s, out string v) { v = (s ?? "").Trim(); return v.Length > 0; }

    // Require a non-negative integer from an input field (for objcfg max / cost amount).
    private static bool ReqUrielInt(InputFieldRef field, out int n)
    {
        n = 0;
        var t = (field?.Text ?? "").Trim();
        return t.Length > 0 && int.TryParse(t, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out n) && n >= 0;
    }

    // Require a signed float (metres) from an input field, for objcfg yoffset. Unlike ReqUrielInt this
    // allows negatives and decimals (an object can need to move down, e.g. -0.4). We hand the raw trimmed
    // token to Uriel (which also accepts "0"/"clear"), so the parse here is just a validity gate; `s` is
    // the invariant-culture-normalised string to send.
    private static bool ReqUrielFloat(InputFieldRef field, out string s)
    {
        s = (field?.Text ?? "").Trim();
        if (s.Length == 0) return false;
        if (s.Equals("clear", System.StringComparison.OrdinalIgnoreCase)) return true;  // pass-through: Uriel clears the offset
        return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
    }

    // Prefab autocomplete off the loaded catalog (name → GUID). Empty until the Object Catalog is loaded;
    // the field still accepts a typed name/GUID, which block/grant/revoke also accept.
    private System.Collections.Generic.List<(string, string)> UrielPrefabMatches(string typed)
    {
        var outl = new System.Collections.Generic.List<(string, string)>();
        try
        {
            foreach (var o in UrielState.Catalog)
            {
                if (BeelzContains(o.DisplayName, typed) || BeelzContains(o.Guid.ToString(), typed))
                    outl.Add(($"{o.DisplayName}  ({o.Guid})", o.Guid.ToString()));
                if (outl.Count >= 30) break;
            }
        }
        catch { }
        return outl;
    }

    // ============================ ADMIN: SHARING ============================

    private void BuildUrielAdminSharingTab(GameObject page)
    {
        var content = BeginAdminGate(page);
        AddBodyText(content,
            "Admin tools for storage/prison sharing. All require V Rising admin status — the server enforces it " +
            "regardless of this UI gate. Admins can also aim+share/unshare/modify ANY container from the Storage tab.");

        var reads = AddCard(content, "UrielAdminShareReads");
        AddSectionHeading(reads, "Inspect (read-only)");
        var r1 = MakeUrielRow(reads, "UrielAdminShareReadRow");
        AddUrielButton(r1, "UrielAdminSharedAll", "All shares",
            "List every shared container on the server, by owner, in chat (.uriel sharedall).",
            () => UrielClient.SendUser(".uriel sharedall"), 130);
        AddUrielButton(r1, "UrielAdminShareDebug", "Debug (aimed)",
            "Dump the AIMED container's live sharing state (team / castle link / prisoner) for diagnostics (.uriel sharedebug). Aim at a container first.",
            () => UrielClient.SendUser(".uriel sharedebug"), 130);

        var tgt = AddCard(content, "UrielAdminShareTarget");
        AddSectionHeading(tgt, "Target a player");
        AddBodyText(tgt, "Type a player name (autocompletes from online players), then choose an action.");
        AddBeelzSearchableField(tgt, "UrielSharePlayer", "Player name…", "", BeelzPlayerMatches, v => _urielSharePlayer = v);
        var tRow = MakeUrielRow(tgt, "UrielShareTgtRow");
        AddUrielButton(tRow, "UrielSharedAllPlayer", "Player's shares",
            "List that player's shared containers in chat (.uriel sharedall <player>).",
            () => { if (ReqUriel(_urielSharePlayer, out var n)) UrielClient.SendUser($".uriel sharedall {n}"); }, 150);
        AddUrielConfirmButton(tRow, "UrielUnsharePlayer", "Unshare player",
            "Revert ALL of that player's public containers to private (.uriel unshareplayer <player>).",
            () => { if (ReqUriel(_urielSharePlayer, out var n)) UrielClient.SendUser($".uriel unshareplayer {n}"); }, 140, UrielDanger);

        var danger = AddCard(content, "UrielAdminShareDanger");
        AddSectionHeading(danger, "Danger zone");
        AddBodyText(danger, "<color=#FF8080>Reverts shares server-wide. Two-click confirm.</color>");
        var dRow = MakeUrielRow(danger, "UrielShareDangerRow");
        AddUrielConfirmButton(dRow, "UrielUnshareAll", "Unshare ALL",
            "Revert EVERY public container to private and clear the registry (.uriel unshareall).",
            () => UrielClient.SendUser(".uriel unshareall"), 130, UrielWipe);
    }

    // ============================ ADMIN: OBJECTS ============================

    private void BuildUrielAdminObjectsTab(GameObject page)
    {
        var content = BeginAdminGate(page);
        AddBodyText(content,
            "Admin tools for object spawning / collection. All require V Rising admin status (server-enforced).");

        var reads = AddCard(content, "UrielAdminObjReads");
        AddSectionHeading(reads, "Inspect (read-only)");
        var r1 = MakeUrielRow(reads, "UrielAdminObjReadRow");
        AddUrielButton(r1, "UrielAdminBlocklist", "Blocklist",
            "List the blocked prefabs in chat (.uriel blocklist).",
            () => UrielClient.SendUser(".uriel blocklist"), 120);
        AddUrielButton(r1, "UrielAdminBossmap", "Boss map",
            "Show the boss→object unlock map in chat (.uriel bossmap list).",
            () => UrielClient.SendUser(".uriel bossmap list"), 120);

        // --- Block / unblock a prefab ---
        var blockCard = AddCard(content, "UrielAdminBlockCard");
        AddSectionHeading(blockCard, "Block / unblock a prefab");
        AddBodyText(blockCard,
            "Forbid or re-allow a prefab from being spawned / discovered / granted. Load the <b>Object Catalog</b> " +
            "first for name autocomplete, or type a GUID/name directly.");
        AddBeelzSearchableField(blockCard, "UrielBlockPrefab", "Prefab name or GUID…", "", UrielPrefabMatches, v => _urielObjPrefab = v);
        var bRow = MakeUrielRow(blockCard, "UrielBlockRow");
        AddUrielButton(bRow, "UrielBlock", "Block",
            "Block this prefab (.uriel block <guid|name>).",
            () => { if (ReqUriel(_urielObjPrefab, out var p)) UrielClient.SendUser($".uriel block {p}"); }, 110);
        AddUrielButton(bRow, "UrielUnblock", "Unblock",
            "Allow a previously-blocked prefab again (.uriel unblock <guid|name>).",
            () => { if (ReqUriel(_urielObjPrefab, out var p)) UrielClient.SendUser($".uriel unblock {p}"); }, 110);

        // --- Player unlocks (grant / revoke / grant-all / inspect) ---
        var unlockCard = AddCard(content, "UrielAdminUnlockCard");
        AddSectionHeading(unlockCard, "Player unlocks");
        AddBodyText(unlockCard, "Pick a player, then grant/revoke a specific prefab (uses the field above) or grant a whole set.");
        AddBeelzSearchableField(unlockCard, "UrielObjPlayer", "Player name…", "", BeelzPlayerMatches, v => _urielObjPlayer = v);
        var uRow = MakeUrielRow(unlockCard, "UrielUnlockRow");
        AddUrielButton(uRow, "UrielUnlocksPlayer", "View unlocks",
            "List that player's unlocked objects in chat (.uriel unlocks <player>).",
            () => { if (ReqUriel(_urielObjPlayer, out var n)) UrielClient.SendUser($".uriel unlocks {n}"); }, 120);
        AddUrielButton(uRow, "UrielGrant", "Grant",
            "Unlock the prefab in the field above for this player (.uriel grant <player> <prefab>).",
            () => { if (ReqUriel(_urielObjPlayer, out var n) && ReqUriel(_urielObjPrefab, out var p)) UrielClient.SendUser($".uriel grant {n} {p}"); }, 90);
        AddUrielConfirmButton(uRow, "UrielRevoke", "Revoke",
            "Remove the prefab in the field above from this player's unlocks (.uriel revoke <player> <prefab>).",
            () => { if (ReqUriel(_urielObjPlayer, out var n) && ReqUriel(_urielObjPrefab, out var p)) UrielClient.SendUser($".uriel revoke {n} {p}"); }, 90, UrielDanger);
        var gaRow = MakeUrielRow(unlockCard, "UrielGrantAllRow");
        _urielGrantAllSubsetBtn = AddUrielButton(gaRow, "UrielGrantAllSubset", UrielGrantAllSubsetLabel(),
            "Cycle which objects “Grant ALL” unlocks: all → destructible → indestructible.",
            CycleUrielGrantAllSubset, 180);
        AddUrielConfirmButton(gaRow, "UrielGrantAll", "Grant ALL",
            "Grant this player ALL placeable objects in the chosen subset (.uriel grantall <player> <subset>).",
            () => { if (ReqUriel(_urielObjPlayer, out var n)) UrielClient.SendUser($".uriel grantall {n} {_urielGrantAllSubset}"); }, 110, UrielDanger);

        // --- Spawn conditions for players (per-object + global; Uriel 0.18+, admins bypass) ---
        var cfgCard = AddCard(content, "UrielAdminCfgCard");
        AddSectionHeading(cfgCard, "Spawn conditions (players)");
        AddBodyText(cfgCard,
            "Limits applied to <b>non-admin players'</b> spawns (admins bypass): a <b>max per plot</b>, a <b>per-spawn " +
            "item cost</b>, and whether they may use <b>indestructible</b> / <b>respawn</b>. Set them <b>per-object</b> " +
            "or as a <b>global default</b>; a refused spawn replies in chat. Stored server-side. The <b>height offset " +
            "(yoffset)</b> below is a placement correction, not a restriction — it applies to <b>everyone including " +
            "admins</b>, auto-correcting how flush the object sits with the ground on every spawn and move.");
        var cfgListRow = MakeUrielRow(cfgCard, "UrielCfgListRow");
        AddUrielButton(cfgListRow, "UrielCfgList", "List all conditions",
            "Show the global default + every per-object override in chat (.uriel objcfglist).",
            () => UrielClient.SendUser(".uriel objcfglist"), 160);

        // ════ Per-object ════ — pick an object, then each row sets ONE field on it.
        AddSectionHeading(cfgCard, "▸ Per-object override");
        AddBodyText(cfgCard, "Choose an object, then set a field below. Each field's input sits directly above its button.");
        AddBeelzSearchableField(cfgCard, "UrielCfgPrefab", "Object name or GUID…", "", UrielPrefabMatches, v => _urielCfgPrefab = v);

        // Max per plot — input then button.
        _urielCfgMaxInput = AddUrielLabeledInput(cfgCard, "UrielCfgMax", "Max per plot (0 = unlimited)", "e.g. 3");
        var maxRow = MakeUrielRow(cfgCard, "UrielCfgMaxRow");
        AddUrielButton(maxRow, "UrielCfgSetMax", "Set max for this object",
            "Limit how many of this object a player may have per plot (.uriel objcfg <obj> max <n>).",
            () => { if (ReqUriel(_urielCfgPrefab, out var p) && ReqUrielInt(_urielCfgMaxInput, out var n)) UrielClient.SendUser($".uriel objcfg {p} max {n}"); }, 190);

        // Cost — two inputs then button.
        _urielCfgCostAmtInput  = AddUrielLabeledInput(cfgCard, "UrielCfgCostAmt", "Cost amount (0 = free)", "e.g. 5");
        _urielCfgCostItemInput = AddUrielLabeledInput(cfgCard, "UrielCfgCostItem", "Cost item GUID", "item GUID (.uriel finditem)");
        var costRow = MakeUrielRow(cfgCard, "UrielCfgCostRow");
        AddUrielButton(costRow, "UrielCfgSetCost", "Set cost for this object",
            "Charge this item per spawn of the object (.uriel objcfg <obj> cost <amount> <itemGuid>).",
            () => { if (ReqUriel(_urielCfgPrefab, out var p) && ReqUrielInt(_urielCfgCostAmtInput, out var amt) && ReqUriel(_urielCfgCostItemInput?.Text, out var item)) UrielClient.SendUser($".uriel objcfg {p} cost {amt} {item}"); }, 190);

        // Height offset (0.20.0) — flush-to-ground correction in metres. Positive raises, negative lowers;
        // "clear" (or 0) removes it. Unlike the limits above this applies to EVERYONE, admins included.
        _urielCfgYOffsetInput = AddUrielLabeledInput(cfgCard, "UrielCfgYOff", "Height offset — metres (+up / −down, or 'clear')", "e.g. 0.25 or -0.4");
        var yoffRow = MakeUrielRow(cfgCard, "UrielCfgYOffRow");
        AddUrielButton(yoffRow, "UrielCfgSetYOff", "Set height for this object",
            "Auto-correct how flush this object sits with the ground on every spawn/move (.uriel objcfg <obj> yoffset <metres>). Applies to everyone.",
            () => { if (ReqUriel(_urielCfgPrefab, out var p) && ReqUrielFloat(_urielCfgYOffsetInput, out var v)) UrielClient.SendUser($".uriel objcfg {p} yoffset {v}"); }, 200);

        // Permit flags — one row each, "allowed?" yes/no.
        AddBodyText(cfgCard, "May players spawn this object <b>indestructible</b>?");
        var indRow = MakeUrielRow(cfgCard, "UrielCfgIndRow");
        AddUrielButton(indRow, "UrielCfgIndAllow", "Yes",
            "Let players spawn this object indestructible (.uriel objcfg <obj> indestructible true).",
            () => { if (ReqUriel(_urielCfgPrefab, out var p)) UrielClient.SendUser($".uriel objcfg {p} indestructible true"); }, 90);
        AddUrielButton(indRow, "UrielCfgIndForbid", "No",
            "Forbid players from spawning this object indestructible (.uriel objcfg <obj> indestructible false).",
            () => { if (ReqUriel(_urielCfgPrefab, out var p)) UrielClient.SendUser($".uriel objcfg {p} indestructible false"); }, 90);
        AddBodyText(cfgCard, "May players use the <b>respawn</b> flag on it?");
        var resRow = MakeUrielRow(cfgCard, "UrielCfgResRow");
        AddUrielButton(resRow, "UrielCfgResAllow", "Yes",
            "Let players use the respawn flag on this object (.uriel objcfg <obj> respawn true).",
            () => { if (ReqUriel(_urielCfgPrefab, out var p)) UrielClient.SendUser($".uriel objcfg {p} respawn true"); }, 90);
        AddUrielButton(resRow, "UrielCfgResForbid", "No",
            "Forbid the respawn flag on this object (.uriel objcfg <obj> respawn false).",
            () => { if (ReqUriel(_urielCfgPrefab, out var p)) UrielClient.SendUser($".uriel objcfg {p} respawn false"); }, 90);

        var soRow = MakeUrielRow(cfgCard, "UrielCfgShowRow");
        AddUrielButton(soRow, "UrielCfgShow", "Show this object",
            "Show this object's current spawn conditions in chat (.uriel objcfg <obj> show).",
            () => { if (ReqUriel(_urielCfgPrefab, out var p)) UrielClient.SendUser($".uriel objcfg {p} show"); }, 140);
        AddUrielConfirmButton(soRow, "UrielCfgClear", "Clear this object",
            "Remove this object's overrides so it falls back to the global default (.uriel objcfg <obj> clear).",
            () => { if (ReqUriel(_urielCfgPrefab, out var p)) UrielClient.SendUser($".uriel objcfg {p} clear"); }, 150, UrielDanger);

        // ════ Global default ════ — same fields, applied to every object without its own override.
        AddSectionHeading(cfgCard, "▸ Global default (all objects)");
        AddBodyText(cfgCard, "Applies to every object that has no per-object override. <b>Max / cost / height use the input fields above.</b>");
        var gNumRow = MakeUrielRow(cfgCard, "UrielCfgGNumRow");
        AddUrielButton(gNumRow, "UrielCfgGlobMax", "Set global max",
            "Default max-per-plot for every object (.uriel objcfgglobal max <n>). Uses the Max field above.",
            () => { if (ReqUrielInt(_urielCfgMaxInput, out var n)) UrielClient.SendUser($".uriel objcfgglobal max {n}"); }, 130);
        AddUrielButton(gNumRow, "UrielCfgGlobCost", "Set global cost",
            "Default per-spawn cost for every object (.uriel objcfgglobal cost <amount> <itemGuid>). Uses the Cost fields above.",
            () => { if (ReqUrielInt(_urielCfgCostAmtInput, out var amt) && ReqUriel(_urielCfgCostItemInput?.Text, out var item)) UrielClient.SendUser($".uriel objcfgglobal cost {amt} {item}"); }, 130);
        // Own row — gNumRow (max + cost) already fills the ~400px group.
        var gYOffRow = MakeUrielRow(cfgCard, "UrielCfgGYOffRow");
        AddUrielButton(gYOffRow, "UrielCfgGlobYOff", "Set global height offset",
            "Default height offset for every object without its own (.uriel objcfgglobal yoffset <metres>). Uses the Height offset field above.",
            () => { if (ReqUrielFloat(_urielCfgYOffsetInput, out var v)) UrielClient.SendUser($".uriel objcfgglobal yoffset {v}"); }, 200);
        AddBodyText(cfgCard, "Default — may players spawn <b>indestructible</b>?");
        var gIndRow = MakeUrielRow(cfgCard, "UrielCfgGIndRow");
        AddUrielButton(gIndRow, "UrielCfgGlobIndAllow", "Yes",
            "Default-allow players to spawn indestructible (.uriel objcfgglobal indestructible true).",
            () => UrielClient.SendUser(".uriel objcfgglobal indestructible true"), 90);
        AddUrielButton(gIndRow, "UrielCfgGlobIndForbid", "No",
            "Default-forbid players from spawning indestructible (.uriel objcfgglobal indestructible false).",
            () => UrielClient.SendUser(".uriel objcfgglobal indestructible false"), 90);
        AddBodyText(cfgCard, "Default — may players use the <b>respawn</b> flag?");
        var gResRow = MakeUrielRow(cfgCard, "UrielCfgGResRow");
        AddUrielButton(gResRow, "UrielCfgGlobResAllow", "Yes",
            "Default-allow the respawn flag (.uriel objcfgglobal respawn true).",
            () => UrielClient.SendUser(".uriel objcfgglobal respawn true"), 90);
        AddUrielButton(gResRow, "UrielCfgGlobResForbid", "No",
            "Default-forbid the respawn flag (.uriel objcfgglobal respawn false).",
            () => UrielClient.SendUser(".uriel objcfgglobal respawn false"), 90);

        // --- Plot tools (act on the plot the admin is standing in) ---
        var plotCard = AddCard(content, "UrielAdminPlotCard");
        AddSectionHeading(plotCard, "Plot tools (your current plot)");
        AddBodyText(plotCard,
            "These act on the castle plot you're <b>standing in</b>. Force-despawn acts on the object you're " +
            "<b>aiming at</b>, ignoring ownership/records — run <b>Arm</b> (the server names the target in chat), " +
            "then <b>Confirm</b> within 30s.");
        var pRow = MakeUrielRow(plotCard, "UrielPlotRow1");
        AddUrielConfirmButton(pRow, "UrielPurgePlot", "Purge plot",
            "Remove ALL Uriel-spawned objects on the plot you're standing in (.uriel purgeplot).",
            () => UrielClient.SendUser(".uriel purgeplot"), 120, UrielDanger);
        AddUrielConfirmButton(pRow, "UrielForcePurgePlot", "Force-purge plot",
            "Force-remove EVERY Uriel-like indestructible object in this plot, even untracked ones; native build pieces + breakables are left (.uriel forcepurgeplot).",
            () => UrielClient.SendUser(".uriel forcepurgeplot"), 150, UrielWipe);
        var fRow = MakeUrielRow(plotCard, "UrielPlotRow2");
        AddUrielButton(fRow, "UrielForceDespawnArm", "Arm force-despawn",
            "Arm a record-ignoring removal of the object you're AIMING at — the server replies naming the prefab (.uriel forcedespawn).",
            () => UrielClient.SendUser(".uriel forcedespawn"), 170);
        AddUrielButton(fRow, "UrielForceDespawnConfirm", "Confirm",
            "Confirm the armed force-despawn within 30s of arming (.uriel forcedespawn confirm).",
            () => UrielClient.SendUser(".uriel forcedespawn confirm"), 100);

        // Server-wide orphan sweep (not plot-scoped) — removes tracked objects whose castle heart is gone.
        AddBodyText(plotCard,
            "<b>Server-wide:</b> remove Uriel objects whose castle heart is gone (castle destroyed / decayed / open world). " +
            "Only Uriel's own objects are touched; native objects are never affected.");
        var oRow = MakeUrielRow(plotCard, "UrielPlotRow3");
        AddUrielConfirmButton(oRow, "UrielPurgeOrphans", "Purge orphans (server-wide)",
            "Scan the WHOLE map and remove orphaned Uriel objects no longer governed by a living castle heart (.uriel purgeorphans).",
            () => UrielClient.SendUser(".uriel purgeorphans"), 200, UrielWipe);

        // --- Object spacing / overlap (SERVER CONFIG — no chat command; reference only) ---
        var spaceCard = AddCard(content, "UrielAdminSpacingCard");
        AddSectionHeading(spaceCard, "Object spacing / overlap (server config)");
        AddBodyText(spaceCard,
            "How close spawned objects may be placed is a <b>server config value</b> — Uriel exposes <b>no chat " +
            "command</b> for it, so Raphael can't change it live. Edit it in the Uriel mod's config on the " +
            $"<b>server</b> ({Mono("BepInEx/config/")} → the Uriel {Mono(".cfg")}, section {Mono("[ObjectSpawn]")}), " +
            "then restart / reload the server:");
        AddBodyText(spaceCard,
            $"• {Mono("OverlapMinDistance")} <color=#9FD0FF>(default 0.5)</color> — minimum spacing in <b>meters</b> " +
            "(center-to-center) Uriel keeps between a spawned/moved object and a nearby non-wall object. " +
            "<b>Lower it toward 0</b> to let players place décor closer together / tighter around furniture; " +
            "<b>0 disables</b> the proximity backstop. This is the “distance allowed between objects.”\n" +
            $"• {Mono("PreventOverlap")} <color=#9FD0FF>(default true)</color> — the master switch. When <b>true</b>, " +
            "objects can't be dropped inside walls / stations / props / other objects (floors excepted). Set " +
            "<b>false</b> to allow free stacking/overlap entirely (e.g. a candle directly on a table), at your own risk.");
        AddBodyText(spaceCard,
            $"<color=#FFB070>Tip:</color> a refused spawn replies in chat ({Mono("Can't place … it would overlap …")}); " +
            "if players hit that too often, lower OverlapMinDistance. <i>If you'd like to set this live from here " +
            "instead of the config file, that needs a new Uriel chat command — ask and it can be requested.</i>");
    }

    private string UrielGrantAllSubsetLabel() => $"Subset: {_urielGrantAllSubset}";

    private void CycleUrielGrantAllSubset()
    {
        _urielGrantAllSubset = _urielGrantAllSubset switch
        {
            "all"           => "destructible",
            "destructible"  => "indestructible",
            _               => "all",
        };
        if (_urielGrantAllSubsetBtn?.Component != null)
        {
            var t = _urielGrantAllSubsetBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null) t.text = UrielGrantAllSubsetLabel();
        }
    }

    // ============================ ADMIN: CONFIG ============================

    private void BuildUrielAdminConfigTab(GameObject page)
    {
        var content = BeginAdminGate(page);
        AddBodyText(content,
            "Each Uriel feature has a server admin kill-switch. When a feature is disabled, its commands reply " +
            "“disabled by the server admin”, and Raphael hides/disables the matching controls. The object-spawn " +
            "switches below are read live from the handshake; the storage / prison / stair switches aren't on " +
            "the wire yet (run the matching command to confirm).");

        var card = AddCard(content, "UrielAdminConfigCard");
        AddSectionHeading(card, "Object spawning (live from handshake)");
        AddBodyText(card, UrielState.Present
            ? $"Object spawning: <b>{(UrielState.ObjectSpawnEnabled ? "ENABLED" : "disabled")}</b>\n" +
              $"Collection master: <b>{(UrielState.CollectionEnabled ? "ENABLED" : "disabled")}</b>\n" +
              $"Discovery mode: <b>{(string.IsNullOrEmpty(UrielState.DiscoveryMode) ? "—" : UrielState.DiscoveryMode)}</b>" +
              (UrielState.DiscoveryChance > 0 ? $"  ·  chance {UrielState.DiscoveryChance}%" : "") + "\n" +
              $"Admin-only spawning: <b>{(UrielState.ObjectSpawnAdminOnly ? "yes" : "no")}</b>\n" +
              $"Prefabs: total {UrielState.TotalPrefabs} · discoverable {UrielState.DiscoverablePrefabs} · blocked {UrielState.BlockedPrefabs}"
            : "Uriel not detected on this server.");

        AddSpacer(content, 6);
        var soon = AddCard(content, "UrielAdminConfigSoon");
        AddSectionHeading(soon, "Coming next");
        AddUrielSoonNote(soon,
            "Live readouts + toggles for the storage (PublicStorage.Enabled), prison (PublicStorage.PrisonEnabled) " +
            "and stair-swap (StairSwap.Enabled) kill-switches arrive as Uriel exposes them on the wire.");
    }

    // ============================ HELP: URIEL QUICK START ============================

    private void BuildUrielQuickStartTab(GameObject page)
    {
        AddGuideSection(page, "Uriel — what it is",
            "Uriel (\"Lord of Hosts\") is a SERVER-SIDE mod, separate from Bloodcraft and Beelzebub — a server " +
            "can run any combination. When it's present it adds four things: <b>shared storage</b> (open a chest " +
            "to everyone, with optional permissions / limits / costs), <b>public prisons</b> (shared cells anyone " +
            "can feed/extract, plus a way to take the prisoner), <b>stair restyling</b> (change a placed " +
            "staircase's look in place), and <b>object spawning</b> (collect world-object prefabs and spawn what " +
            "you've unlocked). Raphael is the client UI; the Uriel tab group only appears when the server " +
            "actually has the mod.");

        AddGuideSection(page, "1. Share a chest",
            "Stand next to one of your placed castle chests and open <b>Uriel → Storage Sharing</b>. Pick " +
            "<b>Share: take-only / give-only / give + take</b> — that's it, the chest is now public for everyone. " +
            "<b>Show rules</b> reports the chest's current settings; <b>Unshare nearest</b> reverts it; <b>List my " +
            "shares</b> shows everything you've opened up. The buttons always act on the chest NEAREST you, so a " +
            "UI click never grabs the wrong one.");

        AddGuideSection(page, "2. Share a prison cell",
            "Same idea for prison cells, on the <b>Prisons</b> tab — once shared, feed/extract show up natively in " +
            "the cell for everyone. To take a shared prisoner for yourself, ready <b>Dominating Presence</b>, then " +
            "press <b>Take prisoner</b>; the prisoner is subdued and follows you home.");

        AddGuideSection(page, "3. Restyle stairs",
            "On the <b>Stairs</b> tab, stand by a staircase and click one of the six styles to swap its look " +
            "instantly (the shape stays the same). Some styles need a DLC; if you don't own one, Uriel tells you " +
            "which. <b>Show styles</b> lists the current style and the ones you own.");

        AddGuideSection(page, "4. Spawn objects you've unlocked",
            "Play to unlock world-object prefabs (some unlock by destroying them). Open <b>Object Spawning</b>, " +
            "press <b>Refresh my unlocked list</b>, and click <b>Spawn</b> next to anything you've unlocked to " +
            "place it on your plot. The progress line shows how much of the collectible set you've found.");

        AddGuideSection(page, "5. Run a castle event (alpha)",
            "If the server admin has turned Castle Events on, open <b>Castle Events</b>. Type a group name, walk to " +
            "where units should appear and press <b>Mob marker here</b> / <b>Boss marker here</b>; stand by a door and " +
            "press <b>Bind nearest door</b>. Build a squad on <b>Event Library</b> (or give each marker its own unit), " +
            "then press <b>Start</b>. The run panel shows the clock, groups cleared and bosses left.");

        AddGuideSection(page, "Don't see the Uriel tabs?",
            "They only appear once Raphael confirms Uriel on your server — which can lag a few seconds after switching " +
            "servers. If the group is greyed or missing:\n" +
            "• Click the greyed <b>URIEL</b> header — it opens a small panel with <b>Re-check now</b> (restarts " +
            "detection) and <b>Force-enable tabs</b> (shows them anyway).\n" +
            "• Or open <b>Settings and Help → Connection</b> → <b>Re-detect Uriel</b>.\n" +
            "• To make the tabs always show, set UrielAvailability = On in kdpen.Raphael.cfg.\n\n" +
            "See <b>Uriel Help</b> for the full command reference.");
    }

    // ============================ HELP: URIEL HELP (reference) ============================

    // Argument placeholders use (parentheses), NOT <angle brackets>, so TMP doesn't treat them as tags.
    private void BuildUrielModHelpTab(GameObject page)
    {
        AddGuideSection(page, "Uriel — the complete guide",
            "Uriel (\"Lord of Hosts\") is a server-side V Rising mod bundling four community features, each with " +
            "its own admin kill-switch: public storage, public prisons, stair restyling, and object spawning. " +
            "Raphael is the client UI; the two talk over chat (just like Raphael ↔ Bloodcraft / Beelzebub). " +
            "Exact limits and toggles are set by the server admin, so behaviour varies by server. You don't need " +
            "to type any of the commands below — the Uriel tabs send them for you — but they're listed so you " +
            "know exactly what each button does and can use chat directly.");

        AddGuideSection(page, "Targeting: why buttons say “nearest”",
            "Uriel commands act on a container/cell/staircase. By default they use whatever your aim is pointing " +
            "at, but clicking a UI panel leaves your aim pointing anywhere — so every Raphael button appends the " +
            "<b>nearest</b> token, which targets the closest qualifying object to YOU instead. After a “nearest” " +
            "action, the chat reply names the object that was affected so you can confirm it hit the right one.");

        AddCollapsibleHelpDetail(page, "Player — storage sharing",
            "• .uriel share (nearest) — share the nearest chest (auto-detects chest vs prison cell).\n" +
            "• .uriel share (nearest) permission (take|give|givetake) — set who can take/deposit.\n" +
            "• .uriel share (nearest) limithours (h) — withdrawal cooldown per player.\n" +
            "• .uriel share (nearest) limitwithdrawal (n) — max stacks per player.\n" +
            "• .uriel share (nearest) cost (itemId) (amount) — charge per withdrawal (0 0 = free). Modifiers STACK " +
            "in one command, any order.\n" +
            "• .uriel unshare (nearest) — revert one container.   • .uriel unsharemine — revert everything you control.\n" +
            "• .uriel shared — list your shares with full rules.   • .uriel info (nearest) — the nearest container's rules.\n" +
            "• .uriel paychest (nearest) — designate a private chest to receive cost payments.\n" +
            "• .uriel finditem (name) — look up an item's numeric id for cost rules (≤8 results).");

        AddCollapsibleHelpDetail(page, "Player — prisons",
            "• .uriel share (nearest) — share a prison cell (feed/extract then appear natively for everyone).\n" +
            "• .uriel takeprisoner (nearest) — take the prisoner from the nearest shared cell; it's subdued and " +
            "follows you. Ready Dominating Presence first.\n" +
            "• .uriel unshare (nearest) / .uriel info (nearest) — same as storage.");

        AddCollapsibleHelpDetail(page, "Player — stairs",
            "• .uriel stairswap (style) (nearest) — restyle the nearest staircase. Styles: stone1, stone2, stone3, " +
            "gloomrot, projectk, strongblade (some DLC-locked).\n" +
            "• .uriel stairswap next (nearest) — cycle to the next style.\n" +
            "• .uriel stairstyles (nearest) — the stair's shape, current style, and the styles you own.\n" +
            "• .uriel removestairs (nearest) — cleanly delete the staircase (leaves floors/walls intact).");

        AddCollapsibleHelpDetail(page, "Player — object spawning",
            "• .uriel spawn (guid) — spawn an unlocked prefab onto your plot (Raphael's Spawn buttons send this).\n" +
            "• .uriel catalog (page) / .uriel unlocks — the full prefab list / your unlocked set in chat.\n" +
            "• .uriel notify (on|off) — per-player unlock messages.\n" +
            "• .uriel despawn / move / rotate / spawninfo / spawnlist — manage objects you've placed (own plot).\n" +
            "• .uriel raise (metres) / .uriel lower (metres) — nudge the nearest object you placed up/down so it sits " +
            "flush with the ground instead of floating or sinking (default 0.25m; Raphael's Raise/Lower buttons send this).\n" +
            "• Build hotkeys — Object Spawning → Building hotkeys lets you bind keys to move / rotate / remove " +
            "the nearest spawned object while a TEMPORARY Build Mode is on (resets off every login).\n" +
            "• Machine API (what Raphael calls): .uriel api version | catalog (page) | unlocked (page) — return " +
            "[URIEL:*] lines Raphael parses into the Object Spawning tab. Turn on Uriel → Settings → Diagnostics to " +
            "see this traffic in the log.");

        AddCollapsibleHelpDetail(page, "Castle Events (alpha, off by default)",
            "Everything acts on the castle plot you stand in: your own castle (clan counts), any castle for admins. " +
            "Needs CastleEvents.Enabled; players also need CastleEvents.PlayerEvents.\n" +
            "• .uriel event marker (group) (mob|boss) [unit|-] [count] [level] [contain|-] — a marker at your feet; the " +
            "group's floor is the one you stand on. Contain: plot, room, storey, a leash in metres (3-60), or room,15.\n" +
            "• .uriel event door / undoor (group) — the castle door within 6 m stays shut until the group falls.\n" +
            "• .uriel event squad (group) (squad|-) · remove (#id|group) · list.\n" +
            "• .uriel event start [template|-] [options] · stop · status · history.\n" +
            "• Options, one comma-separated word: easy|normal|hard|brutal, level=N, level+N, auto+N, hp*X, dmg*X, " +
            "speed*X, aspd*X (0.1-10), limit=15m, faction=keep.\n" +
            "• Library: .uriel squad create|add|extend|remove|weight|show|list|delete, .uriel floor create|assign|show|" +
            "list|delete, .uriel template create|floor|options|show|list|delete.\n" +
            "• Admin: .uriel unit spawn|list|kill|despawn|clear|purge|status|rooms|probe|probemap|door.\n" +
            "• Chat cuts a line at about 120 characters; Raphael refuses to send a longer one. Use short unit names " +
            "(Foreman_VBlood works for CHAR_Bandit_Foreman_VBlood) and add long lists with extend.");

        AddGuideSection(page, "Admin commands",
            "All admin commands require V Rising admin status (the server enforces it — a non-admin just gets a " +
            "rejection). Raphael surfaces the safe read-only ones on the Uriel Admin tabs now; targeted/destructive " +
            "ones (with confirm gating) follow in a later pass.");

        AddCollapsibleHelpDetail(page, "Admin — sharing",
            "• .uriel sharedall (name|steamId) — list all shares (optionally for one player).\n" +
            "• .uriel unshareplayer (name|steamId) — revert one player's shares.\n" +
            "• .uriel unshareall — revert every share on the server.\n" +
            "• .uriel sharedebug — full state dump. Admins may also aim+share/unshare/modify ANY container.");

        AddCollapsibleHelpDetail(page, "Admin — objects",
            "• .uriel block / unblock / blocklist — manage the spawn blocklist.\n" +
            "• .uriel grant / revoke / grantall — manage a player's unlocked prefabs.\n" +
            "• .uriel unlocks (player) — inspect a player's unlocked set.\n" +
            "• .uriel objcfg (obj) max|cost|indestructible|respawn|yoffset|clear|show — per-object spawn limits for players.\n" +
            "   yoffset (metres, +up/−down) is a flush-to-ground height correction that applies to EVERYONE, admins included.\n" +
            "• .uriel objcfgglobal (field) — the global default spawn limit / height offset.   • .uriel objcfglist — list all conditions.\n" +
            "• .uriel bossmap — boss→prefab mapping.   • .uriel purgeplot — clear a plot.\n" +
            "• .uriel purgeorphans — server-wide: remove objects whose castle heart is gone.\n" +
            "• .uriel forcedespawn (confirm) / .uriel forcepurgeplot — record-ignoring recovery for untracked " +
            "objects (arm → names the prefab → confirm within 30s).");

        AddGuideSection(page, "Kill-switches",
            "Each feature can be turned off by the server admin: PublicStorage.Enabled (storage), " +
            "PublicStorage.PrisonEnabled (prisons), StairSwap.Enabled (stairs), and the object-collection master " +
            "switch. A disabled feature replies “disabled by the server admin”, and Raphael hides its controls.");
    }
}
