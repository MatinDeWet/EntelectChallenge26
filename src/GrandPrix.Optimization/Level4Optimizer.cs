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

    public double CornerSafetyMargin { get; init; } = 0.01;
    public double TyreChangeThreshold { get; init; } = 0.84;

    /// <summary>0 = always fit the fastest tyre; higher biases toward high-degradation compounds
    /// to grow the tyre-wear bonus (at some cost to lap time). The tyre-bonus (100k per unit of
    /// Σ wear) appears to outweigh the lap-time cost of wear, so we bias toward more wear.</summary>
    public double WearWeight { get; init; } = 1.5;

    public RacePlan Optimize(Level level)
        => new DegradationAwarePlanner(level, CornerSafetyMargin, TyreChangeThreshold, WearWeight).Build();
}
