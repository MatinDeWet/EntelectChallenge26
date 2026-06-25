using GrandPrix.Domain;
using GrandPrix.Optimization;
using GrandPrix.Simulation;
using Xunit;

namespace GrandPrix.Tests;

public class Level1Tests
{
    private static (Level level, RacePlan plan, RaceResult result) Run()
    {
        var level = LevelLoader.Load(TestPaths.Level(1));
        var plan = new Level1Optimizer().Optimize(level);
        var sim = new RaceSimulator(level, SimulationOptions.ForLevel(1));
        var result = sim.Simulate(plan);
        return (level, plan, result);
    }

    [Fact]
    public void Plan_covers_every_lap_and_segment()
    {
        var (level, plan, _) = Run();
        Assert.Equal(level.Race.Laps, plan.Laps.Count);
        foreach (var lap in plan.Laps)
            Assert.Equal(level.Track.Segments.Count, lap.Segments.Count);
    }

    [Fact]
    public void No_crash_blowout_or_limp()
    {
        var (_, _, result) = Run();
        Assert.Equal(0, result.CrashCount);
        Assert.Equal(0, result.BlowoutCount);
        Assert.False(result.EverLimp);
        Assert.False(result.EverCrawl);
    }

    [Fact]
    public void Starts_on_soft_the_highest_friction_tyre()
    {
        var (level, plan, _) = Run();
        Assert.Equal(Compound.Soft, level.CompoundOf(plan.InitialTyreId));
    }

    [Fact]
    public void Fuel_is_sufficient_for_the_race()
    {
        var (level, _, result) = Run();
        Assert.True(result.TotalFuelUsed <= level.Car.InitialFuel,
            $"Used {result.TotalFuelUsed:F2}L but only had {level.Car.InitialFuel:F2}L.");
    }

    [Fact]
    public void Speeds_stay_within_bounds()
    {
        var (level, plan, _) = Run();
        foreach (var lap in plan.Laps)
            foreach (var seg in lap.Segments.Where(s => s.Type == "straight"))
            {
                Assert.True(seg.TargetSpeed <= level.Car.MaxSpeed + 1e-9);
                Assert.True(seg.TargetSpeed >= 0);
                Assert.NotNull(seg.BrakeStartBeforeNext);
            }
    }

    [Fact]
    public void Output_is_deterministic()
    {
        var level = LevelLoader.Load(TestPaths.Level(1));
        var a = new Level1Optimizer().Optimize(level).ToJson();
        var b = new Level1Optimizer().Optimize(level).ToJson();
        Assert.Equal(a, b);
    }
}
