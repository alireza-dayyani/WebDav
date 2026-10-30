using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Inkstone.Web.Services;

public sealed class ConverterCapacityOptions
{
    public int ValidationSlots { get; set; } = 2;
    public int ConversionSlots { get; set; } = 2;
}

/// <summary>Exports cannot occupy permits reserved for native save validation.</summary>
public sealed class ConverterCapacity(IOptions<ConverterCapacityOptions> options) : IDisposable
{
    private static readonly Meter Meter = new("Inkstone.Conversion");
    private static readonly Histogram<double> Wait = Meter.CreateHistogram<double>("inkstone.converter.queue_wait", "ms");
    private static readonly UpDownCounter<long> Active = Meter.CreateUpDownCounter<long>("inkstone.converter.active");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("inkstone.converter.rejected");
    private readonly SemaphoreSlim validation = new(options.Value.ValidationSlots, options.Value.ValidationSlots);
    private readonly SemaphoreSlim conversion = new(options.Value.ConversionSlots, options.Value.ConversionSlots);

    public async Task<IDisposable> EnterAsync(bool preflight, CancellationToken ct)
    {
        var pool = preflight ? validation : conversion;
        var tag = new KeyValuePair<string, object?>("pool", preflight ? "validation" : "conversion");
        var started = Stopwatch.GetTimestamp();
        bool acquired;
        try { acquired = await pool.WaitAsync(TimeSpan.FromSeconds(5), ct); }
        finally { Wait.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tag); }
        if (!acquired)
        {
            Rejected.Add(1, tag);
            throw new ConversionException("The converter is busy. Try again shortly.", 429, "converter_busy");
        }
        Active.Add(1, tag);
        return new Permit(pool, tag);
    }

    private sealed class Permit(SemaphoreSlim pool, KeyValuePair<string, object?> tag) : IDisposable
    {
        private int released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0) { Active.Add(-1, tag); pool.Release(); }
        }
    }

    public void Dispose() { validation.Dispose(); conversion.Dispose(); }
}
