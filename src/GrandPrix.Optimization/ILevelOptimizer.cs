using GrandPrix.Domain;

namespace GrandPrix.Optimization;

/// <summary>A deterministic optimizer: same level in ⇒ same plan out.</summary>
public interface ILevelOptimizer
{
    int Level { get; }
    RacePlan Optimize(Level level);
}

public static class OptimizerRegistry
{
    public static ILevelOptimizer For(int level) => level switch
    {
        1 => new Level1Optimizer(),
        2 => new Level2Optimizer(),
        3 => new Level3Optimizer(),
        4 => new Level4Optimizer(),
        _ => throw new NotSupportedException($"No optimizer implemented for level {level} yet."),
    };
}
