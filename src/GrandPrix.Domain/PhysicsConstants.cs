namespace GrandPrix.Domain;

/// <summary>
/// Global constants from the problem statement (SPECIFICATION.md §13).
/// These are NOT in the level file and are fixed across all levels.
/// </summary>
public static class PhysicsConstants
{
    /// <summary>Gravitational acceleration (m/s²), from the PDF corner example.</summary>
    public const double G = 9.8;

    // Tyre degradation constants.
    public const double KStraight = 0.0000166;
    public const double KBraking = 0.0398;
    public const double KCorner = 0.000265;

    /// <summary>Fuel drag coefficient (L/m per (m/s)²). 1.5e-9.</summary>
    public const double KDrag = 0.0000000015;

    /// <summary>Flat degradation added to the active tyre set on a corner crash.</summary>
    public const double CrashTyrePenalty = 0.1;
}
