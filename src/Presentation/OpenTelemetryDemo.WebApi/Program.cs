using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Logs;

var builder = WebApplication.CreateBuilder(args);

// ============================================
// 1. 定義自訂的 Meter 和 ActivitySource
// ============================================
var meter = new Meter("OpenTelemetryDemo.App", "1.0.0");
var activitySource = new ActivitySource("OpenTelemetryDemo.App");

// 定義指標
var greetingCounter = meter.CreateCounter<int>("greetings.count",
    description: "計算問候次數");
var requestDuration = meter.CreateHistogram<double>("request.duration",
    unit: "ms", description: "請求處理時間");

// ============================================
// 2. 設定 OpenTelemetry
// ============================================
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: "OpenTelemetryDemo.Api",
                    serviceVersion: "1.0.0"))

    // 設定 Tracing (分散式追蹤)
    .WithTracing(tracing => tracing
        // 自動追蹤 ASP.NET Core 請求
        .AddAspNetCoreInstrumentation()
        // 自動追蹤 HttpClient 呼叫
        .AddHttpClientInstrumentation()
        // 加入我們自訂的 ActivitySource
        .AddSource(activitySource.Name)
        // 輸出到 Console (開發用)
        .AddConsoleExporter()
        .AddOtlpExporter()
        // (可選) 輸出到 Jaeger/OTLP
        // .AddOtlpExporter(options =>
        //     options.Endpoint = new Uri("http://localhost:4317"))
    )

    // 設定 Metrics (指標)
    .WithMetrics(metrics => metrics
        // 自動收集 ASP.NET Core 指標
        .AddAspNetCoreInstrumentation()
        // 自動收集 .NET Runtime 指標
        .AddRuntimeInstrumentation()
        // 加入我們自訂的 Meter
        .AddMeter(meter.Name)
        // 輸出到 Console
        .AddConsoleExporter()
        // 暴露 Prometheus 端點
        .AddPrometheusExporter()
    );

// ============================================
// 3. 設定 Logging (結構化日誌)
// ============================================
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeScopes = true;
    logging.IncludeFormattedMessage = true;
});

// ============================================
// 4. 加入服務
// ============================================
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddScoped<OpenTelemetryDemo.Application.Weather.IWeatherService,
    OpenTelemetryDemo.Infrastructure.Weather.OpenMeteoWeatherService>();
// expose telemetry singletons for controllers
builder.Services.AddSingleton(meter);
builder.Services.AddSingleton(activitySource);
builder.Services.AddSingleton(greetingCounter);
builder.Services.AddSingleton(requestDuration);

var app = builder.Build();

// Map attribute-routed controllers
app.MapControllers();

// ============================================
// 6. 暴露 Prometheus 指標端點
// ============================================
app.MapPrometheusScrapingEndpoint();

// ============================================
// 7. 啟動應用程式
// ============================================
app.Run();

