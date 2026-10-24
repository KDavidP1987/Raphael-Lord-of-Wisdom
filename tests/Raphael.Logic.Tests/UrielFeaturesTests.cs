using System.Text.RegularExpressions;
using Raphael.Services.Uriel;

// 0.73.0: Uriel 0.22.0 feature switches and admin kill switches (PLAN-URIEL022.md). Reply texts are copied verbatim
// from Uriel 0bad5fe: KillSwitchCommands.cs (features / killswitch / purgeall), KillSwitch.RestoreVanilla (result
// lines), EventCommands.cs:93 (stopall), UnitSpawnService.cs:650-655 (unit purge) and the refusal lines.
internal static partial class Program
{
    private static void UrielFeaturesTests()
    {
        UrielParse();
        UrielRefusals();
        UrielGates();
        UrielReplies();
        UrielFlowArm();
        UrielFlowConfirm();
        UrielFlowGather();
        UrielFlowOwnership();
        UrielSourceGuards();
    }

    private static string UrielFeaturesLine(bool masterOff = false, string stairs = "on", string objects = "on",
        bool objAdmin = false, string events = "on", bool evAdmin = false, string doors = "on",
        string chests = "on", string cells = "on", string costs = "on")
        => "Uriel features:" + (masterOff ? " Master switch [General] Enabled is OFF: everything below is off." : "")
         + $" Stair swap {stairs}. Object spawning {objects}{(objAdmin ? " (admins only)" : "")}."
         + $" NPC units and castle events {events}{(evAdmin ? " (admins only)" : "")}, event doors {doors}."
         + $" Public chests {chests}, prison cells {cells}, paid shares {costs}."
         + " Changes to the config take effect at the next server start.";

    // ------------------------------------------------------------------ `.uriel features`
    private static void UrielParse()
    {
        var all = UrielFeatures.Parse(UrielFeaturesLine());
        Check(all != null, "features: all on parses");
        if (all != null)
        {
            foreach (var f in UrielFeatures.All) Eq(all.Values[f], UrielSwitch.On, $"features: all on: {f}");
            Check(!all.ObjectsAdminOnly && !all.EventsAdminOnly, "features: all on: no admin-only");
        }

        // Uriel's defaults: events off, objects admins only.
        var def = UrielFeatures.Parse(UrielFeaturesLine(objAdmin: true, events: "OFF"));
        Check(def != null, "features: defaults parse");
        if (def != null)
        {
            Eq(def.Values[UrielFeature.Events], UrielSwitch.Off, "features: defaults: events off");
            Eq(def.Values[UrielFeature.Objects], UrielSwitch.On, "features: defaults: objects on");
            Check(def.ObjectsAdminOnly, "features: defaults: objects admins only");
            Check(!def.EventsAdminOnly, "features: defaults: events not admin-only");
        }

        var master = UrielFeatures.Parse(UrielFeaturesLine(masterOff: true));
        Check(master != null && master.Values[UrielFeature.Master] == UrielSwitch.Off, "features: master off parses");

        var evAdmin = UrielFeatures.Parse(UrielFeaturesLine(evAdmin: true));
        Check(evAdmin != null && evAdmin.EventsAdminOnly && !evAdmin.ObjectsAdminOnly, "features: events admins only");

        var mixed = UrielFeatures.Parse(UrielFeaturesLine(stairs: "OFF", doors: "OFF", cells: "OFF", costs: "OFF"));
        Check(mixed != null, "features: mixed parses");
        if (mixed != null)
        {
            Eq(mixed.Values[UrielFeature.Stairs], UrielSwitch.Off, "features: mixed stairs");
            Eq(mixed.Values[UrielFeature.Doors], UrielSwitch.Off, "features: mixed doors");
            Eq(mixed.Values[UrielFeature.Chests], UrielSwitch.On, "features: mixed chests");
            Eq(mixed.Values[UrielFeature.Cells], UrielSwitch.Off, "features: mixed cells");
            Eq(mixed.Values[UrielFeature.Costs], UrielSwitch.Off, "features: mixed costs");
        }

        // No guessing: anything else is null.
        Check(UrielFeatures.Parse(UrielFeaturesLine().Replace(", paid shares on", "")) == null, "features: a missing token is null");
        Check(UrielFeatures.Parse(UrielFeaturesLine().Replace(" Changes to the config take effect at the next server start.", "")) == null, "features: a missing tail is null");
        Check(UrielFeatures.Parse("<color=#fff>" + UrielFeaturesLine() + "</color>") == null, "features: markup is the caller's job");
        Check(UrielFeatures.Parse("[Bloodcraft] Uriel features: Stair swap on.") == null, "features: another mod's line is null");
        Check(UrielFeatures.Parse("") == null && UrielFeatures.Parse(null) == null, "features: empty is null");
    }

    // ------------------------------------------------------------------ refusals
    private static void UrielRefusals()
    {
        void R(string line, UrielFeature f, bool adminOnly)
        {
            var r = UrielFeatures.ClassifyRefusal(line);
            Check(r.HasValue && r.Value.Feature == f && r.Value.AdminOnly == adminOnly, $"refusal: {line}");
        }
        R("Stair swapping is disabled by the server admin.", UrielFeature.Stairs, false);
        R("Object spawning is disabled by the server admin (ObjectSpawn.Enabled).", UrielFeature.Objects, false);
        R("Castle events are disabled by the server admin (CastleEvents.Enabled).", UrielFeature.Events, false);
        R("Event doors are disabled by the server admin (CastleEvents.DoorGates).", UrielFeature.Doors, false);
        R("Public storage is disabled by the server admin.", UrielFeature.Chests, false);
        R("Prison-cell sharing is disabled by the server admin (PublicStorage.PrisonEnabled).", UrielFeature.Cells, false);
        R("Prison-cell sharing is disabled by the server admin.", UrielFeature.Cells, false);
        R("Paid shares are disabled by the server admin (PublicStorage.CostsEnabled).", UrielFeature.Costs, false);
        R("[Uriel] This container has a price, and paid shares are switched off on this server — it's closed until its owner removes the price.", UrielFeature.Costs, false);
        R("Object spawning is admin-only on this server (ObjectSpawn.AdminOnly).", UrielFeature.Objects, true);
        R("Castle events are admin-only on this server (CastleEvents.PlayerEvents).", UrielFeature.Events, true);
        Check(UrielFeatures.ClassifyRefusal("Stair swapping is disabled by the server admin") == null, "refusal: no partial match");
        Check(UrielFeatures.ClassifyRefusal("Player says: Public storage is disabled by the server admin.") == null, "refusal: no substring match");
    }

    // ------------------------------------------------------------------ Effective / Gate
    private static void UrielGates()
    {
        var m = new UrielFeatureModel();
        foreach (var f in UrielFeatures.All)
            Check(m.Gate(f).Enabled, $"gate: unknown {f} stays usable (0.21.0 servers)");

        Check(m.Apply(UrielFeatures.Parse(UrielFeaturesLine(objAdmin: true, events: "OFF"))), "gate: first snapshot changes");
        Check(!m.Apply(UrielFeatures.Parse(UrielFeaturesLine(objAdmin: true, events: "OFF"))), "gate: same snapshot doesn't change");
        Eq(m.Effective(UrielFeature.Doors), UrielSwitch.Off, "gate: doors follow events");
        var g = m.Gate(UrielFeature.Doors);
        Check(!g.Enabled && g.Reason.Contains("[CastleEvents] Enabled"), $"gate: doors name the events switch ({g.Reason})");
        Check(m.Gate(UrielFeature.Objects).Enabled, "gate: objects admins-only is still usable (admins see the snapshot)");

        m.Apply(UrielFeatures.Parse(UrielFeaturesLine(chests: "OFF")));
        foreach (var f in new[] { UrielFeature.Chests, UrielFeature.Cells, UrielFeature.Costs })
        {
            var gg = m.Gate(f);
            Check(!gg.Enabled && gg.Reason.Contains("[PublicStorage] Enabled"), $"gate: {f} names the chests switch ({gg.Reason})");
        }
        Check(m.Gate(UrielFeature.Stairs).Enabled, "gate: stairs unaffected by chests");

        m.Apply(UrielFeatures.Parse(UrielFeaturesLine(masterOff: true)));
        foreach (var f in UrielFeatures.All)
        {
            if (f == UrielFeature.Master) continue;
            var gg = m.Gate(f);
            Eq(gg.Reason, "Uriel is switched off on this server ([General] Enabled).", $"gate: master off: {f}");
        }

        m.Clear();
        Check(!m.HasSnapshot && m.Gate(UrielFeature.Stairs).Enabled, "gate: clear resets");
        Check(m.Apply(UrielFeatures.ClassifyRefusal("Stair swapping is disabled by the server admin.").Value), "gate: refusal turns off");
        Check(!m.Apply(UrielFeatures.ClassifyRefusal("Stair swapping is disabled by the server admin.").Value), "gate: second refusal no change");
        Eq(m.Gate(UrielFeature.Stairs).Reason, "Stair swap is off on this server ([StairSwap] Enabled).", "gate: stairs reason");
        Check(m.Apply(UrielFeatures.ClassifyRefusal("Object spawning is admin-only on this server (ObjectSpawn.AdminOnly).").Value), "gate: admin-only refusal marks objects");
        Check(!m.Gate(UrielFeature.Objects).Enabled && m.Gate(UrielFeature.Objects).Reason.Contains("admin-only"), "gate: objects admin-only for this player");
        Check(!m.Apply(UrielFeatures.ClassifyRefusal("Castle events are admin-only on this server (CastleEvents.PlayerEvents).").Value), "gate: events admin-only left to UrielEvents");
        m.Apply(UrielFeatures.Parse(UrielFeaturesLine()));
        Check(m.Gate(UrielFeature.Objects).Enabled && m.Gate(UrielFeature.Stairs).Enabled, "gate: a snapshot replaces refusals");
        Check(m.SetOff(UrielFeature.Objects) && !m.SetOff(UrielFeature.Objects), "gate: SetOff once");
    }

    // ------------------------------------------------------------------ reply classification
    private static void UrielReplies()
    {
        void C(string line, UrielAction? a, UrielReplyKind k, string domain = "")
        {
            var r = UrielControlFlow.ClassifyReply(line);
            Check(r.HasValue && r.Value.Action == a && r.Value.Kind == k && r.Value.Domain == domain, $"reply: {line}");
        }
        C("This removes everything Uriel put in the world: every castle event is stopped and its doors put back, every Uriel unit and Uriel-spawned object is removed, and every shared chest and prison cell is made private. Run '.uriel killswitch confirm' within 30 s. To keep it off afterwards, set [General] Enabled = false in the config.",
            UrielAction.KillSwitch, UrielReplyKind.Arm);
        C("Kill switch done: 2 event(s) stopped, 14 unit(s) being removed, 30 object(s) removed, 5 container(s) made private.", UrielAction.KillSwitch, UrielReplyKind.Done);
        C("Not armed. Run '.uriel killswitch' first.", UrielAction.KillSwitch, UrielReplyKind.NotArmed);
        C("Objects: failed (Object reference not set to an instance of an object.).", UrielAction.KillSwitch, UrielReplyKind.Failure, "Objects");
        C("Storage: failed (boom).", UrielAction.KillSwitch, UrielReplyKind.Failure, "Storage");
        C("3 spawned object(s) couldn't be found right now and are still tracked; run it again once their area is loaded.", UrielAction.KillSwitch, UrielReplyKind.Note);
        C("1 event door(s) weren't found yet; Uriel keeps trying to put them back.", UrielAction.KillSwitch, UrielReplyKind.Note);
        C("This removes every Uriel-spawned object on the server (42 tracked). Run '.uriel purgeall confirm' within 30 s.", UrielAction.PurgeObjects, UrielReplyKind.Arm);
        C("Removed 42 Uriel-spawned object(s) server-wide.", UrielAction.PurgeObjects, UrielReplyKind.Done);
        C("Removed 40 Uriel-spawned object(s) server-wide. 2 couldn't be found right now and are still tracked; run it again once their area is loaded.", UrielAction.PurgeObjects, UrielReplyKind.Done);
        C("Not armed. Run '.uriel purgeall' first.", UrielAction.PurgeObjects, UrielReplyKind.NotArmed);
        C("This removes every Uriel unit on the server (12 tracked, 3 pending). Run '.uriel unit purge confirm' within 30 s.", UrielAction.PurgeUnits, UrielReplyKind.Arm);
        C("Purging 12 unit(s); cancelled 3 pending spawn(s).", UrielAction.PurgeUnits, UrielReplyKind.Done);
        C("Not armed. Run '.uriel unit purge' first.", UrielAction.PurgeUnits, UrielReplyKind.NotArmed);
        C("No castle event is running.", UrielAction.StopEvents, UrielReplyKind.Done);
        C("Stopped 2 castle event(s).", UrielAction.StopEvents, UrielReplyKind.Done);
        C("Stopped 2 castle event(s). 1 door(s) weren't found yet; Uriel keeps trying to put them back.", UrielAction.StopEvents, UrielReplyKind.Done);
        C("Uriel is not yet initialized.", null, UrielReplyKind.NotReady);
        Check(UrielControlFlow.ClassifyReply("Kill switch done.") == null, "reply: unknown text is null");
        Eq(UrielControlFlow.Command(UrielAction.KillSwitch, true), ".uriel killswitch confirm", "command: killswitch confirm");
        Eq(UrielControlFlow.Command(UrielAction.PurgeUnits, false), ".uriel unit purge", "command: unit purge");
        Eq(UrielControlFlow.Command(UrielAction.PurgeObjects, true), ".uriel purgeall confirm", "command: purgeall confirm");
        Eq(UrielControlFlow.Command(UrielAction.StopEvents, true), ".uriel event stopall", "command: stopall");
    }

    private const string UArmPurge = "This removes every Uriel-spawned object on the server (42 tracked). Run '.uriel purgeall confirm' within 30 s.";
    private const string UDonePurge = "Removed 42 Uriel-spawned object(s) server-wide.";

    // ------------------------------------------------------------------ arm window (25 s from the send)
    private static void UrielFlowArm()
    {
        var f = new UrielControlFlow();
        Check(!f.AnyActive, "flow: idle is not active");
        f.ArmSent(UrielAction.PurgeObjects, 100);
        Eq(f.Step(UrielAction.PurgeObjects), UrielStep.ArmSent, "flow: arm sent");
        Check(f.AnyActive && !f.CanArm(UrielAction.PurgeObjects) && !f.CanConfirm(UrielAction.PurgeObjects), "flow: waiting for the arm");
        Check(f.OnLine(UArmPurge, 106), "flow: arm reply accepted");
        Eq(f.Step(UrielAction.PurgeObjects), UrielStep.Armed, "flow: armed");
        Eq(f.ArmSecondsLeft(UrielAction.PurgeObjects, 106), 19.0, "flow: a 6 s reply leaves 19 s");
        Check(f.CanConfirm(UrielAction.PurgeObjects) && f.CanArm(UrielAction.PurgeObjects), "flow: armed: confirm and re-arm usable");
        Check(f.Tick(124.9).Count == 0, "flow: still armed at 24.9 s");
        Check(f.Tick(125).Contains(UrielAction.PurgeObjects), "flow: tick reports expiry");
        Eq(f.Step(UrielAction.PurgeObjects), UrielStep.Expired, "flow: expired at 25 s from the send");
        Check(!f.AnyActive, "flow: expired is not active");

        // No reply within 10 s.
        f.ArmSent(UrielAction.KillSwitch, 200);
        f.Tick(209.9);
        Eq(f.Step(UrielAction.KillSwitch), UrielStep.ArmSent, "flow: still waiting at 9.9 s");
        f.Tick(210);
        Eq(f.Step(UrielAction.KillSwitch), UrielStep.NoReply, "flow: no reply at 10 s");
        Check(!f.AnyActive, "flow: no reply is not active");
        Check(f.OnLine("This removes everything Uriel put in the world: x. To keep it off afterwards, set [General] Enabled = false in the config.", 211)
              && f.Step(UrielAction.KillSwitch) == UrielStep.NoReply && f.Unsolicited.Count == 1, "flow: a late arm is unsolicited");

        // Re-arm restarts the slot.
        f.ArmSent(UrielAction.PurgeObjects, 300);
        f.OnLine(UArmPurge, 301);
        f.ArmSent(UrielAction.PurgeObjects, 310);
        Eq(f.Step(UrielAction.PurgeObjects), UrielStep.ArmSent, "flow: re-arm restarts");
        f.OnLine(UArmPurge, 312);
        Eq(f.ArmSecondsLeft(UrielAction.PurgeObjects, 312), 23.0, "flow: re-arm window from the new send");

        // NotReady refuses a waiting kill switch.
        var g = new UrielControlFlow();
        g.ArmSent(UrielAction.KillSwitch, 0);
        Check(g.OnLine("Uriel is not yet initialized.", 1) && g.Step(UrielAction.KillSwitch) == UrielStep.Refused, "flow: not initialized refuses");
        Check(!g.OnLine("Uriel is not yet initialized.", 2) && g.Unsolicited.Count == 0, "flow: not initialized is never unsolicited");
        var two = new UrielControlFlow();
        two.ArmSent(UrielAction.KillSwitch, 0); two.ArmSent(UrielAction.PurgeObjects, 0);
        Check(!two.OnLine("Uriel is not yet initialized.", 1) && two.Step(UrielAction.KillSwitch) == UrielStep.ArmSent
              && two.Step(UrielAction.PurgeObjects) == UrielStep.ArmSent, "flow: not initialized with two waiting matches neither");
    }

    // ------------------------------------------------------------------ confirm (two presses within 3 s)
    private static void UrielFlowConfirm()
    {
        var f = new UrielControlFlow();
        Check(!f.Press(UrielAction.PurgeObjects, 0), "flow: confirm needs an arm");
        Eq(f.Step(UrielAction.PurgeObjects), UrielStep.Idle, "flow: unarmed press does nothing");
        f.ArmSent(UrielAction.PurgeObjects, 0);
        f.OnLine(UArmPurge, 1);
        Check(!f.Press(UrielAction.PurgeObjects, 5), "flow: first press");
        Eq(f.Step(UrielAction.PurgeObjects), UrielStep.ConfirmPending, "flow: confirm pending");
        f.Tick(8.1);
        Eq(f.Step(UrielAction.PurgeObjects), UrielStep.Armed, "flow: 3 s press window lapses back to armed");
        Check(!f.Press(UrielAction.PurgeObjects, 10), "flow: first press again");
        Check(f.Press(UrielAction.PurgeObjects, 12), "flow: second press within 3 s sends");
        f.ConfirmSent(UrielAction.PurgeObjects, 12);
        Eq(f.Step(UrielAction.PurgeObjects), UrielStep.ConfirmSent, "flow: confirm sent");
        Check(!f.CanConfirm(UrielAction.PurgeObjects) && !f.CanArm(UrielAction.PurgeObjects), "flow: nothing pressable while sending");
        Check(f.OnLine(UDonePurge, 13), "flow: done accepted");
        Eq(f.Step(UrielAction.PurgeObjects), UrielStep.Done, "flow: done");
        Check(!f.AnyActive, "flow: done is not active");

        // A press near the window's end is cut to the window.
        var h = new UrielControlFlow();
        h.ArmSent(UrielAction.PurgeUnits, 0);
        h.OnLine("This removes every Uriel unit on the server (1 tracked, 0 pending). Run '.uriel unit purge confirm' within 30 s.", 1);
        h.Press(UrielAction.PurgeUnits, 24);
        h.Tick(25);
        Eq(h.Step(UrielAction.PurgeUnits), UrielStep.Expired, "flow: press window ends with the arm window");

        // Not armed on the server.
        var n = new UrielControlFlow();
        n.ArmSent(UrielAction.KillSwitch, 0);
        n.OnLine("This removes everything Uriel put in the world: x. To keep it off afterwards, set [General] Enabled = false in the config.", 1);
        n.Press(UrielAction.KillSwitch, 2); n.Press(UrielAction.KillSwitch, 3); n.ConfirmSent(UrielAction.KillSwitch, 3);
        n.OnLine("Not armed. Run '.uriel killswitch' first.", 4);
        Eq(n.Step(UrielAction.KillSwitch), UrielStep.Expired, "flow: not armed reads as expired");

        // Confirm sent, no reply.
        n.ArmSent(UrielAction.KillSwitch, 50);
        n.OnLine("This removes everything Uriel put in the world: x. To keep it off afterwards, set [General] Enabled = false in the config.", 51);
        n.Press(UrielAction.KillSwitch, 52); n.Press(UrielAction.KillSwitch, 53); n.ConfirmSent(UrielAction.KillSwitch, 53);
        n.Tick(63);
        Eq(n.Step(UrielAction.KillSwitch), UrielStep.NoReply, "flow: confirm no reply at 10 s");

        // Stop all events: no arm, two presses.
        var s = new UrielControlFlow();
        Check(!s.CanArm(UrielAction.StopEvents) && s.CanConfirm(UrielAction.StopEvents), "flow: stopall has no arm");
        Check(!s.Press(UrielAction.StopEvents, 0) && s.Press(UrielAction.StopEvents, 2), "flow: stopall two presses");
        s.ConfirmSent(UrielAction.StopEvents, 2);
        s.OnLine("No castle event is running.", 3);
        Eq(s.Step(UrielAction.StopEvents), UrielStep.Done, "flow: stopall done");
        Check(!s.Press(UrielAction.StopEvents, 10), "flow: stopall again after done");
        s.Tick(13.5);
        Eq(s.Step(UrielAction.StopEvents), UrielStep.Done, "flow: stopall lapsed press returns to done");
    }

    // ------------------------------------------------------------------ kill switch result lines (3 s gather)
    private static void UrielFlowGather()
    {
        var f = new UrielControlFlow();
        f.ArmSent(UrielAction.KillSwitch, 0);
        f.OnLine("This removes everything Uriel put in the world: x. To keep it off afterwards, set [General] Enabled = false in the config.", 0.5);
        f.Press(UrielAction.KillSwitch, 1); f.Press(UrielAction.KillSwitch, 1.5); f.ConfirmSent(UrielAction.KillSwitch, 0);
        Check(f.OnLine("Kill switch done: 1 event(s) stopped, 2 unit(s) being removed, 3 object(s) removed, 4 container(s) made private.", 9.9), "gather: done at 9.9 s accepted");
        Check(f.AnyActive && f.Gathering(UrielAction.KillSwitch, 10), "gather: open after done");
        f.OnLine("Units: failed (x).", 12);
        Eq(f.Step(UrielAction.KillSwitch), UrielStep.Partial, "gather: failure at 12 s makes it partial");
        Check(f.FailedDomains(UrielAction.KillSwitch).Count == 1 && f.FailedDomains(UrielAction.KillSwitch)[0] == "Units", "gather: failed domain");
        f.OnLine("1 event door(s) weren't found yet; Uriel keeps trying to put them back.", 12.5);
        Eq(f.Lines(UrielAction.KillSwitch).Count, 3, "gather: three lines kept");
        Check(f.Tick(12.9).Contains(UrielAction.KillSwitch), "gather: tick closes it");
        Check(!f.AnyActive, "gather: closed is not active");
        f.OnLine("Objects: failed (y).", 13.1);
        Check(f.Lines(UrielAction.KillSwitch).Count == 3 && f.Unsolicited.Count == 1, "gather: a failure at 13.1 s is unsolicited");
    }

    // ------------------------------------------------------------------ only replies this client asked for
    private static void UrielFlowOwnership()
    {
        var f = new UrielControlFlow();
        Check(f.OnLine(UDonePurge, 0), "owner: an unasked reply is shown");
        Eq(f.Step(UrielAction.PurgeObjects), UrielStep.Idle, "owner: an unasked reply moves no button");
        for (int i = 0; i < 6; i++) f.OnLine("Stopped 1 castle event(s).", i);
        Eq(f.Unsolicited.Count, UrielControlFlow.MaxUnsolicited, "owner: unsolicited list is capped");
        f.ArmSent(UrielAction.PurgeObjects, 10);
        f.OnLine(UDonePurge, 11);
        Eq(f.Step(UrielAction.PurgeObjects), UrielStep.ArmSent, "owner: a done line doesn't satisfy an arm wait");
        Check(!f.OnLine("hello", 12), "owner: other lines ignored");
        f.Reset();
        Check(!f.AnyActive && f.Unsolicited.Count == 0 && f.Step(UrielAction.PurgeObjects) == UrielStep.Idle, "owner: reset clears everything");
    }

    // ------------------------------------------------------------------ source guards (main menu, subscriptions, ticker)
    private static void UrielSourceGuards()
    {
        var fixtures = FixturesDir();
        Check(fixtures != null, "uriel guards: repo found");
        if (fixtures == null) return;
        var root = Path.Combine(Directory.GetParent(fixtures).Parent.FullName, "Raphael");
        var ui = Directory.GetFiles(Path.Combine(root, "UI", "ModContent"), "MainPanel.Uriel*.cs");
        var svc = Directory.GetFiles(Path.Combine(root, "Services", "Uriel"), "*.cs");
        Check(ui.Length >= 3 && svc.Length >= 4, $"uriel guards: files found ({ui.Length} UI, {svc.Length} services)");
        string[] rail = { "RefreshAllTabGroupAvailability", "ReconcileRailExpansion", "RequestRebuildMainPanel", "ApplyRailChanges", "RailMemory", "RebuildMainPanel" };
        foreach (var file in ui.Concat(svc))
        {
            var text = Regex.Replace(File.ReadAllText(file), @"//[^\n]*", "");
            foreach (var r in rail)
                Eq(Regex.Matches(text, $@"\b{r}\b").Count, 0, $"uriel rail guard: {Path.GetFileName(file)} calls {r}");
        }

        string Code(string path) => Regex.Replace(File.ReadAllText(path), @"//[^\n]*", "");
        var allUi = string.Concat(Directory.GetFiles(Path.Combine(root, "UI"), "*.cs", SearchOption.AllDirectories).Select(Code));
        Eq(Regex.Matches(allUi, @"UrielFeatureState\.Changed \+=").Count, 1, "uriel guard: Changed subscribed once");
        Eq(Regex.Matches(allUi, @"UrielFeatureState\.Changed -=").Count, 1, "uriel guard: Changed unsubscribed once");

        var state = Code(Path.Combine(root, "Services", "Uriel", "UrielFeatureState.cs"));
        foreach (var bad in new[] { "AvailabilityChanged", "PresenceChanged", "FireAvailability", "DestroyEntity", "Destroy(" })
            Check(!state.Contains(bad), $"uriel guard: UrielFeatureState has no {bad}");

        var ctl = Code(Path.Combine(root, "UI", "ModContent", "MainPanel.UrielControls.cs"));
        int start = ctl.IndexOf("void StartUrielControlsTicker", System.StringComparison.Ordinal);
        int stop = ctl.IndexOf("void StopUrielControlsTicker", System.StringComparison.Ordinal);
        Check(start >= 0 && stop > start, "uriel guard: ticker methods found");
        if (start >= 0 && stop > start)
        {
            var body = ctl.Substring(start, stop - start);
            Check(body.Contains("UrielFeatureState.Tick()"), "uriel guard: ticker calls Tick");
            Check(body.Contains("!UrielFeatureState.Flow.AnyActive") && body.Contains("StopUrielControlsTicker()"), "uriel guard: ticker removes itself when idle");
            Check(ctl.Substring(stop).Contains("Actions.Remove(_urielCtlTicker)"), "uriel guard: stop removes the ticker");
        }
        var main = Code(Path.Combine(root, "UI", "ModContent", "MainPanel.Uriel.cs"));
        int unsub = main.IndexOf("UrielFeatureState.Changed -=", System.StringComparison.Ordinal);
        Check(unsub >= 0 && main.IndexOf("StopUrielControlsTicker()", unsub, System.StringComparison.Ordinal) > unsub, "uriel guard: unsubscribe stops the ticker");
    }
}
