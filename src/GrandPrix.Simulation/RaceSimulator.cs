using GrandPrix.Domain;

namespace GrandPrix.Simulation;

/// <summary>
/// Authoritative forward simulator (SPECIFICATION.md §4–§7). Executes a plan against a
/// level deterministically and reports time, fuel, wear, crashes and blowouts.
/// </summary>
public sealed class RaceSimulator
{
    private readonly Level _level;
    private readonly SimulationOptions _opt;
    private readonly WeatherSchedule _weather;

    public RaceSimulator(Level level, SimulationOptions options)
    {
        _level = level;
        _opt = options;
        _weather = new WeatherSchedule(level);
    }

    private sealed class State
    {
        public double Time;
        public double Fuel;
        public int ActiveTyreId;
        public bool InLimp;
        public bool InCrawl;
        public double Speed; // speed entering the next segment
        public readonly Dictionary<int, double> Degradation = new();

        public double DegOf(int id) => Degradation.TryGetValue(id, out var d) ? d : 0.0;
        public void AddDeg(int id, double delta) => Degradation[id] = DegOf(id) + delta;
    }

    public RaceResult Simulate(RacePlan plan)
    {
        var result = new RaceResult();
        var car = _level.Car;
        var state = new State
        {
            Fuel = car.InitialFuel,
            ActiveTyreId = plan.InitialTyreId,
            Speed = 0.0,
        };

        var planByLap = new Dictionary<int, LapPlan>();
        foreach (var lp in plan.Laps) planByLap[lp.Lap] = lp;

        for (var lap = 1; lap <= _level.Race.Laps; lap++)
        {
            if (!planByLap.TryGetValue(lap, out var lapPlan))
                throw new InvalidOperationException($"Plan is missing lap {lap}.");

            var actions = new Dictionary<int, SegmentAction>();
            foreach (var a in lapPlan.Segments) actions[a.Id] = a;

            var lapStart = state.Time;

            foreach (var seg in _level.Track.Segments)
            {
                actions.TryGetValue(seg.Id, out var action);
                if (seg.Type == SegmentType.Straight)
                    SimulateStraight(seg, action, state, result);
                else
                    SimulateCorner(seg, state, result);
            }

            ApplyPit(lapPlan.Pit, state, result);
            result.LapTimes.Add(state.Time - lapStart);
        }

        result.TotalTime = state.Time;
        result.FuelRemaining = state.Fuel;
        result.TotalFuelUsed = car.InitialFuel - state.Fuel; // refuel reduces "used" via ApplyPit
        result.TotalDegradation = state.Degradation.Values.Sum();
        return result;
    }

    private void SimulateStraight(Segment seg, SegmentAction? action, State state, RaceResult result)
    {
        // Entering a straight ends crawl mode (acceleration is possible again).
        if (state.InCrawl) { state.InCrawl = false; }

        var car = _level.Car;
        var w = _weather.At(state.Time);
        var accelEff = car.Accel * w.AccelerationMultiplier;
        var degRate = _opt.EnableDegradation ? _level.PropertiesOfId(state.ActiveTyreId).DegradationRate(w.Kind) : 0.0;

        double endSpeed;
        var phases = new List<(double vi, double vf, double dist)>();

        if (state.InLimp)
        {
            // Constant limp speed, no accel/brake.
            phases.Add((car.LimpSpeed, car.LimpSpeed, seg.Length));
            endSpeed = car.LimpSpeed;
        }
        else
        {
            var brakeEff = car.Brake * w.DecelerationMultiplier;
            var vIn = state.Speed;
            var target = Math.Min(action?.TargetSpeed ?? vIn, car.MaxSpeed);
            var dBrake = Math.Clamp(action?.BrakeStartBeforeNext ?? 0.0, 0.0, seg.Length);
            var regionA = seg.Length - dBrake;

            // Phase A: accelerate toward target, then cruise (or follow-through hold).
            double vAtBrake;
            if (target > vIn + 1e-12)
            {
                var dAcc = Kinematics.DistanceForSpeedChange(vIn, target, accelEff);
                if (dAcc >= regionA)
                {
                    var vEndA = Math.Min(Kinematics.SpeedAfterDistance(vIn, accelEff, regionA), car.MaxSpeed);
                    phases.Add((vIn, vEndA, regionA));
                    vAtBrake = vEndA;
                }
                else
                {
                    phases.Add((vIn, target, dAcc));
                    phases.Add((target, target, regionA - dAcc));
                    vAtBrake = target;
                }
            }
            else
            {
                // Follow-through: hold entry speed across the non-braking region.
                phases.Add((vIn, vIn, regionA));
                vAtBrake = vIn;
            }

            // Phase B: braking.
            if (dBrake > 0)
            {
                var vEnd = Math.Max(Kinematics.SpeedAfterDistance(vAtBrake, -brakeEff, dBrake), car.CrawlSpeed);
                phases.Add((vAtBrake, vEnd, dBrake));
                endSpeed = vEnd;
            }
            else
            {
                endSpeed = vAtBrake;
            }

            // Tyre wear: full-length straight term + braking term.
            if (_opt.EnableDegradation)
            {
                var deg = TyreModel.StraightDegradation(degRate, seg.Length);
                if (dBrake > 0) deg += TyreModel.BrakingDegradation(degRate, vAtBrake, endSpeed);
                ApplyWear(state, result, deg);
            }
        }

        // Time + fuel across all phases.
        foreach (var (vi, vf, dist) in phases)
        {
            if (dist <= 0) continue;
            var avg = (vi + vf) / 2.0;
            if (avg <= 0) continue;
            state.Time += dist / avg;
            ConsumeFuel(state, FuelModel.Used(car.FuelKBase, vi, vf, dist), result);
        }

        state.Speed = endSpeed;
    }

    private void SimulateCorner(Segment seg, State state, RaceResult result)
    {
        var car = _level.Car;
        var w = _weather.At(state.Time);
        var radius = seg.Radius ?? throw new InvalidOperationException($"Corner {seg.Id} has no radius.");
        var kind = w.Kind;
        var tyre = _level.PropertiesOfId(state.ActiveTyreId);
        var degRate = _opt.EnableDegradation ? tyre.DegradationRate(kind) : 0.0;

        double speed;
        if (state.InLimp)
        {
            speed = car.LimpSpeed;
        }
        else if (state.InCrawl)
        {
            speed = car.CrawlSpeed;
        }
        else
        {
            var totalDeg = _opt.EnableDegradation ? state.DegOf(state.ActiveTyreId) : 0.0;
            var friction = TyreModel.Friction(tyre, totalDeg, kind);
            var safe = TyreModel.SafeCornerSpeed(friction, radius);

            if (state.Speed > safe + _opt.CrashEpsilon)
            {
                // Crash: time penalty, flat wear, enter crawl mode, traverse this corner at crawl.
                result.CrashCount++;
                result.EverCrawl = true;
                state.InCrawl = true;
                state.Time += _level.Race.CornerCrashPenalty;
                if (_opt.EnableDegradation) ApplyWear(state, result, PhysicsConstants.CrashTyrePenalty);
                speed = car.CrawlSpeed;
            }
            else
            {
                speed = state.Speed;
            }
        }

        if (state.InLimp) result.EverLimp = true;
        if (state.InCrawl) result.EverCrawl = true;

        // Time + fuel for the corner (constant speed over its length).
        if (speed > 0)
        {
            state.Time += seg.Length / speed;
            ConsumeFuel(state, FuelModel.Used(car.FuelKBase, speed, speed, seg.Length), result);
        }

        if (_opt.EnableDegradation && !state.InLimp)
            ApplyWear(state, result, TyreModel.CornerDegradation(degRate, speed, radius));

        state.Speed = speed;
    }

    private void ApplyPit(PitAction pit, State state, RaceResult result)
    {
        if (pit is null || !pit.Enter)
        {
            // No pit: speed simply carries into next lap's first segment.
            return;
        }

        var race = _level.Race;
        var pitTime = race.BasePitStopTime;

        var changingTyres = pit.TyreChangeSetId is int newId && newId > 0;
        if (changingTyres)
        {
            pitTime += race.PitTyreSwapTime;
            state.ActiveTyreId = pit.TyreChangeSetId!.Value;
        }

        var refuel = pit.FuelRefuelAmount ?? 0.0;
        if (refuel > 0)
        {
            var space = _level.Car.FuelCapacity - state.Fuel;
            var added = Math.Min(refuel, Math.Max(0, space));
            pitTime += refuel / race.PitRefuelRate; // time charged on the requested amount
            state.Fuel += added;
        }

        // A pit stop fixes the cause of limp mode (blowout fixed by a tyre change,
        // empty tank fixed by refuelling).
        state.InLimp = false;
        state.InCrawl = false;
        state.Time += pitTime;
        state.Speed = race.PitExitSpeed; // exit pit lane at pit exit speed
    }

    private void ApplyWear(State state, RaceResult result, double delta)
    {
        if (delta <= 0) return;
        var id = state.ActiveTyreId;
        var before = state.DegOf(id);
        var lifeSpan = _level.PropertiesOfId(id).LifeSpan;
        state.AddDeg(id, delta);
        if (before < lifeSpan && state.DegOf(id) >= lifeSpan)
        {
            result.BlowoutCount++;
            state.InLimp = true;
            result.EverLimp = true;
        }
    }

    private void ConsumeFuel(State state, double used, RaceResult result)
    {
        state.Fuel -= used;
        if (state.Fuel <= 0)
        {
            state.Fuel = 0;
            if (_opt.EnableFuelLimp && !state.InLimp)
            {
                state.InLimp = true;
                result.EverLimp = true;
            }
        }
    }
}
