using GrandPrix.Domain;

namespace GrandPrix.Optimization;

/// <summary>
/// Schedules the minimum number of refuel-only pit stops so the car never runs dry.
/// Refuels to full early, tops up the final stint just enough to finish. Fuel burn is
/// essentially distance-fixed (speed-independent base term), so this is robust to weather.
/// </summary>
public static class PitScheduler
{
    /// <param name="lapFuel">True flat-out fuel consumption per lap (measured with fuel-limp disabled).</param>
    /// <returns>Map of lap number ⇒ refuel litres at that lap's end.</returns>
    public static Dictionary<int, double> ScheduleRefuels(Level level, IReadOnlyList<double> lapFuel, double safetyBuffer)
    {
        var capacity = level.Car.FuelCapacity;
        var n = level.Race.Laps;
        var pits = new Dictionary<int, double>();

        // Suffix sums: suffix[lap] = fuel needed for laps (lap .. n).
        var suffix = new double[n + 2];
        for (var lap = n; lap >= 1; lap--)
            suffix[lap] = suffix[lap + 1] + lapFuel[lap - 1];

        var fuel = level.Car.InitialFuel;
        for (var lap = 1; lap <= n; lap++)
        {
            fuel -= lapFuel[lap - 1]; // burn this lap
            if (lap >= n) break;       // no pit needed after the final lap

            var nextNeed = lapFuel[lap]; // consumption of lap+1
            if (fuel < nextNeed + safetyBuffer)
            {
                var remainingNeed = suffix[lap + 1];
                var topUpToFinish = remainingNeed - fuel + safetyBuffer;
                var refuel = Math.Min(capacity - fuel, Math.Max(0.0, topUpToFinish));
                if (refuel > 0)
                {
                    pits[lap] = refuel;
                    fuel += refuel;
                }
            }
        }

        return pits;
    }
}
