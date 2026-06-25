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

    public static SimulationOptions ForLevel(int level) => level switch
    {
        1 => new SimulationOptions { EnableDegradation = false, EnableFuelLimp = false },
        _ => new SimulationOptions { EnableDegradation = true, EnableFuelLimp = true },
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
    public List<double> LapTimes { get; } = new();
    public List<string> Warnings { get; } = new();
}
