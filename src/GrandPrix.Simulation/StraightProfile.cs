namespace GrandPrix.Simulation;

/// <summary>
/// Normal-case (non-limp) straight kinematics: accelerate toward target, cruise, then brake.
/// Shared by the simulator and the optimizer's planner so their time-keeping is identical by
/// construction (essential for weather look-ups to line up).
/// </summary>
public static class StraightProfile
{
    public readonly record struct Phase(double Vi, double Vf, double Dist);

    public readonly record struct Result(IReadOnlyList<Phase> Phases, double EndSpeed, double SpeedAtBrake);

    public static Result Compute(
        double vIn, double target, double brakeStart, double length,
        double accelEff, double brakeEff, double maxSpeed, double crawlSpeed)
    {
        target = Math.Min(target, maxSpeed);
        var dBrake = Math.Clamp(brakeStart, 0.0, length);
        var regionA = length - dBrake;
        var phases = new List<Phase>(3);

        double vAtBrake;
        if (target > vIn + 1e-12)
        {
            var dAcc = Kinematics.DistanceForSpeedChange(vIn, target, accelEff);
            if (dAcc >= regionA)
            {
                var vEndA = Math.Min(Kinematics.SpeedAfterDistance(vIn, accelEff, regionA), maxSpeed);
                phases.Add(new Phase(vIn, vEndA, regionA));
                vAtBrake = vEndA;
            }
            else
            {
                phases.Add(new Phase(vIn, target, dAcc));
                phases.Add(new Phase(target, target, regionA - dAcc));
                vAtBrake = target;
            }
        }
        else
        {
            // Follow-through: hold entry speed across the non-braking region.
            phases.Add(new Phase(vIn, vIn, regionA));
            vAtBrake = vIn;
        }

        double endSpeed;
        if (dBrake > 0)
        {
            var vEnd = Math.Max(Kinematics.SpeedAfterDistance(vAtBrake, -brakeEff, dBrake), crawlSpeed);
            phases.Add(new Phase(vAtBrake, vEnd, dBrake));
            endSpeed = vEnd;
        }
        else
        {
            endSpeed = vAtBrake;
        }

        return new Result(phases, endSpeed, vAtBrake);
    }

    public static double Time(IReadOnlyList<Phase> phases)
    {
        var t = 0.0;
        foreach (var p in phases)
        {
            if (p.Dist <= 0) continue;
            var avg = (p.Vi + p.Vf) / 2.0;
            if (avg <= 0) continue;
            t += p.Dist / avg;
        }
        return t;
    }
}
