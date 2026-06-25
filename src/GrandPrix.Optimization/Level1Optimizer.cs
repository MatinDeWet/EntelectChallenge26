using GrandPrix.Domain;

namespace GrandPrix.Optimization;

/// <summary>
/// Level 1: no degradation, no fuel limit, single dry weather. The optimum is to run the
/// highest-friction tyre and drive every straight flat-out, braking exactly to each corner's
/// safe speed. No pit stops are needed (one tank lasts the race; tyres don't wear).
/// </summary>
public sealed class Level1Optimizer : ILevelOptimizer
{
    public int Level => 1;

    public double CornerSafetyMargin { get; init; } = 0.001;

    public RacePlan Optimize(Level level)
    {
        var planner = new FlatOutPlanner(level, CornerSafetyMargin);
        var plan = new RacePlan { InitialTyreId = planner.InitialTyreId };

        var speed = 0.0; // race starts at rest
        for (var lap = 1; lap <= level.Race.Laps; lap++)
        {
            var (segments, exitSpeed) = planner.BuildLap(lap, speed);
            plan.Laps.Add(new LapPlan { Lap = lap, Segments = segments, Pit = PitAction.None() });
            speed = exitSpeed;
        }

        return plan;
    }
}
