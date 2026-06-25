using GrandPrix.Domain;

namespace GrandPrix.Simulation;

/// <summary>
/// Resolves the active weather condition at a given cumulative race time.
/// Conditions run in list order starting from <c>starting_weather_condition_id</c>,
/// each for its <c>duration_s</c>, then cycle (SPECIFICATION.md §3.6, Q4).
/// </summary>
public sealed class WeatherSchedule
{
    private readonly List<WeatherCondition> _order;
    private readonly double[] _cumEnd; // cumulative end time of each condition within one cycle
    private readonly double _cycleLength;

    public WeatherSchedule(Level level)
    {
        var conditions = level.Weather.Conditions;
        var startIndex = conditions.FindIndex(c => c.Id == level.Race.StartingWeatherConditionId);
        if (startIndex < 0) startIndex = 0;

        // Build the cyclic order beginning at the starting condition.
        _order = new List<WeatherCondition>(conditions.Count);
        for (var i = 0; i < conditions.Count; i++)
            _order.Add(conditions[(startIndex + i) % conditions.Count]);

        _cumEnd = new double[_order.Count];
        var acc = 0.0;
        for (var i = 0; i < _order.Count; i++)
        {
            acc += _order[i].DurationS;
            _cumEnd[i] = acc;
        }
        _cycleLength = acc;
    }

    public WeatherCondition At(double time)
    {
        if (_cycleLength <= 0) return _order[0];
        var t = time % _cycleLength;
        if (t < 0) t += _cycleLength;
        for (var i = 0; i < _cumEnd.Length; i++)
            if (t < _cumEnd[i])
                return _order[i];
        return _order[^1];
    }

    public WeatherKind KindAt(double time) => At(time).Kind;
}
