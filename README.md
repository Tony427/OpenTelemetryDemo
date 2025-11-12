# OpenTelemetry .NET
**參考文件**: [Microsoft .NET Observability with OpenTelemetry](https://learn.microsoft.com/dotnet/core/diagnostics/observability-prgrja-example)

---

## 快速開始

### 前置需求

- .NET 6.0+ SDK
- (可選) Docker Desktop - 用於執行 Jaeger

### 建立專案

```bash
# 1. 建立 Web API 專案
dotnet new webapi -n OpenTelemetryDemo -o .
dotnet new sln -n OpenTelemetryDemo

# 2. 加入專案到方案
dotnet sln add OpenTelemetryDemo.csproj

# 3. 安裝必要套件
dotnet add package OpenTelemetry.Extensions.Hosting --version 1.7.0
dotnet add package OpenTelemetry.Instrumentation.AspNetCore --version 1.7.1
dotnet add package OpenTelemetry.Instrumentation.Http --version 1.7.1
dotnet add package OpenTelemetry.Exporter.Console --version 1.7.0
dotnet add package OpenTelemetry.Exporter.Prometheus.AspNetCore --version 1.7.0-rc.1

# 4. (可選) 用於 Jaeger 輸出
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol --version 1.7.0
```

---

## 專案結構

```
OpenTelemetryDemo/
├── Program.cs                    # 所有設定都在這裡
├── OpenTelemetryDemo.csproj      # 專案檔
├── appsettings.json              # (可選) 基本設定
└── docker-compose.yml            # (可選) Jaeger 容器
```

---

## 核心實作

### Step 1: Program.cs

```csharp
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

var app = builder.Build();

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
```

### Step 2: appsettings.json (可選)

如果你想透過設定檔控制日誌層級：

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "OpenTelemetry": "Information"
    }
  },
  "AllowedHosts": "*"
}
```

---

## 測試與驗證

### 1. 執行應用程式

```bash
dotnet run
```

應用程式會在 `http://localhost:5000` 或 `https://localhost:5001` 啟動。

### 2. 測試端點

開啟另一個終端機視窗：

```bash
# 測試基本端點
curl http://localhost:5000/

# 測試自訂 Tracing 和 Metrics
curl http://localhost:5000/greet/John
curl http://localhost:5000/greet/Mary

# 測試錯誤處理
curl http://localhost:5000/error

# 測試外部 API 呼叫
curl http://localhost:5000/external

# 查看 Prometheus 指標
curl http://localhost:5000/metrics
```

### 3. 觀察 Console 輸出

執行應用程式的終端機中，你會看到：

**Tracing 輸出範例**：
```
Activity.TraceId:            8c9f2e1a4b3d6c5e7f8g9h0i1j2k3l4m
Activity.SpanId:             7f8g9h0i1j2k
Activity.TraceFlags:         Recorded
Activity.ParentSpanId:       6c5e7f8g9h0i
Activity.ActivitySourceName: OpenTelemetryDemo.App
Activity.DisplayName:        GreetUser
Activity.Kind:               Internal
Activity.StartTime:          2025-11-12T10:30:00.0000000Z
Activity.Duration:           00:00:00.1234567
Activity.Tags:
    user.name: John
    greeting.language: zh-TW
Activity.Events:
    使用者驗證完成 [2025-11-12T10:30:00.0500000Z]
```

**Metrics 輸出範例**：
```
Metric: greetings.count
Value: 5
Tags:
    endpoint: /greet
    user: John

Metric: request.duration
Value: 123.45
Tags:
    endpoint: /greet
```

**Logging 輸出範例**：
```
info: Program[0]
      問候使用者 John
      TraceId: 8c9f2e1a4b3d6c5e7f8g9h0i1j2k3l4m
      SpanId: 7f8g9h0i1j2k
```

### 4. 查看 Prometheus 指標

訪問 `http://localhost:5000/metrics`，你會看到 Prometheus 格式的指標：

```
# HELP greetings_count 計算問候次數
# TYPE greetings_count counter
greetings_count{endpoint="/greet",user="John"} 1

# HELP request_duration 請求處理時間
# TYPE request_duration histogram
request_duration_bucket{endpoint="/greet",le="50"} 0
request_duration_bucket{endpoint="/greet",le="100"} 1
request_duration_bucket{endpoint="/greet",le="+Inf"} 1
request_duration_sum{endpoint="/greet"} 123.45
request_duration_count{endpoint="/greet"} 1
```

---

## (可選) 使用 Jaeger 視覺化 Traces

### 1. 啟動 Jaeger

建立 `docker-compose.yml`：

```yaml
version: '3.8'

services:
  jaeger:
    image: jaegertracing/all-in-one:latest
    container_name: jaeger
    environment:
      - COLLECTOR_OTLP_ENABLED=true
    ports:
      - "16686:16686"  # Jaeger UI
      - "4317:4317"    # OTLP gRPC receiver
      - "4318:4318"    # OTLP HTTP receiver
```

啟動 Jaeger：

```bash
docker-compose up -d
```

### 2. 修改 Program.cs

取消註解 OTLP Exporter：

```csharp
.WithTracing(tracing => tracing
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddSource(activitySource.Name)
    // .AddConsoleExporter()  // 可以註解掉 Console，只用 Jaeger
    .AddOtlpExporter(options =>
        options.Endpoint = new Uri("http://localhost:4317"))
)
```

### 3. 重新執行應用程式

```bash
dotnet run
```

### 4. 產生一些流量

```bash
for i in {1..10}; do
  curl http://localhost:5000/greet/User$i
done
```

### 5. 開啟 Jaeger UI

訪問 `http://localhost:16686`

1. 在 "Service" 下拉選單選擇 "OpenTelemetryDemo.Api"
2. 點擊 "Find Traces"
3. 點擊任何一個 Trace 來查看詳細資訊

你會看到：
- 完整的請求流程
- 每個 Span 的時間和標籤
- 父子關係
- 錯誤和例外

---

### 1. 三大支柱

| 支柱 | 用途 | 實作方式 |
|------|------|----------|
| **Tracing** | 追蹤請求流程 | `ActivitySource` + `Activity` |
| **Metrics** | 量化系統行為 | `Meter` + `Counter`/`Histogram` |
| **Logging** | 事件記錄 | `ILogger` + OpenTelemetry 整合 |

### 2. 自動 vs 手動 Instrumentation

**自動 Instrumentation** (零程式碼):
```csharp
.AddAspNetCoreInstrumentation()  // 自動追蹤所有 HTTP 請求
.AddHttpClientInstrumentation()  // 自動追蹤 HttpClient 呼叫
```

**手動 Instrumentation** (自訂邏輯):
```csharp
using var activity = activitySource.StartActivity("MyOperation");
activity?.SetTag("custom.key", "value");
greetingCounter.Add(1);
```

### 3. 關聯性 (Correlation)

OpenTelemetry 自動關聯：
- Trace ID 在所有 Logs 中自動出現
- 子 Span 自動連接到父 Span
- 跨服務的請求自動傳播 Trace Context (W3C Trace Context)

### 4. Exporters

- **Console Exporter**: 開發和學習用，直接印到終端機
- **OTLP Exporter**: 標準協定，支援 Jaeger、Prometheus 等
- **Prometheus Exporter**: 暴露 `/metrics` 端點供抓取

---

## 常見問題

### Q1: 為什麼我看不到 Traces？

**檢查清單**:
- [ ] ActivitySource 名稱是否加入 `.AddSource()`？
- [ ] 是否有建立 Activity？ (`StartActivity()`)
- [ ] Exporter 是否正確設定？
- [ ] Console 輸出是否被過濾？

### Q2: Metrics 沒有出現在 `/metrics`？

**檢查清單**:
- [ ] 是否呼叫 `app.MapPrometheusScrapingEndpoint()`？
- [ ] Meter 名稱是否加入 `.AddMeter()`？
- [ ] 是否有實際記錄 Metrics？ (`counter.Add()`)
- [ ] 是否加入 Prometheus Exporter？

### Q3: Logs 沒有 TraceId？

確保：
```csharp
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeScopes = true;  // 必須啟用
});
```

### Q4: 效能影響？

OpenTelemetry 設計為低開銷：
- CPU: < 5% 額外使用率
- Memory: 50-100MB (批次緩衝)
- Latency: < 1ms per operation

本 POC 使用 100% 採樣，生產環境建議 10-25%。

---

## 資源連結

### 官方文件
- [OpenTelemetry .NET](https://opentelemetry.io/docs/instrumentation/net/)
- [Microsoft .NET Observability](https://learn.microsoft.com/dotnet/core/diagnostics/observability-prgrja-example)
- [OpenTelemetry Specification](https://opentelemetry.io/docs/specs/otel/)

### 工具
- [Jaeger UI](http://localhost:16686) (執行後)
- [Prometheus Metrics](http://localhost:5000/metrics) (執行後)

### 範例程式碼
- [OpenTelemetry .NET Examples](https://github.com/open-telemetry/opentelemetry-dotnet/tree/main/examples)
