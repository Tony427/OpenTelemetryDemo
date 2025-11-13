using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Logs;

var builder = WebApplication.CreateBuilder(args);

// ============================================
// 1. Define custom Meter and ActivitySource
// ============================================
var meter = new Meter("OpenTelemetryDemo.App", "1.0.0");
var activitySource = new ActivitySource("OpenTelemetryDemo.App");

// Define metrics - demonstrating different metric types
var greetingCounter = meter.CreateCounter<int>("greetings.count",
    description: "Counts greeting requests");
var requestDuration = meter.CreateHistogram<double>("request.duration",
    unit: "ms", description: "Request processing duration");

// NEW: UpDownCounter - tracks current active requests (can increment and decrement)
var activeRequestsCounter = meter.CreateUpDownCounter<int>("active_requests.count",
    description: "Number of currently active requests");

// NEW: ObservableGauge - tracks system memory usage (auto-collected periodically)
var memoryGauge = meter.CreateObservableGauge("system.memory.used",
    () => GC.GetTotalMemory(forceFullCollection: false) / 1024.0 / 1024.0,
    unit: "MB",
    description: "Current application memory usage in MB");

// NEW: ObservableGauge for simulated cache size
var cacheSize = 0;
var cacheSizeGauge = meter.CreateObservableGauge("cache.size",
    () => cacheSize,
    description: "Number of items in cache");

// ============================================
// 2. Configure OpenTelemetry
// ============================================
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: "OpenTelemetryDemo.Api",
                    serviceVersion: "1.0.0"))

    // Configure Tracing (Distributed Tracing)
    .WithTracing(tracing => tracing
        // Auto-instrument ASP.NET Core requests
        .AddAspNetCoreInstrumentation()
        // Auto-instrument HttpClient calls
        .AddHttpClientInstrumentation()
        // Add our custom ActivitySource
        .AddSource(activitySource.Name)
        // Export to Console (for development)
        .AddConsoleExporter()
        .AddOtlpExporter()
        // (Optional) Export to Jaeger/OTLP
        // .AddOtlpExporter(options =>
        //     options.Endpoint = new Uri("http://localhost:4317"))
    )

    // Configure Metrics
    .WithMetrics(metrics => metrics
        // Auto-collect ASP.NET Core metrics
        .AddAspNetCoreInstrumentation()
        // Auto-collect .NET Runtime metrics
        .AddRuntimeInstrumentation()
        // Add our custom Meter
        .AddMeter(meter.Name)
        // Export to Console
        .AddConsoleExporter()
        // Expose Prometheus scraping endpoint
        .AddPrometheusExporter()
    );

// ============================================
// 3. Configure Logging (Structured Logging)
// ============================================
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeScopes = true;
    logging.IncludeFormattedMessage = true;
});

// ============================================
// 4. Add Services
// ============================================
builder.Services.AddControllers();
builder.Services.AddHttpClient();

// 加入 Swagger/OpenAPI 支援
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<OpenTelemetryDemo.Application.Weather.IWeatherService,
    OpenTelemetryDemo.Infrastructure.Weather.OpenMeteoWeatherService>();
// expose telemetry singletons for controllers
builder.Services.AddSingleton(meter);
builder.Services.AddSingleton(activitySource);
builder.Services.AddSingleton(greetingCounter);
builder.Services.AddSingleton(requestDuration);
builder.Services.AddSingleton(activeRequestsCounter);

var app = builder.Build();

// ============================================
// 5. Configure Middleware Pipeline
// ============================================
// Enable Swagger (available in both development and production)
app.UseSwagger();
app.UseSwaggerUI();

// Map attribute-routed controllers
app.MapControllers();

// ============================================
// 6. Expose Prometheus Metrics Endpoint
// ============================================
app.MapPrometheusScrapingEndpoint();

// ============================================
// 7. Run Application
// ============================================
app.Run();

