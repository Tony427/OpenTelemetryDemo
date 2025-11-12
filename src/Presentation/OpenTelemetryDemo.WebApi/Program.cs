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

var app = builder.Build();

// Weather endpoint using IWeatherService (Application layer)
app.MapGet("/weather", async (
    double? lat,
    double? lon,
    OpenTelemetryDemo.Application.Weather.IWeatherService weatherService,
    ILogger<Program> logger) =>
{
    var latitude = lat ?? 25.0330;   // Taipei default
    var longitude = lon ?? 121.5654;

    using var activity = activitySource.StartActivity("GetCurrentWeather");
    activity?.SetTag("weather.latitude", latitude);
    activity?.SetTag("weather.longitude", longitude);

    logger.LogInformation("Fetching weather for {Lat},{Lon}", latitude, longitude);

    var result = await weatherService.GetCurrentAsync(latitude, longitude);
    return Results.Ok(result);
});

// ============================================
// 5. 定義 API 端點
// ============================================

// 簡單的 Hello World 端點
app.MapGet("/", (ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("HandleRootRequest");
    activity?.SetTag("greeting.type", "simple");

    logger.LogInformation("處理根路徑請求");
    greetingCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "/"));

    return "Hello OpenTelemetry!";
});

// 示範自訂 Span 和指標的端點
app.MapGet("/greet/{name}", async (string name, ILogger<Program> logger) =>
{
    var sw = Stopwatch.StartNew();

    // 建立自訂的 Activity (Span)
    using var activity = activitySource.StartActivity("GreetUser", ActivityKind.Internal);
    activity?.SetTag("user.name", name);
    activity?.SetTag("greeting.language", "zh-TW");

    logger.LogInformation("問候使用者 {UserName}", name);

    // 模擬一些處理時間
    await Task.Delay(Random.Shared.Next(50, 200));

    // 記錄事件
    activity?.AddEvent(new ActivityEvent("使用者驗證完成"));

    // 記錄指標
    greetingCounter.Add(1,
        new KeyValuePair<string, object?>("endpoint", "/greet"),
        new KeyValuePair<string, object?>("user", name));

    sw.Stop();
    requestDuration.Record(sw.ElapsedMilliseconds,
        new KeyValuePair<string, object?>("endpoint", "/greet"));

    logger.LogInformation("請求處理完成，耗時 {Duration}ms", sw.ElapsedMilliseconds);

    return new { Message = $"你好，{name}！", Timestamp = DateTime.UtcNow };
});

// 示範錯誤處理和 Exception 記錄
app.MapGet("/error", (ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("ErrorExample");

    try
    {
        logger.LogWarning("即將拋出測試例外");
        throw new InvalidOperationException("這是一個測試錯誤");
    }
    catch (Exception ex)
    {
        // 記錄例外到 Activity
        activity?.RecordException(ex);
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

        // 記錄到日誌
        logger.LogError(ex, "處理請求時發生錯誤");

        return Results.Problem(
            title: "發生錯誤",
            detail: ex.Message,
            statusCode: 500
        );
    }
});

// 示範呼叫外部 API (追蹤 HTTP Client)
app.MapGet("/external", async (IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("CallExternalApi");

    var client = httpClientFactory.CreateClient();
    logger.LogInformation("呼叫外部 API");

    try
    {
        var response = await client.GetStringAsync("https://api.github.com/repos/open-telemetry/opentelemetry-dotnet");
        logger.LogInformation("外部 API 呼叫成功");

        return Results.Ok(new { Status = "Success", ResponseLength = response.Length });
    }
    catch (Exception ex)
    {
        activity?.RecordException(ex);
        logger.LogError(ex, "外部 API 呼叫失敗");
        return Results.Problem("外部 API 呼叫失敗");
    }
});

// ============================================
// 6. 暴露 Prometheus 指標端點
// ============================================
app.MapPrometheusScrapingEndpoint();

// ============================================
// 7. 啟動應用程式
// ============================================
app.Run();

