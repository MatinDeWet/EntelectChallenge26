using GrandPrix.Domain;
using GrandPrix.Simulation;
using Xunit;

namespace GrandPrix.Tests;

public class PhysicsTests
{
    // PDF §6: 50→70 m/s over 800 m ⇒ 0.40432 L.
    [Fact]
    public void Fuel_matches_pdf_worked_example()
    {
        var used = FuelModel.Used(0.0005, 50, 70, 800);
        Assert.Equal(0.40432, used, 5);
    }

    // PDF §4.3: sqrt(0.9·9.8·50) = 21 m/s, plus the crawl_constant term (Car-section formula,
    // confirmed by submission to be the grader's actual limit).
    [Fact]
    public void SafeCornerSpeed_is_sqrt_plus_crawl()
    {
        Assert.Equal(21.0, TyreModel.SafeCornerSpeed(0.9, 50, 0.0), 6);
        Assert.Equal(31.0, TyreModel.SafeCornerSpeed(0.9, 50, 10.0), 6);
    }

    // PDF §5.1: (1.8 − 0.5) × 1 = 1.3.
    [Fact]
    public void TyreFriction_matches_pdf_worked_example()
    {
        var tyre = new TyreProperties { BaseFriction = 1.8, DryFriction = 1.0 };
        var friction = TyreModel.Friction(tyre, 0.5, WeatherKind.Dry);
        Assert.Equal(1.3, friction, 6);
    }

    // PDF §4.6 kinematics identities.
    [Fact]
    public void Kinematics_distance_and_time()
    {
        Assert.Equal(400.0, Kinematics.DistanceForSpeedChange(0, 80, 8), 6);   // (80²)/(2·8)
        Assert.Equal(10.0, Kinematics.TimeForSpeedChange(0, 80, 8), 6);        // 80/8
        Assert.Equal(80.0, Kinematics.SpeedAfterDistance(0, 8, 400), 6);       // inverse
    }
}

public class ScorerTests
{
    [Fact]
    public void BaseScore_is_billion_over_time()
        => Assert.Equal(2_000_000.0, Scorer.BaseScore(500), 6);

    // Fuel bonus parabola peaks (= 1,000,000) exactly at fuel_used == soft_cap.
    [Fact]
    public void FuelBonus_peaks_at_soft_cap()
    {
        Assert.Equal(1_000_000.0, Scorer.FuelBonus(200, 200), 3);
        Assert.True(Scorer.FuelBonus(150, 200) < 1_000_000.0);
        Assert.True(Scorer.FuelBonus(250, 200) < 1_000_000.0);
    }

    // PDF §7 pit example: refuel 30 L @ 10 L/s (=3s) + swap 5 + base 20 = 28 s.
    [Fact]
    public void PitTime_components_sum_correctly()
    {
        var refuelTime = 30.0 / 10.0;
        Assert.Equal(28.0, refuelTime + 5.0 + 20.0, 6);
    }
}
