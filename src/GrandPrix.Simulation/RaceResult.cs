namespace GrandPrix.Simulation;

/// <summary>Per-level toggles. Lets us honour level rules (e.g. "no degradation in level 1").</summary>
public sealed class SimulationOptions
{
    /// <summary>If false, tyres never wear, friction never drops, no blowouts (Level 1).</summary>
    public bool EnableDegradation { get; init; } = true;

    /// <summary>If false, running out of fuel does NOT trigger limp mode (Level 1: "no fuel limitations").</summary>
    public bool EnableFuelLimp { get; init; } = true;

    /// <summary>Crash tolerance: entry speed is a crash only if it exceeds safe speed by more than this.</summary>
    public double CrashEpsilon { get; init; } = 1e-9;

    // Cumulative level rules: degradation is only active from Level 4 (Levels 1–3 have a
    // single tyre set per compound and do not focus on wear); fuel limits apply from
    // Level 2 onward (Level 1 has "no fuel limitations").
    public static SimulationOptions ForLevel(int level) => new()
    {
        EnableDegradation = level >= 4,
        EnableFuelLimp = level >= 2,
    };
}

public sealed class RaceResult
{
    public double TotalTime { get; set; }
    public double TotalFuelUsed { get; set; }
    public double FuelRemaining { get; set; }
    public double TotalDegradation { get; set; }
    public int BlowoutCount { get; set; }
    public int CrashCount { get; set; }
    public bool EverLimp { get; set; }
    public bool EverCrawl { get; set; }
    public int PitStopCount { get; set; }
    public List<double> LapTimes { get; } = new();
    public List<double> LapFuelUsed { get; } = new();
    public List<string> Warnings { get; } = new();
}
