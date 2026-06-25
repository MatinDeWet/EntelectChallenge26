using GrandPrix.Domain;
using GrandPrix.Simulation;

namespace GrandPrix.Optimization;

/// <summary>
/// Builds the time-optimal "flat-out" speed plan for a lap: every straight runs as fast as
/// feasible and brakes exactly to each corner's safe speed; corner chains are entered at the
/// minimum safe speed of the chain. Shared by Levels 1 and 2 (constant friction, dry weather,
/// no degradation). Pit/fuel decisions are layered on by the caller.
/// </summary>
public sealed class FlatOutPlanner
{
    private readonly Level _level;
    private readonly double _accelEff;
    private readonly double _brakeEff;
    private readonly double[] _exitSpeed; // required exit speed per segment index (straights)

    public int InitialTyreId { get; }

    /// <summary>Fractional safety margin applied to every corner's safe speed.</summary>
    public double CornerSafetyMargin { get; }

    private const int RoundDigits = 3;

    public FlatOutPlanner(Level level, double cornerSafetyMargin = 0.005)
    {
        _level = level;
        CornerSafetyMargin = cornerSafetyMargin;

        var w = level.StartingWeather();
        _accelEff = level.Car.Accel * w.AccelerationMultiplier;
        _brakeEff = level.Car.Brake * w.DecelerationMultiplier;

        InitialTyreId = PickBestTyreId(level, w.Kind);
        var tyre = level.PropertiesOfId(InitialTyreId);
        var friction = TyreModel.Friction(tyre, 0.0, w.Kind); // no degradation in L1–L3

        var segs = level.Track.Segments;
        var safe = new double[segs.Count];
        for (var i = 0; i < segs.Count; i++)
            if (segs[i].Type == SegmentType.Corner)
                safe[i] = TyreModel.SafeCornerSpeed(friction, segs[i].Radius!.Value, level.Car.CrawlSpeed) * (1.0 - CornerSafetyMargin);

        _exitSpeed = new double[segs.Count];
        for (var i = 0; i < segs.Count; i++)
        {
            if (segs[i].Type != SegmentType.Straight) continue;
            var min = double.PositiveInfinity;
            for (var j = i + 1; j < segs.Count && segs[j].Type == SegmentType.Corner; j++)
                min = Math.Min(min, safe[j]);
            _exitSpeed[i] = double.IsPositiveInfinity(min) ? level.Car.MaxSpeed : min;
        }
    }

    /// <summary>Build one lap's segment actions starting from <paramref name="entrySpeed"/>.
    /// Returns the actions and the speed the car carries into the next lap.</summary>
    public (List<SegmentAction> Segments, double ExitSpeed) BuildLap(int lapNumber, double entrySpeed)
    {
        var segs = _level.Track.Segments;
        var actions = new List<SegmentAction>(segs.Count);
        var speed = entrySpeed;

        for (var i = 0; i < segs.Count; i++)
        {
            var seg = segs[i];
            if (seg.Type == SegmentType.Corner)
            {
                actions.Add(SegmentAction.Corner(seg.Id));
                continue; // speed carries through the corner unchanged
            }

            var vExit = _exitSpeed[i];
            var sol = StraightSolver.Solve(speed, vExit, seg.Length, _accelEff, _brakeEff, _level.Car.MaxSpeed);

            var target = Floor(sol.TargetSpeed);
            var brakeStart = Ceil((target * target - vExit * vExit) / (2.0 * _brakeEff));
            brakeStart = Math.Clamp(brakeStart, 0.0, seg.Length);

            actions.Add(SegmentAction.Straight(seg.Id, target, brakeStart));
            speed = vExit <= target ? vExit : target;
        }

        return (actions, speed);
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
