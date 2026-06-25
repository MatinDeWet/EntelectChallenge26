using GrandPrix.Domain;
using GrandPrix.Simulation;

namespace GrandPrix.Optimization;

/// <summary>
/// Level 1: no degradation, no fuel limit, single dry weather. The optimum is to run the
/// highest-friction tyre and drive every straight flat-out, braking exactly to each
/// corner's safe speed. Corner chains (consecutive corners) are entered at the minimum
/// safe speed of the chain (no acceleration is possible between them).
/// </summary>
public sealed class Level1Optimizer : ILevelOptimizer
{
    public int Level => 1;

    /// <summary>Fractional safety margin applied to every corner's safe speed (guards against
    /// grader rounding / physics differences — a crash is catastrophic).</summary>
    public double CornerSafetyMargin { get; init; } = 0.005;

    private const int RoundDigits = 3;

    public RacePlan Optimize(Level level)
    {
        var car = level.Car;
        var w = level.StartingWeather();
        var accelEff = car.Accel * w.AccelerationMultiplier;
        var brakeEff = car.Brake * w.DecelerationMultiplier;

        var initialTyreId = PickBestTyreId(level, w.Kind);
        var tyre = level.PropertiesOfId(initialTyreId);
        // Level 1: no degradation, so friction is constant for the whole race.
        var friction = TyreModel.Friction(tyre, 0.0, w.Kind);

        var segments = level.Track.Segments;

        // Per-corner safe entry speed (with margin).
        var safeSpeed = new double[segments.Count];
        for (var i = 0; i < segments.Count; i++)
        {
            if (segments[i].Type == SegmentType.Corner)
            {
                var raw = TyreModel.SafeCornerSpeed(friction, segments[i].Radius!.Value);
                safeSpeed[i] = raw * (1.0 - CornerSafetyMargin);
            }
        }

        // For each straight, the required exit speed = min safe speed of the immediately
        // following corner chain (consecutive corners until the next straight).
        var exitSpeed = new double[segments.Count];
        for (var i = 0; i < segments.Count; i++)
        {
            if (segments[i].Type != SegmentType.Straight) continue;
            var min = double.PositiveInfinity;
            for (var j = i + 1; j < segments.Count && segments[j].Type == SegmentType.Corner; j++)
                min = Math.Min(min, safeSpeed[j]);
            // A straight with no following corner before lap-end flows into the next lap's
            // first straight; brake target falls back to max speed (effectively no braking).
            exitSpeed[i] = double.IsPositiveInfinity(min) ? car.MaxSpeed : min;
        }

        var plan = new RacePlan { InitialTyreId = initialTyreId };

        var speed = 0.0; // race starts at rest
        for (var lap = 1; lap <= level.Race.Laps; lap++)
        {
            var lapPlan = new LapPlan { Lap = lap, Pit = PitAction.None() };
            for (var i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                if (seg.Type == SegmentType.Corner)
                {
                    lapPlan.Segments.Add(SegmentAction.Corner(seg.Id));
                    // Speed carries through the corner unchanged (constant corner speed).
                    continue;
                }

                var vExit = exitSpeed[i];
                var sol = StraightSolver.Solve(speed, vExit, seg.Length, accelEff, brakeEff, car.MaxSpeed);

                var target = Floor(sol.TargetSpeed);
                // Recompute braking from the emitted target and round UP so arrival speed ≤ vExit.
                var brakeStart = Ceil((target * target - vExit * vExit) / (2.0 * brakeEff));
                brakeStart = Math.Clamp(brakeStart, 0.0, seg.Length);

                lapPlan.Segments.Add(SegmentAction.Straight(seg.Id, target, brakeStart));

                // Update speed for the next segment: we brake to vExit (the chain speed).
                speed = vExit <= target ? vExit : target;
            }
            plan.Laps.Add(lapPlan);
        }

        return plan;
    }

    private static int PickBestTyreId(Level level, WeatherKind weather)
    {
        var bestId = int.MaxValue;
        var bestFriction = double.NegativeInfinity;
        foreach (var set in level.AvailableSets)
        {
            var props = level.PropertiesOf(set.Compound);
            var friction = TyreModel.Friction(props, 0.0, weather);
            foreach (var id in set.Ids)
            {
                if (friction > bestFriction + 1e-12 ||
                    (Math.Abs(friction - bestFriction) <= 1e-12 && id < bestId))
                {
                    bestFriction = friction;
                    bestId = id;
                }
            }
        }
        if (bestId == int.MaxValue) throw new InvalidOperationException("No available tyre sets.");
        return bestId;
    }

    private static double Floor(double v) => Math.Floor(v * 1000.0) / 1000.0;
    private static double Ceil(double v) => Math.Ceiling(v * 1000.0) / 1000.0;
}
