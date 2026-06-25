using GrandPrix.Domain;

namespace GrandPrix.Optimization;

/// <summary>
/// Level 4: full problem — tyre degradation, limited multi-set tyres, weather and fuel. Uses the
/// degradation-and-weather-aware planner, which adapts speeds to wear, swaps to the
/// weather-appropriate compound before blowouts, and folds refuels into pit stops.
/// </summary>
public sealed class Level4Optimizer : ILevelOptimizer
{
    public int Level => 4;

    public double CornerSafetyMargin { get; init; } = 0.007;
    public double TyreChangeThreshold { get; init; } = 0.92;

    /// <summary>0 = always fit the fastest tyre; higher biases toward high-degradation compounds
    /// to grow the tyre-wear bonus. Paired submissions (v1 wear 6.71 vs v2 wear 7.18) showed the
    /// score FELL with more wear: time dominates (~−150 to −190 per second) and chasing wear costs
    /// far more time than the bonus is worth. So minimise time — keep wear bias at 0.</summary>
    public double WearWeight { get; init; } = 0.0;

    public RacePlan Optimize(Level level)
        => new DegradationAwarePlanner(level, CornerSafetyMargin, TyreChangeThreshold, WearWeight).Build();
}
