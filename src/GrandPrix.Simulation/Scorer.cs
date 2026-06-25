using GrandPrix.Domain;

namespace GrandPrix.Simulation;

/// <summary>Scoring (SPECIFICATION.md §10). Level number selects which bonus terms apply.</summary>
public static class Scorer
{
    public static double BaseScore(double time)
        => time <= 0 ? 0 : 1_000_000_000.0 / time;

    public static double FuelBonus(double fuelUsed, double softCap)
    {
        if (softCap <= 0) return 0;
        var ratio = fuelUsed / softCap;
        var t = 1.0 - ratio;
        return -1_000_000.0 * (t * t) + 1_000_000.0;
    }

    public static double TyreBonus(double totalDegradation, int blowouts)
        => 100_000.0 * totalDegradation - 50_000.0 * blowouts;

    public static double Score(Level level, RaceResult result, int levelNumber)
    {
        var score = BaseScore(result.TotalTime);
        if (levelNumber >= 2)
            score += FuelBonus(result.TotalFuelUsed, level.Race.FuelSoftCapLimit);
        if (levelNumber >= 4)
            score += TyreBonus(result.TotalDegradation, result.BlowoutCount);
        return score;
    }
}
