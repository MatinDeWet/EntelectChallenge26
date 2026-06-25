namespace GrandPrix.Optimization;

/// <summary>Time-optimal traversal of a single straight (accelerate as high as feasible, brake to the corner speed).</summary>
public static class StraightSolver
{
    public readonly record struct Solution(double TargetSpeed, double BrakeStart);

    /// <param name="vIn">Entry speed.</param>
    /// <param name="vExit">Required speed at the next corner (already safety-margined).</param>
    /// <param name="length">Straight length (m).</param>
    /// <param name="accel">Effective acceleration (m/s²).</param>
    /// <param name="brake">Effective deceleration (m/s²).</param>
    /// <param name="maxSpeed">Car max speed.</param>
    public static Solution Solve(double vIn, double vExit, double length, double accel, double brake, double maxSpeed)
    {
        // Peak speed if we accelerate then brake with no cruise (triangle profile):
        //   (vp²-vIn²)/(2a) + (vp²-vExit²)/(2b) = L
        var inv = 1.0 / (2.0 * accel) + 1.0 / (2.0 * brake);
        var rhs = length + vIn * vIn / (2.0 * accel) + vExit * vExit / (2.0 * brake);
        var vp = Math.Sqrt(Math.Max(0.0, rhs / inv));

        double target;
        if (vp >= maxSpeed)
            target = maxSpeed;            // trapezoid: cruise at max
        else if (vp <= vIn)
            target = vIn;                 // already faster than the peak: hold then brake (follow-through)
        else
            target = vp;                  // triangle peak

        var brakeStart = (target * target - vExit * vExit) / (2.0 * brake);
        brakeStart = Math.Clamp(brakeStart, 0.0, length);
        return new Solution(target, brakeStart);
    }
}
