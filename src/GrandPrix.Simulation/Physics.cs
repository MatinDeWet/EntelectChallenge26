using GrandPrix.Domain;

namespace GrandPrix.Simulation;

/// <summary>Pure kinematic helpers (SPECIFICATION.md §4.6). All SI units.</summary>
public static class Kinematics
{
    /// <summary>Distance to change speed from vi to vf at constant accel a (a &gt; 0). |vf²-vi²|/(2a).</summary>
    public static double DistanceForSpeedChange(double vi, double vf, double a)
        => Math.Abs(vf * vf - vi * vi) / (2.0 * a);

    /// <summary>Time to change speed from vi to vf at constant accel a (a &gt; 0).</summary>
    public static double TimeForSpeedChange(double vi, double vf, double a)
        => Math.Abs(vf - vi) / a;

    /// <summary>Final speed after accelerating from vi over distance d at accel a (a may be negative for braking).</summary>
    public static double SpeedAfterDistance(double vi, double a, double d)
    {
        var v2 = vi * vi + 2.0 * a * d;
        return v2 <= 0 ? 0 : Math.Sqrt(v2);
    }
}

/// <summary>Fuel model (SPECIFICATION.md §6).</summary>
public static class FuelModel
{
    /// <summary>Fuel used travelling <paramref name="distance"/> m from vi to vf.</summary>
    public static double Used(double kBase, double vi, double vf, double distance)
    {
        var avg = (vi + vf) / 2.0;
        return (kBase + PhysicsConstants.KDrag * avg * avg) * distance;
    }
}

/// <summary>Tyre friction and degradation (SPECIFICATION.md §5).</summary>
public static class TyreModel
{
    /// <summary>tyre_friction = (base_friction − total_degradation) × weather_multiplier.</summary>
    public static double Friction(TyreProperties tyre, double totalDegradation, WeatherKind weather)
        => (tyre.BaseFriction - totalDegradation) * tyre.FrictionMultiplier(weather);

    /// <summary>Safe maximum corner speed = sqrt(friction · g · radius). (SPEC Q2: plain form.)</summary>
    public static double SafeCornerSpeed(double friction, double radius)
    {
        var v2 = friction * PhysicsConstants.G * radius;
        return v2 <= 0 ? 0 : Math.Sqrt(v2);
    }

    public static double StraightDegradation(double degRate, double length)
        => degRate * length * PhysicsConstants.KStraight;

    public static double BrakingDegradation(double degRate, double vInitial, double vFinal)
    {
        var a = vInitial / 100.0;
        var b = vFinal / 100.0;
        return (a * a - b * b) * PhysicsConstants.KBraking * degRate;
    }

    public static double CornerDegradation(double degRate, double speed, double radius)
        => PhysicsConstants.KCorner * (speed * speed / radius) * degRate;
}
