using System.Diagnostics.Metrics;
using Inkstone.Web.Services;

namespace Inkstone.Api.Tests;

public sealed class ConverterOptionsTests
{
    [Fact]
    public void TimeoutFor_selects_preflight_or_conversion_budget()
    {
        var options = new ConverterCapacityOptions { PreflightTimeoutSeconds = 15, TimeoutSeconds = 90 };
        Assert.Equal(15, options.TimeoutFor(true));
        Assert.Equal(90, options.TimeoutFor(false));
    }

    [Fact]
    public void Conversion_telemetry_emits_pool_and_exit_labels_only()
    {
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meter) =>
            {
                if (instrument.Meter.Name == "Inkstone.Conversion")
                    meter.EnableMeasurementEvents(instrument);
            }
        };
        var events = new List<(string Name, long Value, string Pool, string Exit)>();
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            string pool = "", exit = "";
            foreach (var tag in tags)
            {
                if (tag.Key == "pool") pool = tag.Value?.ToString() ?? "";
                if (tag.Key == "exit_code") exit = tag.Value?.ToString() ?? "";
            }
            events.Add((instrument.Name, measurement, pool, exit));
        });
        listener.Start();

        ConversionTelemetry.RecordTimeout(true);
        ConversionTelemetry.RecordFailure(false, exitCode: 2);
        ConversionTelemetry.RecordFailure(false, reason: "missing_output");

        listener.RecordObservableInstruments();
        listener.Dispose();

        Assert.Contains(events, e => e.Name == "inkstone.converter.timeouts" && e.Pool == "validation" && e.Exit == "");
        Assert.Contains(events, e => e.Name == "inkstone.converter.failures" && e.Pool == "conversion" && e.Exit == "2");
        Assert.Contains(events, e => e is { Name: "inkstone.converter.failures", Exit: "missing_output" });
        Assert.All(events, e => Assert.DoesNotContain("document", e.Pool, StringComparison.OrdinalIgnoreCase));
    }
}