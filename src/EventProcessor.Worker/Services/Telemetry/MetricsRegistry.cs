using Prometheus;

namespace EventProcessor.Worker.Services.Telemetry;

public static class MetricsRegistry
{
    // Counters
    public static readonly Counter EventsProcessed = Metrics.CreateCounter(
        "eventprocessor_events_processed_total",
        "Total de eventos procesados exitosamente",
        new CounterConfiguration { LabelNames = new[] { "camera_ip", "event_type" } });

    public static readonly Counter EventsFailed = Metrics.CreateCounter(
        "eventprocessor_events_failed_total",
        "Total de eventos que fallaron en procesamiento",
        new CounterConfiguration { LabelNames = new[] { "camera_ip", "reason" } });

    public static readonly Counter JsonExports = Metrics.CreateCounter(
        "eventprocessor_json_exports_total",
        "Total de archivos JSON exportados");

    // Histogram (latency)
    public static readonly Histogram EventProcessingDuration = Metrics.CreateHistogram(
        "eventprocessor_event_processing_seconds",
        "Tiempo de procesamiento por evento (segundos)",
        new HistogramConfiguration
        {
            Buckets = Histogram.LinearBuckets(start: 0.01, width: 0.05, count: 20),
            LabelNames = new[] { "camera_ip", "event_type" }
        });

    // Gauge example
    public static readonly Gauge WorkerHeartbeat = Metrics.CreateGauge(
        "eventprocessor_worker_heartbeat",
        "Último latido del worker (timestamp unix)");
}
