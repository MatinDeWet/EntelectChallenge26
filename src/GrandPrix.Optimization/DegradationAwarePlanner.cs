using GrandPrix.Domain;
using GrandPrix.Simulation;

namespace GrandPrix.Optimization;

/// <summary>
/// Level 4 planner: weather-aware AND wear-aware. It tracks each tyre set's accumulated
/// degradation, recomputes friction = (base − wear) × weather_multiplier per segment (so corner
/// speeds fall as the tyre wears), and changes to the weather-appropriate compound before a
/// blowout. Refuels are folded into pit stops. Tracks race time exactly like the simulator
/// (shared <see cref="StraightProfile"/>) so weather/wear look-ups line up and crashes are avoided.
/// </summary>
public sealed class DegradationAwarePlanner
{
    private readonly Level _level;
    private readonly Car _car;
    private readonly WeatherSchedule _schedule;
    private readonly IReadOnlyList<Segment> _segs;
    private readonly double _margin;
    private readonly double _changeThreshold; // change tyres when wear would exceed this next lap
    private readonly double _wearWeight;      // 0 = pick fastest tyre; higher = bias toward high-wear compounds
    private readonly Random? _rng;            // when set, explores alternative tyre picks (for search)
    private readonly double _exploreProb;     // probability of picking a non-top fresh set

    public DegradationAwarePlanner(Level level, double cornerSafetyMargin = 0.01,
        double changeThreshold = 0.85, double wearWeight = 0.0,
        Random? rng = null, double exploreProb = 0.0)
    {
        _level = level;
        _car = level.Car;
        _schedule = new WeatherSchedule(level);
        _segs = level.Track.Segments;
        _margin = cornerSafetyMargin;
        _changeThreshold = changeThreshold;
        _wearWeight = wearWeight;
        _rng = rng;
        _exploreProb = exploreProb;
    }

    public RacePlan Build()
    {
        var deg = new Dictionary<int, double>();          // wear per set id
        var mounted = new HashSet<int>();                 // sets that have ever been active
        foreach (var set in _level.AvailableSets)
            foreach (var id in set.Ids) deg[id] = 0.0;

        var time = 0.0;
        var speed = 0.0;
        var fuel = _car.InitialFuel;

        var activeId = PickBestSet(0.0, mounted, deg);
        mounted.Add(activeId);

        var plan = new RacePlan { InitialTyreId = activeId };

        for (var lap = 1; lap <= _level.Race.Laps; lap++)
        {
            var lapPlan = new LapPlan { Lap = lap, Pit = PitAction.None() };
            var lapStartDeg = deg[activeId];
            var lapStartFuel = fuel;

            for (var i = 0; i < _segs.Count; i++)
            {
                var seg = _segs[i];
                var w = _schedule.At(time);
                var props = _level.PropertiesOfId(activeId);
                var degRate = props.DegradationRate(w.Kind);

                if (seg.Type == SegmentType.Corner)
                {
                    lapPlan.Segments.Add(SegmentAction.Corner(seg.Id));
                    var r = seg.Radius!.Value;
                    if (speed > 0) time += seg.Length / speed;
                    deg[activeId] += TyreModel.CornerDegradation(degRate, speed, r);
                    if (speed > 0) fuel -= FuelModel.Used(_car.FuelKBase, speed, speed, seg.Length);
                    continue;
                }

                var (target, brakeStart, straightTime, endSpeed, accelFuel, speedAtBrake) =
                    SolveStraight(i, speed, time, activeId, deg[activeId]);
                lapPlan.Segments.Add(SegmentAction.Straight(seg.Id, target, brakeStart));
                time += straightTime;
                fuel -= accelFuel;
                // Straight wear over the non-braking distance + the separate braking term.
                deg[activeId] += TyreModel.StraightDegradation(degRate, seg.Length - brakeStart);
                if (brakeStart > 0)
                    deg[activeId] += TyreModel.BrakingDegradation(degRate, speedAtBrake, endSpeed);
                speed = endSpeed;
            }

            // End-of-lap pit decision: change tyres before a blowout, and/or refuel. During a
            // randomised search, jitter the change point and refuel trigger so restarts explore
            // different pit timings (not just different compounds).
            var lapDeg = deg[activeId] - lapStartDeg;
            var lapFuelUsed = lapStartFuel - fuel;
            var thr = _changeThreshold + (_rng?.NextDouble() - 0.5 ?? 0.0) * 0.06; // ±3%
            var refuelTrigger = _rng is null ? 2.5 : 1.6 + _rng.NextDouble() * 2.0; // 1.6–3.6 laps
            var changeTyre = lap < _level.Race.Laps && deg[activeId] + lapDeg * 1.15 > thr;
            var refuel = fuel < lapFuelUsed * refuelTrigger;

            if (changeTyre || refuel)
            {
                var pit = new PitAction { Enter = true };
                var pitTime = _level.Race.BasePitStopTime;

                if (changeTyre)
                {
                    var newId = PickBestSet(time, mounted, deg);
                    pit.TyreChangeSetId = newId;
                    pitTime += _level.Race.PitTyreSwapTime;
                    mounted.Add(newId);
                    activeId = newId;
                }

                // Refuel to full whenever we stop (fuel burn is distance-fixed, so this only
                // affects pit time; topping up avoids extra fuel-only stops).
                var add = _car.FuelCapacity - fuel;
                if (add > 0)
                {
                    pit.FuelRefuelAmount = Round(add);
                    pitTime += add / _level.Race.PitRefuelRate;
                    fuel = _car.FuelCapacity;
                }

                lapPlan.Pit = pit;
                time += pitTime;
                speed = _level.Race.PitExitSpeed;
            }

            plan.Laps.Add(lapPlan);
        }

        return plan;
    }

    private (double target, double brakeStart, double straightTime, double endSpeed, double accelFuel, double speedAtBrake)
        SolveStraight(int i, double vIn, double tStart, int setId, double setDeg)
    {
        var seg = _segs[i];
        var w0 = _schedule.At(tStart);
        var accelEff = _car.Accel * w0.AccelerationMultiplier;
        var brakeEff = _car.Brake * w0.DecelerationMultiplier;

        // The tyre wears DURING this straight, so the corner ahead is taken on a slightly more
        // worn (lower-grip) tyre than at the straight's start. Use the post-straight wear when
        // sizing the corner entry speed, otherwise the safety margin is eaten by the in-straight
        // wear and near-limit corners crash on the grader.
        var degRate = _level.PropertiesOfId(setId).DegradationRate(w0.Kind);
        var sol = Solve(seg, vIn, ChainExitSpeed(i, tStart, setId, setDeg), accelEff, brakeEff);
        var straightTime = StraightProfile.Time(sol.profile.Phases);

        for (var iter = 0; iter < 3; iter++)
        {
            var cornerDeg = setDeg + StraightWear(degRate, seg.Length - sol.brakeStart, sol);
            var refined = ChainExitSpeed(i, tStart + straightTime, setId, cornerDeg);
            sol = Solve(seg, vIn, refined, accelEff, brakeEff);
            var nt = StraightProfile.Time(sol.profile.Phases);
            if (Math.Abs(nt - straightTime) < 1e-9) { straightTime = nt; break; }
            straightTime = nt;
        }

        // Fuel for this straight: accelerate + cruise phases only (no fuel while braking).
        var accelFuel = 0.0;
        foreach (var p in sol.profile.Phases)
            if (p.Vf >= p.Vi - 1e-9 && p.Dist > 0)
                accelFuel += FuelModel.Used(_car.FuelKBase, p.Vi, p.Vf, p.Dist);

        return (sol.target, sol.brakeStart, straightTime, sol.profile.EndSpeed, accelFuel, sol.profile.SpeedAtBrake);
    }

    /// <summary>Total tyre wear over a straight: the non-braking straight term + the braking term.</summary>
    private static double StraightWear(double degRate, double nonBrakingDist, (double target, double brakeStart, StraightProfile.Result profile) sol)
    {
        var deg = TyreModel.StraightDegradation(degRate, Math.Max(0.0, nonBrakingDist));
        if (sol.brakeStart > 0)
            deg += TyreModel.BrakingDegradation(degRate, sol.profile.SpeedAtBrake, sol.profile.EndSpeed);
        return deg;
    }

    private (double target, double brakeStart, StraightProfile.Result profile)
        Solve(Segment seg, double vIn, double vExit, double accelEff, double brakeEff)
    {
        var s = StraightSolver.Solve(vIn, vExit, seg.Length, accelEff, brakeEff, _car.MaxSpeed);
        var target = Floor(s.TargetSpeed);
        var brakeStart = Math.Clamp(Ceil((target * target - vExit * vExit) / (2.0 * brakeEff)), 0.0, seg.Length);
        var profile = StraightProfile.Compute(vIn, target, brakeStart, seg.Length, accelEff, brakeEff, _car.MaxSpeed, _car.CrawlSpeed);
        return (target, brakeStart, profile);
    }

    /// <summary>Min safe speed over the following corner chain, using worn friction at each corner's arrival weather.</summary>
    private double ChainExitSpeed(int i, double cornerStartTime, int setId, double setDeg)
    {
        var chain = new List<int>();
        for (var j = i + 1; j < _segs.Count && _segs[j].Type == SegmentType.Corner; j++) chain.Add(j);
        if (chain.Count == 0) return _car.MaxSpeed;

        var props = _level.PropertiesOfId(setId);
        var speed = double.PositiveInfinity;
        for (var iter = 0; iter < 3; iter++)
        {
            var t = cornerStartTime;
            var minSafe = double.PositiveInfinity;
            foreach (var j in chain)
            {
                var w = _schedule.At(t);
                var friction = TyreModel.Friction(props, setDeg, w.Kind);
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

    /// <summary>Look-ahead window (s) over which a freshly-fitted set's weather exposure is averaged.</summary>
    private const double StintLookaheadSeconds = 2000.0;

    /// <summary>
    /// Choose the best set for the stint starting at <paramref name="startTime"/>: score each
    /// compound by its average fresh friction over the weather it will face in the look-ahead
    /// window (so we don't fit Wet just as heavy rain is ending). Prefer fresh sets; fall back to
    /// the non-active set with the most life left if none remain.
    /// </summary>
    private int PickBestSet(double startTime, HashSet<int> mounted, Dictionary<int, double> deg)
    {
        var samples = new List<WeatherKind>();
        for (var dt = 0.0; dt <= StintLookaheadSeconds; dt += 250.0)
            samples.Add(_schedule.KindAt(startTime + dt));

        // Score a compound by average fresh friction (speed) plus an optional bias toward high
        // degradation rate (tyre-bonus). Degradation rates (~0.05–0.16) are scaled up to be
        // comparable to friction (~1–2) before weighting.
        double Score(TyreProperties p)
        {
            var fr = 0.0; var dr = 0.0;
            foreach (var w in samples) { fr += TyreModel.Friction(p, 0.0, w); dr += p.DegradationRate(w); }
            return (fr + _wearWeight * 10.0 * dr) / samples.Count;
        }

        var fresh = new List<(int id, double score)>();
        var bestUsedId = -1; var bestUsedLife = double.NegativeInfinity;

        foreach (var set in _level.AvailableSets)
        {
            var props = _level.PropertiesOf(set.Compound);
            var score = Score(props);
            foreach (var id in set.Ids)
            {
                if (!mounted.Contains(id)) fresh.Add((id, score));
                else
                {
                    var life = props.LifeSpan - deg[id];
                    if (life > bestUsedLife) { bestUsedLife = life; bestUsedId = id; }
                }
            }
        }

        if (fresh.Count == 0) return bestUsedId;

        // Rank fresh sets by score (speed), tie-break by id for determinism.
        fresh.Sort((a, b) => b.score != a.score ? b.score.CompareTo(a.score) : a.id.CompareTo(b.id));

        // Exploration: occasionally pick among the top few alternatives instead of the best,
        // so a randomized-restart search can discover better whole-race tyre schedules.
        if (_rng is not null && _exploreProb > 0 && fresh.Count > 1 && _rng.NextDouble() < _exploreProb)
        {
            var k = Math.Min(3, fresh.Count);
            return fresh[_rng.Next(k)].id;
        }

        return fresh[0].id;
    }

    private static double Floor(double v) => Math.Floor(v * 1000.0) / 1000.0;
    private static double Ceil(double v) => Math.Ceiling(v * 1000.0) / 1000.0;
    private static double Round(double v) => Math.Round(v, 3, MidpointRounding.AwayFromZero);
}
