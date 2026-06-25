using GrandPrix.Domain;
using Xunit;

namespace GrandPrix.Tests;

public class LevelParsingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void All_levels_parse(int n)
    {
        var level = LevelLoader.Load(TestPaths.Level(n));
        Assert.True(level.Track.Segments.Count > 0);
        Assert.True(level.Race.Laps > 0);
        Assert.Equal(90, level.Car.MaxSpeed);
        Assert.Equal(0.0005, level.Car.FuelKBase);
    }

    [Fact]
    public void Level1_key_fields()
    {
        var level = LevelLoader.Load(TestPaths.Level(1));
        Assert.Equal(50, level.Race.Laps);
        Assert.Equal(15, level.Track.Segments.Count);
        Assert.Equal(7300.0, level.Race.TimeReferenceS);
        // Single dry weather with neutral multipliers.
        Assert.Single(level.Weather.Conditions);
        Assert.Equal(WeatherKind.Dry, level.Weather.Conditions[0].Kind);
        // base_friction is present and per the PDF table.
        Assert.Equal(1.8, level.PropertiesOf(Compound.Soft).BaseFriction);
    }

    [Fact]
    public void Level4_has_multi_set_compounds_and_wet_override()
    {
        var level = LevelLoader.Load(TestPaths.Level(4));
        // Soft has two physical sets [1,2].
        var soft = level.AvailableSets.Single(s => s.Compound == Compound.Soft);
        Assert.Equal(new[] { 1, 2 }, soft.Ids.ToArray());
        // Level 4 overrides Wet base_friction to 1.6 (vs 1.1 elsewhere).
        Assert.Equal(1.6, level.PropertiesOf(Compound.Wet).BaseFriction);
        // Weather cycles through 8 conditions.
        Assert.Equal(8, level.Weather.Conditions.Count);
    }

    [Fact]
    public void Segment_type_and_radius_mapping()
    {
        var level = LevelLoader.Load(TestPaths.Level(1));
        var s1 = level.Track.Segments[0];
        Assert.Equal(SegmentType.Straight, s1.Type);
        Assert.Null(s1.Radius);
        var s2 = level.Track.Segments[1];
        Assert.Equal(SegmentType.Corner, s2.Type);
        Assert.Equal(53, s2.Radius);
    }
}
