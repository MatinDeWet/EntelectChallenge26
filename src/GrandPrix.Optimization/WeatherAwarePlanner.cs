using GrandPrix.Domain;
using GrandPrix.Simulation;

namespace GrandPrix.Optimization;

/// <summary>
/// Builds a full race plan with weather-aware speeds. Weather changes friction (corner safe
/// speeds), acceleration and deceleration, all of which depend on the cumulative race time —
/// so this does a forward pass that tracks time identically to the simulator (shared
/// <see cref="StraightProfile"/>), inserting pit times so later laps see the correct weather.
/// Tyres never degrade here (Levels 1–3), so a single best compound is run throughout.
/// </summary>
public sealed class WeatherAwarePlanner
{
    private readonly Level _level;
    private readonly Car _car;
    private readonly WeatherSchedule _schedule;
    private readonly TyreProperties _tyre;
    private readonly double _margin;
    private readonly IReadOnlyList<Segment> _segs;

    public int InitialTyreId { get; }

    public WeatherAwarePlanner(Level level, double cornerSafetyMargin = 0.005)
    {
        _level = level;
        _car = level.Car;
        _schedule = new WeatherSchedule(level);
        _margin = cornerSafetyMargin;
        _segs = level.Track.Segments;

        InitialTyreId = PickBestTyreId(level);
        _tyre = level.PropertiesOfId(InitialTyreId);
    }

    public RacePlan BuildRace(IReadOnlyDictionary<int, double> pits)
    {
        var plan = new RacePlan { InitialTyreId = InitialTyreId };
        var time = 0.0;
        var speed = 0.0;

        for (var lap = 1; lap <= _level.Race.Laps; lap++)
        {
            var lapPlan = new LapPlan { Lap = lap, Pit = PitAction.None() };

            for (var i = 0; i < _segs.Count; i++)
            {
                var seg = _segs[i];
                if (seg.Type == SegmentType.Corner)
                {
                    lapPlan.Segments.Add(SegmentAction.Corner(seg.Id));
                    if (speed > 0) time += seg.Length / speed; // constant speed through the corner
                    continue;
                }

                var (target, brakeStart, straightTime, endSpeed) = SolveStraight(i, speed, time);
                lapPlan.Segments.Add(SegmentAction.Straight(seg.Id, target, brakeStart));
                time += straightTime;
                speed = endSpeed;
            }

            if (pits.TryGetValue(lap, out var refuel) && refuel > 0)
            {
                lapPlan.Pit = new PitAction { Enter = true, FuelRefuelAmount = Round(refuel) };
                time += _level.Race.BasePitStopTime + refuel / _level.Race.PitRefuelRate;
                speed = _level.Race.PitExitSpeed;
            }

            plan.Laps.Add(lapPlan);
        }

        return plan;
    }

    private (double target, double brakeStart, double straightTime, double endSpeed)
        SolveStraight(int i, double vIn, double tStart)
    {
        var seg = _segs[i];
        var w0 = _schedule.At(tStart);
        var accelEff = _car.Accel * w0.AccelerationMultiplier;
        var brakeEff = _car.Brake * w0.DecelerationMultiplier; // simulator uses start-of-segment weather

        // Initial estimate: corner chain weather ≈ straight-start weather.
        var vExit = ChainExitSpeed(i, tStart);
        var (target, brakeStart, profile) = SolveOnce(seg, vIn, vExit, accelEff, brakeEff);
        var straightTime = StraightProfile.Time(profile.Phases);

        // Refine: evaluate each chain corner at its true arrival time (tStart + straightTime,
        // then walking the chain). This matches the simulator, which looks up each corner's
        // weather at the moment it is reached — essential across weather-window boundaries.
        for (var iter = 0; iter < 2; iter++)
        {
            var refined = ChainExitSpeed(i, tStart + straightTime);
            var (t2, b2, p2) = SolveOnce(seg, vIn, refined, accelEff, brakeEff);
            var newTime = StraightProfile.Time(p2.Phases);
            target = t2; brakeStart = b2; profile = p2;
            if (Math.Abs(newTime - straightTime) < 1e-9) { straightTime = newTime; break; }
            straightTime = newTime;
        }

        return (target, brakeStart, straightTime, profile.EndSpeed);
    }

    private (double target, double brakeStart, StraightProfile.Result profile)
        SolveOnce(Segment seg, double vIn, double vExit, double accelEff, double brakeEff)
    {
        var sol = StraightSolver.Solve(vIn, vExit, seg.Length, accelEff, brakeEff, _car.MaxSpeed);
        var target = Floor(sol.TargetSpeed);
        var brakeStart = Math.Clamp(Ceil((target * target - vExit * vExit) / (2.0 * brakeEff)), 0.0, seg.Length);
        var profile = StraightProfile.Compute(vIn, target, brakeStart, seg.Length, accelEff, brakeEff, _car.MaxSpeed, _car.CrawlSpeed);
        return (target, brakeStart, profile);
    }

    /// <summary>
    /// Minimum safe speed (with margin) over the corner chain following straight <paramref name="i"/>,
    /// evaluating each corner at the weather active when the car reaches it. The whole chain is
    /// driven at one (constant) speed, so we iterate: assume a chain speed, walk the corners in
    /// time at that speed, recompute the min safe speed, repeat to a fixpoint.
    /// </summary>
    private double ChainExitSpeed(int i, double cornerStartTime)
    {
        var chain = new List<int>();
        for (var j = i + 1; j < _segs.Count && _segs[j].Type == SegmentType.Corner; j++) chain.Add(j);
        if (chain.Count == 0) return _car.MaxSpeed;

        var speed = double.PositiveInfinity;
        for (var iter = 0; iter < 3; iter++)
        {
            var t = cornerStartTime;
            var minSafe = double.PositiveInfinity;
            foreach (var j in chain)
            {
                var w = _schedule.At(t);
                var friction = TyreModel.Friction(_tyre, 0.0, w.Kind);
                var safe = TyreModel.SafeCornerSpeed(friction, _segs[j].Radius!.Value) * (1.0 - _margin);
                minSafe = Math.Min(minSafe, safe);
                var traverse = double.IsPositiveInfinity(speed) ? minSafe : Math.Min(speed, minSafe);
                if (traverse > 1e-6) t += _segs[j].Length / traverse;
            }
            if (!double.IsInfinity(speed) && Math.Abs(minSafe - speed) < 1e-9) { speed = minSafe; break; }
            speed = minSafe;
        }
        return speed;
    }

    /// <summary>Pick the compound with the best worst-case friction across the weathers in this race.</summary>
    private static int PickBestTyreId(Level level)
    {
        var weathers = level.Weather.Conditions.Select(c => c.Kind).Distinct().ToArray();
        var bestId = int.MaxValue;
        var bestScore = double.NegativeInfinity;

        foreach (var set in level.AvailableSets)
        {
            var props = level.PropertiesOf(set.Compound);
            var worst = weathers.Min(w => TyreModel.Friction(props, 0.0, w));
            foreach (var id in set.Ids)
            {
                if (worst > bestScore + 1e-12 ||
                    (Math.Abs(worst - bestScore) <= 1e-12 && id < bestId))
                {
                    bestScore = worst;
                    bestId = id;
                }
            }
        }
        if (bestId == int.MaxValue) throw new InvalidOperationException("No available tyre sets.");
        return bestId;
    }

    private static double Floor(double v) => Math.Floor(v * 1000.0) / 1000.0;
    private static double Ceil(double v) => Math.Ceiling(v * 1000.0) / 1000.0;
    private static double Round(double v) => Math.Round(v, 3, MidpointRounding.AwayFromZero);
}
