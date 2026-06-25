using System.Text.Json;

namespace GrandPrix.Domain;

public static class LevelLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Level Load(string path)
    {
        var json = File.ReadAllText(path);
        return Parse(json);
    }

    public static Level Parse(string json)
    {
        var level = JsonSerializer.Deserialize<Level>(json, Options)
            ?? throw new FormatException("Level JSON deserialized to null.");
        Validate(level);
        return level;
    }

    private static void Validate(Level level)
    {
        if (level.Car is null) throw new FormatException("Level is missing 'car'.");
        if (level.Race is null) throw new FormatException("Level is missing 'race'.");
        if (level.Track is null || level.Track.Segments.Count == 0)
            throw new FormatException("Level is missing 'track.segments'.");
        if (level.Tyres is null || level.Tyres.Properties.Count == 0)
            throw new FormatException("Level is missing 'tyres.properties'.");
        if (level.Weather is null || level.Weather.Conditions.Count == 0)
            throw new FormatException("Level is missing 'weather.conditions'.");
    }
}

public static class LevelExtensions
{
    /// <summary>Resolve the compound of a physical tyre set by its id.</summary>
    public static Compound CompoundOf(this Level level, int tyreId)
    {
        foreach (var set in level.AvailableSets)
            if (set.Ids.Contains(tyreId))
                return set.Compound;
        throw new ArgumentException($"Tyre id {tyreId} is not in any available set.");
    }

    /// <summary>Tyre property block for a compound (matches the dictionary key, case-insensitive).</summary>
    public static TyreProperties PropertiesOf(this Level level, Compound compound)
    {
        foreach (var kv in level.Tyres.Properties)
            if (kv.Key.Equals(compound.ToString(), StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        throw new ArgumentException($"No tyre properties for compound {compound}.");
    }

    public static TyreProperties PropertiesOfId(this Level level, int tyreId)
        => level.PropertiesOf(level.CompoundOf(tyreId));

    public static WeatherCondition StartingWeather(this Level level)
    {
        foreach (var c in level.Weather.Conditions)
            if (c.Id == level.Race.StartingWeatherConditionId)
                return c;
        return level.Weather.Conditions[0];
    }
}
