namespace BloodcraftEclipseBridge.Messages;

// 0.73.0: Bloodcraft 1.13.22+'s Emberglass packet, mirrored. Emberglass identifies a packet type by a hash of its
// FullName (Emberglass Network/Registry.cs), so the namespace and class name must stay exactly
// "BloodcraftEclipseBridge.Messages.EclipseServerMessagePacket". The payload is System.Text.Json with camelCase
// names, so {"message":"…"} binds to Message. Pure: linked into the logic tests.
internal sealed class EclipseServerMessagePacket
{
    public string Message { get; set; } = string.Empty;

    public EclipseServerMessagePacket() { }

    public EclipseServerMessagePacket(string message) { Message = message; }
}
