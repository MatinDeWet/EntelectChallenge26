using GrandPrix.Domain;
using GrandPrix.Simulation;

namespace GrandPrix.Optimization;

/// <summary>
/// Level 3: weather is introduced. It changes corner friction, acceleration and deceleration
/// over time, so speeds must adapt per weather window. With degradation off (Levels 1–3),
/// Soft has the highest friction in *every* weather here, so no tyre changes are beneficial —
/// the strategy is weather-aware flat-out driving plus the same minimal refuel pits as Level 2.
/// </summary>
public sealed class Level3Optimizer : ILevelOptimizer
{
    public int Level => 3;

    public double CornerSafetyMargin { get; init; } = 0.005;
    public double FuelSafetyBuffer { get; init; } = 1.0;

    public RacePlan Optimize(Level level)
    {
        var planner = new WeatherAwarePlanner(level, CornerSafetyMargin);

        // Pass 1: no-pit, weather-aware plan → true per-lap fuel (fuel-limp disabled, see Level 2).
        var noPit = planner.BuildRace(new Dictionary<int, double>());
        var estimateOptions = new SimulationOptions { EnableDegradation = false, EnableFuelLimp = false };
        var lapFuel = new RaceSimulator(level, estimateOptions).Simulate(noPit).LapFuelUsed;

        var pits = PitScheduler.ScheduleRefuels(level, lapFuel, FuelSafetyBuffer);

        // Pass 2: rebuild with pits; pit times shift later laps' weather, handled inside BuildRace.
        return planner.BuildRace(pits);
    }
}
