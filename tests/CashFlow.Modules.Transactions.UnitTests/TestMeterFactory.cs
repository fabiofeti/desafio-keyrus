using System.Diagnostics.Metrics;

namespace CashFlow.Modules.Transactions.UnitTests;

/// <summary>
/// Real meters, owned by the test: a MeterListener scoped to them sees only
/// what this test recorded, even when tests run in parallel.
/// </summary>
public sealed class TestMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = [];

    public IReadOnlyList<Meter> Meters => _meters;

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options.Name, options.Version, options.Tags, scope: this);
        _meters.Add(meter);
        return meter;
    }

    /// <summary>Every measurement of <paramref name="instrument"/> recorded while <paramref name="act"/> runs.</summary>
    public async Task<List<(double Value, KeyValuePair<string, object?>[] Tags)>> MeasureAsync(string instrument, Func<Task> act)
    {
        var measurements = new List<(double, KeyValuePair<string, object?>[])>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (i, l) =>
            {
                if (i.Meter.Scope == this && i.Name == instrument)
                {
                    l.EnableMeasurementEvents(i);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) => measurements.Add((value, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) => measurements.Add((value, tags.ToArray())));
        listener.Start();

        await act();

        return measurements;
    }

    public void Dispose()
    {
        foreach (Meter meter in _meters)
        {
            meter.Dispose();
        }
    }
}
