using System.Text.Json;
using BloodcraftEclipseBridge.Messages;
using Raphael.Services.Emberglass;

// 0.73.0: the experimental Emberglass receiver (PLAN.md §1): the wire format Bloodcraft sends, the packet identity
// Emberglass matches on, the registration decision, and the honest status line.
internal static partial class Program
{
    private static void EmberglassTests()
    {
        // Emberglass hashes the packet type's FullName; Bloodcraft 1.13.22+ declares exactly this one.
        Eq(typeof(EclipseServerMessagePacket).FullName, "BloodcraftEclipseBridge.Messages.EclipseServerMessagePacket", "emberglass: packet FullName");

        // Wire: Emberglass Network/Serialization.cs packs with these options; the server packs Bloodcraft's packet.
        var opts = new JsonSerializerOptions { IncludeFields = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        const string raw = "[1]:12,34,56;macAbCd+/=";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new EclipseServerMessagePacket(raw), typeof(EclipseServerMessagePacket), opts);
        Eq(System.Text.Encoding.UTF8.GetString(bytes), "{\"message\":\"[1]:12,34,56;macAbCd\\u002B/=\"}", "emberglass: camelCase wire");
        var back = (EclipseServerMessagePacket)JsonSerializer.Deserialize(bytes, typeof(EclipseServerMessagePacket), opts);
        Eq(back.Message, raw, "emberglass: round trip into Raphael's packet");

        // Who registers.
        Eq(EmberglassBridgeState.Decide(false, true, false), EmberglassBridgeStatus.Off, "emberglass: setting off");
        Eq(EmberglassBridgeState.Decide(true, false, false), EmberglassBridgeStatus.NotInstalled, "emberglass: not installed");
        Eq(EmberglassBridgeState.Decide(true, true, true), EmberglassBridgeStatus.EclipseHandles, "emberglass: Eclipse loaded");
        Eq(EmberglassBridgeState.Decide(true, true, false), EmberglassBridgeStatus.RegisteredNoData, "emberglass: registers");

        // Routing seam: the Message goes to the same handler as chat; only an accepted packet counts.
        var st = new EmberglassBridgeState();
        st.Registered();
        var seen = new List<string>();
        Check(st.Describe(0).Contains("registered, no data yet"), "emberglass: registration alone isn't receiving");
        Check(!st.Deliver("", 1, m => { seen.Add(m); return true; }), "emberglass: empty message dropped");
        Eq(seen.Count, 0, "emberglass: empty never reaches the handler");
        Check(!st.Deliver("bad", 2, m => { seen.Add(m); return false; }), "emberglass: MAC failure");
        Eq(st.MacFailures, 1, "emberglass: MAC failure counted");
        Eq(st.Status, EmberglassBridgeStatus.RegisteredNoData, "emberglass: MAC failure isn't receiving");
        Check(!st.Deliver("boom", 3, _ => throw new InvalidOperationException()), "emberglass: handler throw contained");
        Check(st.Deliver(raw, 10, m => { seen.Add(m); return true; }), "emberglass: valid packet");
        Eq(seen.Last(), raw, "emberglass: message passed unchanged");
        Eq(st.Status, EmberglassBridgeStatus.Receiving, "emberglass: receiving after a valid packet");
        Check(st.Describe(40).Contains("receiving (1 packet(s), last 30 s ago"), $"emberglass: describe ({st.Describe(40)})");

        var f = new EmberglassBridgeState();
        f.Fail("MissingMethodException: x");
        Check(f.Describe(0).Contains("couldn't register") && f.Describe(0).Contains("chat"), "emberglass: failure line");
        Check(new EmberglassBridgeState().Describe(0).StartsWith("Emberglass bridge: off"), "emberglass: default off");
    }
}
