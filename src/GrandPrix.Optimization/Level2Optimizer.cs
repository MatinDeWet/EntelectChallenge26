using GrandPrix.Domain;
using GrandPrix.Simulation;

namespace GrandPrix.Optimization;

/// <summary>
/// Level 2: fuel management + pit stops. Fuel burn is dominated by the distance-based base
/// term (K_base · distance), which is independent of speed, so total fuel for the race is
/// essentially fixed and well above both the soft cap and the tank capacity. Consequences:
///  • The fuel bonus is effectively fixed — we cannot approach the soft cap by slowing down.
///  • The only lever for score is time ⇒ drive flat-out (as in Level 1).
///  • The tank cannot hold the whole race's fuel, so we insert the *minimum* number of
///    refuel pit stops (refuel-to-full early, top up the last stint) to never run dry.
/// </summary>
public sealed class Level2Optimizer : ILevelOptimizer
{
    public int Level => 2;

    public double CornerSafetyMargin { get; init; } = 0.001;

    /// <summary>Fuel buffer (L) kept in reserve when deciding to pit, against estimate drift.</summary>
    public double FuelSafetyBuffer { get; init; } = 1.0;

    public RacePlan Optimize(Level level)
    {
        var planner = new FlatOutPlanner(level, CornerSafetyMargin);

        // Pass 1: a no-pit flat-out plan, simulated to get the true per-lap fuel consumption.
        // Fuel-limp is disabled here so the estimate reflects flat-out driving even though one
        // tank cannot cover the whole race (otherwise late laps would be measured in limp mode).
        var noPit = BuildPlan(level, planner, new Dictionary<int, double>());
        var estimateOptions = new SimulationOptions { EnableDegradation = false, EnableFuelLimp = false };
        var lapFuel = new RaceSimulator(level, estimateOptions).Simulate(noPit).LapFuelUsed;

        // Schedule the minimum refuel stops so the car never runs dry.
        var pits = PitScheduler.ScheduleRefuels(level, lapFuel, FuelSafetyBuffer);

        // Pass 2: rebuild with pits (a pitted lap exits at pit_exit_speed into the next lap).
        return BuildPlan(level, planner, pits);
    }

    private static RacePlan BuildPlan(Level level, FlatOutPlanner planner, IReadOnlyDictionary<int, double> pits)
    {
        var plan = new RacePlan { InitialTyreId = planner.InitialTyreId };
        var speed = 0.0;

        for (var lap = 1; lap <= level.Race.Laps; lap++)
        {
            var (segments, exitSpeed) = planner.BuildLap(lap, speed);

            PitAction pit;
            if (pits.TryGetValue(lap, out var refuel) && refuel > 0)
            {
                pit = new PitAction { Enter = true, FuelRefuelAmount = Round(refuel) };
                speed = level.Race.PitExitSpeed;
            }
            else
            {
                pit = PitAction.None();
                speed = exitSpeed;
            }

            plan.Laps.Add(new LapPlan { Lap = lap, Segments = segments, Pit = pit });
        }

        return plan;
    }

    private static double Round(double v) => Math.Round(v, 3, MidpointRounding.AwayFromZero);
}
