namespace Raphael.Services.Nyar;

// 0.74.0: Nyarlathotep 0.15.0's two unit limits and their ceilings (Logic/Limits.cs, horde-pools D1 / D2), in one place
// so the editor's hints, the help text and the field rules say the same numbers. Pure: no Unity types, unit-tested.

/// <summary>Nyarlathotep's unit limits as of 0.15.0. MaxTrackedUnits caps every living unit Nyarlathotep tracks at once
/// (all events together); MaxUnitsPerWave caps one SpawnWaves wave only (the horde families ignore it since 0.15.0).</summary>
internal static class NyarLimits
{
    /// <summary>The highest count any one entry, pool range or wave can ask for (MaxUnitsPerWave's ceiling).</summary>
    public const int UnitCeiling = 500;

    /// <summary>[Limits] MaxTrackedUnits: default, lowest, highest (0.15.0 raised the default from 150 to its ceiling).</summary>
    public const int TrackedDefault = 500, TrackedMin = 1, TrackedMax = 500;

    /// <summary>[Limits] MaxUnitsPerWave: default, lowest, highest. SpawnWaves only.</summary>
    public const int PerWaveDefault = 20, PerWaveMin = 1, PerWaveMax = 500;

    /// <summary>A HordeMarch's units: at least this many in all (counting each range at its minimum)...</summary>
    public const int HordeMarchMin = 10;

    /// <summary>...and at most this many (counting each range at its maximum).</summary>
    public const int HordeMarchMax = UnitCeiling;
}
