# OpenTelemetry .NET Demo

A comprehensive .NET 9 Web API demonstrating OpenTelemetry observability with distributed tracing, metrics, and logging. Features a complete observability stack with OpenTelemetry Collector, Jaeger, Prometheus, and Grafana, showcasing advanced tracing concepts including Baggage propagation and Span Links.

---

## Table of Contents

- [Overview](#overview)
- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Architecture](#architecture)
- [OpenTelemetry Concepts Demonstrated](#opentelemetry-concepts-demonstrated)
- [API Endpoints](#api-endpoints)
- [Configuration](#configuration)
- [Testing](#testing)
- [Observability Stack](#observability-stack)
- [Project Structure](#project-structure)
- [Advanced Topics](#advanced-topics)
- [Notes](#notes)

---

## Overview

This project demonstrates enterprise-grade OpenTelemetry implementation in .NET, covering:

- **Tracing**: Parent-child span relationships, nested spans, Span Links, Baggage propagation
- **Metrics**: Counter, Histogram, UpDownCounter, ObservableGauge
- **Logging**: Structured logging with trace correlation
- **Multi-layer observability**: Infrastructure → Application → Presentation layers
- **Auto-instrumentation**: ASP.NET Core and HttpClient
- **Manual instrumentation**: Custom spans with detailed attributes and events

**Observability Stack**:
- **API**: .NET 9 Web API with OpenTelemetry SDK (service name: `OpenTelemetryDemo.Api`)
- **OTel Collector**: Receives OTLP, routes to Jaeger and Prometheus
- **Jaeger**: Distributed tracing visualization
- **Prometheus**: Metrics storage and querying
- **Grafana**: Unified dashboards with pre-provisioned datasources

**DDD Structure**:
- `src/Domain/` - Domain entities (empty for this demo)
- `src/Application/` - Weather service contracts (`IWeatherService`, `WeatherDto`)
- `src/Infrastructure/` - Weather service implementation with nested span tracing
- `src/Presentation/` - Web API with controllers demonstrating various OTel features

---

## Prerequisites

- **Docker Desktop** (required for full stack deployment)
- **.NET SDK 9.0** (optional - for local development)

---

## Quick Start

### Docker Deployment (Recommended)

```bash
# Start all services
docker compose up -d --build

# Check service status
docker compose ps

# View API logs
docker compose logs -f api

# Tear down
docker compose down
```

### Local .NET Development

```bash
cd src/Presentation/OpenTelemetryDemo.WebApi
dotnet restore
dotnet run
```

### Access Points

| Service | URL | Credentials |
|---------|-----|-------------|
| **API** | http://localhost:8080 | - |
| **Swagger UI** | http://localhost:8080/swagger | - |
| **Prometheus Metrics** | http://localhost:8080/metrics | - |
| **Jaeger UI** | http://localhost:16686 | - |
| **Prometheus** | http://localhost:9090 | - |
| **Grafana** | http://localhost:3000 | admin/admin |
| **OTel Collector Health** | http://localhost:13133 | - |
| **OTel Collector Metrics** | http://localhost:8888/metrics | - |

---

## Architecture

### Service Diagram

```
┌─────────────────┐
│   Web Browser   │
└────────┬────────┘
         │ HTTP
         ▼
┌─────────────────────────────────────────────┐
│  .NET 9 Web API (Port 8080)                 │
│  ┌────────────────────────────────────────┐ │
│  │ OpenTelemetry SDK                      │ │
│  │ - Tracing (ActivitySource)             │ │
│  │ - Metrics (Meter)                      │ │
│  │ - Logging (ILogger)                    │ │
│  └────────────────────────────────────────┘ │
└──────┬──────────────────────────────────────┘
       │ OTLP (gRPC :4317)
       ▼
┌─────────────────────────────────────────────┐
│  OpenTelemetry Collector                    │
│  ┌────────────────────────────────────────┐ │
│  │ Receivers: OTLP (gRPC/HTTP)            │ │
│  │ Processors: Batch                      │ │
│  │ Exporters: Jaeger, Logging             │ │
│  └────────────────────────────────────────┘ │
└──────┬────────────────────┬─────────────────┘
       │                    │
       │ Traces             │ Metrics
       ▼                    ▼
┌─────────────┐      ┌──────────────┐
│   Jaeger    │      │  Prometheus  │
│  (Port      │      │  (Port 9090) │
│   16686)    │      │  Scrapes:    │
│             │      │  - API       │
│             │      │  - Collector │
└─────────────┘      └──────┬───────┘
                            │
                            ▼
                     ┌──────────────┐
                     │   Grafana    │
                     │  (Port 3000) │
                     │  Datasources:│
                     │  - Prometheus│
                     │  - Jaeger    │
                     └──────────────┘
```

### Port Mappings

| Port | Service | Purpose |
|------|---------|---------|
| 8080 | API | Main application endpoint |
| 16686 | Jaeger UI | Trace visualization |
| 9090 | Prometheus | Metrics queries |
| 3000 | Grafana | Dashboards |
| 4317 | OTel Collector | OTLP gRPC receiver |
| 4318 | OTel Collector | OTLP HTTP receiver |
| 13133 | OTel Collector | Health check endpoint |
| 8888 | OTel Collector | Self-metrics endpoint |

---

## OpenTelemetry Concepts Demonstrated

### 1. Tracing (Distributed Tracing)

#### Parent-Child Span Relationships
Demonstrates hierarchical span relationships across multiple layers:

**Example: Weather API Flow**
```
ASP.NET Core auto-span (automatic)
└── GetCurrentWeather (Controller - manual)
    └── FetchWeatherData (Infrastructure - manual)
        ├── PrepareHttpClient
        ├── [HttpClient auto-span] (automatic)
        ├── DeserializeWeatherResponse
        ├── ValidateWeatherData
        └── TransformToDto
```

**Implementation**: `src/Infrastructure/OpenTelemetryDemo.Infrastructure/Weather/OpenMeteoWeatherService.cs` (Lines 28-108)

#### Span Attributes (Tags)
Add contextual metadata to spans for filtering and analysis:

```csharp
activity?.SetTag("weather.provider", "open-meteo");
activity?.SetTag("weather.latitude", latitude);
activity?.SetTag("user.name", name);
```

**Implementation**: Throughout controllers and services

#### Span Events
Record timeline markers within spans:

```csharp
activity?.AddEvent(new ActivityEvent("HttpResponseReceived",
    tags: new ActivityTagsCollection { { "status_code", (int)response.StatusCode } }));
```

**Implementation**: `OpenMeteoWeatherService.cs` (Lines 55-56, 73)

#### Exception Recording
Capture and record exceptions with span context:

```csharp
activity?.RecordException(ex);
activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
```

**Implementation**: `DemoController.cs` (Lines 83-84, 109)

#### Baggage (Context Propagation)
Pass business context data across service boundaries:

```csharp
// Set baggage
Activity.Current?.SetBaggage("userId", userId);
Activity.Current?.SetBaggage("tenantId", tenantId);

// Read baggage
var userId = Activity.Current?.GetBaggageItem("userId");
```

**Use Cases**:
- Multi-tenant request tracking
- Feature flag propagation
- User context in distributed systems
- Experiment group tracking

**Implementation**: `DemoController.cs` (Lines 127-187)

#### Span Links
Connect indirectly related spans (not parent-child):

```csharp
using var bgActivity = _activitySource.StartActivity(
    "BackgroundTask",
    ActivityKind.Internal,
    parentContext: default,  // No parent
    links: new[] { new ActivityLink(parentContext) }); // Weak association
```

**Use Cases**:
- Async background tasks
- Message queue producer-consumer
- Batch processing
- Event-driven architectures

**Implementation**: `DemoController.cs` (Lines 217-221)

#### Auto-Instrumentation
Automatic span creation for common libraries:

```csharp
.AddAspNetCoreInstrumentation()  // HTTP requests
.AddHttpClientInstrumentation()  // External HTTP calls
```

**Configuration**: `Program.cs` (Lines 49, 51)

### 2. Metrics

#### Counter (Monotonic - always increases)
Count occurrences of events:

```csharp
var greetingCounter = meter.CreateCounter<int>("greetings.count",
    description: "Counts greeting requests");

// Usage
_greetingCounter.Add(1,
    new KeyValuePair<string, object?>("endpoint", "/api/demo"));
```

**Definition**: `Program.cs` (Lines 17-18)
**Usage**: `DemoController.cs` (Lines 39, 59-61)

#### Histogram (Distribution of values)
Measure distribution of request durations, sizes, etc.:

```csharp
var requestDuration = meter.CreateHistogram<double>("request.duration",
    unit: "ms", description: "Request processing duration");

// Usage
_requestDuration.Record(sw.ElapsedMilliseconds,
    new KeyValuePair<string, object?>("endpoint", "/greet"));
```

**Definition**: `Program.cs` (Lines 19-20)
**Usage**: `DemoController.cs` (Lines 64-65)

#### UpDownCounter (Can increase/decrease)
Track values that can go up and down:

```csharp
var activeRequestsCounter = meter.CreateUpDownCounter<int>("active_requests.count",
    description: "Number of currently active requests");

// Usage
_activeRequestsCounter.Add(1, ...);   // Request start
_activeRequestsCounter.Add(-1, ...);  // Request end
```

**Definition**: `Program.cs` (Lines 23-24)
**Usage**: `MetricsController.cs` (Lines 30, 57)

**Use Cases**: Active connections, queue depth, inventory levels

#### ObservableGauge (Auto-collected periodically)
Report current state without manual updates:

```csharp
var memoryGauge = meter.CreateObservableGauge("system.memory.used",
    () => GC.GetTotalMemory(forceFullCollection: false) / 1024.0 / 1024.0,
    unit: "MB",
    description: "Current application memory usage in MB");
```

**Definition**: `Program.cs` (Lines 27-36)

**Key Difference**: Callback is invoked automatically during metrics collection - no manual `Record()` calls needed.

**Use Cases**: Memory usage, cache size, configuration values, current temperature

### 3. Logging

Structured logging with automatic trace correlation:

```csharp
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeScopes = true;
    logging.IncludeFormattedMessage = true;
});
```

**Configuration**: `Program.cs` (Lines 79-83)

Logs automatically include `TraceId` and `SpanId` when inside an active span, enabling correlation between logs and traces.

### 4. Exporters

#### Console Exporter (Development)
Output telemetry to console for debugging:

```csharp
.AddConsoleExporter()
```

**Configuration**: `Program.cs` (Lines 55, 71)

#### OTLP Exporter (Production)
Send telemetry to OpenTelemetry Collector:

```csharp
.AddOtlpExporter()
```

**Configuration**: `Program.cs` (Line 56)
**Endpoint**: Set via `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable

#### Prometheus Exporter
Expose metrics in Prometheus format:

```csharp
.AddPrometheusExporter()
app.MapPrometheusScrapingEndpoint();  // /metrics endpoint
```

**Configuration**: `Program.cs` (Lines 73, 118)

---

## API Endpoints

### DemoController (`/api/demo`)

| Endpoint | Method | Description | Demonstrates |
|----------|--------|-------------|--------------|
| `/api/demo` | GET | Simple greeting | Basic span, tags, counter |
| `/api/demo/greet/{name}` | GET | Personalized greeting with delay | Span tags, events, counter, histogram, async work |
| `/api/demo/error` | GET | Error demonstration | Exception recording, error status, structured logging |
| `/api/demo/external` | GET | External API call | HttpClient auto-instrumentation |
| `/api/demo/baggage` | GET | Baggage propagation demo | Baggage set/get, context propagation, child spans |
| `/api/demo/async-task` | POST | Span Links demo | Span Links, async background tasks, weak associations |

### WeatherController (`/api/weather`)

| Endpoint | Method | Description | Demonstrates |
|----------|--------|-------------|--------------|
| `/api/weather?lat={lat}&lon={lon}` | GET | Fetch weather data | Multi-layer tracing, external HTTP call, nested spans |

**Default**: Taipei (lat: 25.0330, lon: 121.5654)

**External API**: Open-Meteo (no API key required)
- URL: `https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current_weather=true`
- Response mapped to `WeatherDto`: latitude, longitude, temperature, windSpeed, weatherCode, time

### MetricsController (`/api/metrics`)

| Endpoint | Method | Description | Demonstrates |
|----------|--------|-------------|--------------|
| `/api/metrics/demo?delayMs={ms}` | GET | Metrics demonstration | UpDownCounter increment/decrement, ObservableGauge |
| `/api/metrics/cache?action={action}` | POST | Cache operation simulation | ObservableGauge state tracking |

---

## Configuration

### Program.cs Configuration

**File**: `src/Presentation/OpenTelemetryDemo.WebApi/Program.cs`

#### 1. Define Custom Meter and ActivitySource (Lines 13-36)

```csharp
var meter = new Meter("OpenTelemetryDemo.App", "1.0.0");
var activitySource = new ActivitySource("OpenTelemetryDemo.App");

// Define metrics
var greetingCounter = meter.CreateCounter<int>("greetings.count", ...);
var requestDuration = meter.CreateHistogram<double>("request.duration", ...);
var activeRequestsCounter = meter.CreateUpDownCounter<int>("active_requests.count", ...);
var memoryGauge = meter.CreateObservableGauge("system.memory.used", ...);
var cacheSizeGauge = meter.CreateObservableGauge("cache.size", ...);
```

#### 2. Configure OpenTelemetry (Lines 41-74)

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: "OpenTelemetryDemo.Api", serviceVersion: "1.0.0"))

    // Tracing
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource(activitySource.Name)
        .AddConsoleExporter()
        .AddOtlpExporter())

    // Metrics
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(meter.Name)
        .AddConsoleExporter()
        .AddPrometheusExporter());
```

#### 3. Configure Logging (Lines 79-83)

```csharp
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeScopes = true;
    logging.IncludeFormattedMessage = true;
});
```

#### 4. Register Services (Lines 88-101)

```csharp
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Domain services
builder.Services.AddScoped<IWeatherService, OpenMeteoWeatherService>();

// Telemetry singletons for DI
builder.Services.AddSingleton(meter);
builder.Services.AddSingleton(activitySource);
builder.Services.AddSingleton(greetingCounter);
builder.Services.AddSingleton(requestDuration);
builder.Services.AddSingleton(activeRequestsCounter);
```

### Docker Compose Configuration

**File**: `docker-compose.yml`

- **api**: .NET application with `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317`
- **otel-collector**: Routes telemetry to Jaeger and exposes metrics
- **jaeger**: Trace storage and UI
- **prometheus**: Metrics storage (scrapes API and collector)
- **grafana**: Visualization with pre-provisioned datasources

### OpenTelemetry Collector Configuration

**File**: `otel-collector-config.yaml`

```yaml
receivers:
  otlp:
    protocols:
      grpc: { endpoint: 0.0.0.0:4317 }
      http: { endpoint: 0.0.0.0:4318 }

exporters:
  logging: { loglevel: info }
  otlp/jaeger:
    endpoint: jaeger:4317
    tls: { insecure: true }

processors:
  batch: {}

service:
  pipelines:
    traces:
      receivers: [otlp]
      processors: [batch]
      exporters: [logging, otlp/jaeger]
```

### Prometheus Configuration

**File**: `prometheus.yml`

```yaml
scrape_configs:
  - job_name: 'otel-api'
    scrape_interval: 5s
    static_configs:
      - targets: ['api:8080']

  - job_name: 'otel-collector'
    static_configs:
      - targets: ['otel-collector:8888']
```

---

## Testing

### Quick Test Commands

```bash
# Basic greeting (span + counter)
curl http://localhost:8080/api/demo

# Greet with name (span events + histogram)
curl http://localhost:8080/api/demo/greet/Alice

# Error demonstration (exception recording)
curl http://localhost:8080/api/demo/error

# External HTTP call (auto-instrumentation)
curl http://localhost:8080/api/demo/external

# Baggage context propagation
curl "http://localhost:8080/api/demo/baggage?userId=user-456&tenantId=tenant-789"

# Span Links (async tasks)
curl -X POST "http://localhost:8080/api/demo/async-task?taskCount=3"

# Weather with default coordinates (Taipei)
curl "http://localhost:8080/api/weather"

# Weather with custom coordinates (New York)
curl "http://localhost:8080/api/weather?lat=40.7128&lon=-74.0060"

# Metrics demonstration (UpDownCounter)
curl "http://localhost:8080/api/metrics/demo?delayMs=2000"

# Prometheus metrics endpoint
curl http://localhost:8080/metrics
```

### Observability Verification

1. **View Traces in Jaeger**:
   - Open http://localhost:16686
   - Select service: `OpenTelemetryDemo.Api`
   - Search for traces
   - Examine span hierarchy, tags, events, and baggage

2. **Query Metrics in Prometheus**:
   - Open http://localhost:9090
   - Query examples:
     - `greetings_count_total`
     - `request_duration_bucket`
     - `active_requests_count`
     - `system_memory_used`
     - `http_server_request_duration_seconds_count`

3. **Create Dashboards in Grafana**:
   - Open http://localhost:3000 (admin/admin)
   - Add dashboard using Prometheus datasource
   - Add trace queries using Jaeger datasource
   - Correlate metrics and traces

---

## Observability Stack

### Jaeger (Tracing)

**URL**: http://localhost:16686

**Features**:
- Distributed trace visualization
- Service dependency graphs
- Trace search and filtering
- Span details with tags, events, and logs

**Search Tips**:
- Service: `OpenTelemetryDemo.Api`
- Operation: `GetCurrentWeather`, `FetchWeatherData`, `BaggageDemo`, etc.
- Tags: `weather.latitude`, `user.name`, `baggage.userId`

### Prometheus (Metrics)

**URL**: http://localhost:9090

**Query Examples**:

```promql
# Counter - total greetings
greetings_count_total

# Histogram - request duration (95th percentile)
histogram_quantile(0.95, rate(request_duration_bucket[5m]))

# UpDownCounter - current active requests
active_requests_count

# ObservableGauge - current memory usage
system_memory_used

# Auto-instrumented - HTTP request rate
rate(http_server_request_duration_seconds_count[1m])
```

### Grafana (Dashboards)

**URL**: http://localhost:3000 (admin/admin)

**Pre-provisioned Datasources**:
- Prometheus (http://prometheus:9090)
- Jaeger (http://jaeger:16686)

**Dashboard Ideas**:
- HTTP request rate, duration, error rate
- Active requests over time (UpDownCounter)
- Memory usage trends (ObservableGauge)
- Request duration histogram
- Trace correlation with metrics

---

## Project Structure

### DDD Layers

```
src/
├── Domain/
│   └── OpenTelemetryDemo.Domain/
│       └── (Domain entities - empty for this demo)
│
├── Application/
│   └── OpenTelemetryDemo.Application/
│       └── Weather/
│           ├── IWeatherService.cs          # Service contract
│           └── WeatherDto.cs                # Data transfer object
│
├── Infrastructure/
│   └── OpenTelemetryDemo.Infrastructure/
│       └── Weather/
│           └── OpenMeteoWeatherService.cs   # Implementation with nested spans
│
└── Presentation/
    └── OpenTelemetryDemo.WebApi/
        ├── Controllers/
        │   ├── DemoController.cs            # Basic OTel demos, Baggage, Span Links
        │   ├── WeatherController.cs         # Multi-layer tracing
        │   └── MetricsController.cs         # UpDownCounter, ObservableGauge
        ├── Program.cs                       # OTel configuration
        └── OpenTelemetryDemo.csproj         # Dependencies
```

### Key Files and Their Purposes

| File | Lines | Purpose |
|------|-------|---------|
| **Program.cs** | 17-36 | Metric definitions (4 types) |
| | 41-74 | OpenTelemetry configuration |
| | 79-83 | Logging configuration |
| | 97-101 | Telemetry DI registration |
| **OpenMeteoWeatherService.cs** | 28-108 | Enterprise-grade nested span tracing |
| **DemoController.cs** | 33-42 | Basic span + counter |
| | 45-70 | Span events + histogram |
| | 73-88 | Exception recording |
| | 122-187 | Baggage propagation |
| | 194-262 | Span Links |
| **MetricsController.cs** | 27-60 | UpDownCounter usage |
| **WeatherController.cs** | 26-39 | Multi-layer tracing trigger |

---

## Advanced Topics

### Nested Span Tracing Pattern

**Example**: `OpenMeteoWeatherService.cs`

This demonstrates enterprise-grade instrumentation with granular operation tracking:

```
FetchWeatherData (parent span)
├── PrepareHttpClient
│   └── Tags: client.configured = true
├── [HttpClient auto-span] (external API call)
├── DeserializeWeatherResponse
│   ├── Tags: serialization.format, response.size_bytes
│   ├── Performance: Stopwatch tracking (deserialization.duration_ms)
│   └── Event: DeserializationCompleted
├── ValidateWeatherData
│   └── Tags: validation.result, validation.error (if failed)
└── TransformToDto
    └── Tags: transform.source, transform.target, result.*
```

**Benefits**:
- Pinpoint performance bottlenecks at operation level
- Track success/failure of individual steps
- Capture detailed context for troubleshooting
- Enable fine-grained SLO monitoring

### Baggage vs Span Attributes

| Feature | Baggage | Span Attributes |
|---------|---------|-----------------|
| **Propagation** | Across service boundaries via HTTP headers | Local to span only |
| **Use Case** | User context, tenant ID, feature flags | Operation-specific metadata |
| **Performance** | Higher overhead (transmitted with every request) | Lower overhead |
| **Query** | Not directly queryable | Indexed and searchable |
| **Best Practice** | Minimal, essential context only | Rich, detailed operation metadata |

**Pattern**: Set Baggage, then record as span attributes for querying:

```csharp
Activity.Current?.SetBaggage("userId", userId);
activity?.SetTag("baggage.userId", userId);  // Make it queryable
```

### Span Links vs Parent-Child

| Relationship | Span Links | Parent-Child |
|--------------|------------|--------------|
| **Connection** | Weak association | Strong hierarchy |
| **Timing** | Can be asynchronous | Sequential or concurrent |
| **Use Case** | Batch processing, message queues, fire-and-forget | Request-response, nested calls |
| **Visualization** | Separate traces, linked | Single trace tree |

**Example Use Cases**:
- **Parent-Child**: HTTP request → database query
- **Span Links**: Order placed → async order fulfillment tasks

---

## Notes

### Dependencies

OpenTelemetry packages (see `OpenTelemetryDemo.csproj`):

- `OpenTelemetry.Exporter.Console` 1.7.0
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.7.0
- `OpenTelemetry.Exporter.Prometheus.AspNetCore` 1.7.0-rc.1
- `OpenTelemetry.Extensions.Hosting` 1.7.0
- `OpenTelemetry.Instrumentation.AspNetCore` 1.7.1
- `OpenTelemetry.Instrumentation.Http` 1.7.1
- `OpenTelemetry.Instrumentation.Runtime` 1.7.0
- `Swashbuckle.AspNetCore` 6.8.1

### Security Notes

- Vulnerability warnings may appear for `OpenTelemetry.Instrumentation.AspNetCore` and `OpenTelemetry.Instrumentation.Http` 1.7.1
- Consider upgrading to patched versions when available
- Grafana default credentials (admin/admin) should be changed in production
- OTLP uses insecure connections in this demo (enable TLS in production)

### Production Considerations

1. **Resource Configuration**:
   - Set resource attributes: `service.name`, `service.version`, `deployment.environment`
   - Add host metadata: `host.name`, `host.id`

2. **Sampling**:
   - Implement trace sampling for high-volume services
   - Use parent-based and probability sampling strategies

3. **Cardinality**:
   - Limit unique tag values (avoid user IDs in metric dimensions)
   - Use bounded value sets for dimensions

4. **Performance**:
   - Use batch processors for exporters
   - Configure appropriate buffer sizes
   - Monitor OTel Collector resource usage

5. **Security**:
   - Enable TLS for OTLP exporters
   - Secure Grafana, Prometheus, Jaeger with authentication
   - Sanitize sensitive data in span attributes and logs

### Learning Path

1. **Beginner**: Start with `DemoController.GetDemo()` and `Greet()`
2. **Intermediate**: Explore `WeatherController` multi-layer tracing
3. **Advanced**: Study `BaggageDemo` and `SpanLinksDemo`
4. **Expert**: Examine `OpenMeteoWeatherService` nested span pattern

---

## Contributing

This is a demonstration project. Feel free to:
- Add new instrumentation examples
- Improve documentation
- Report issues or suggestions
- Share your own observability patterns

---

## License

This project is for educational purposes. Use at your own discretion.

---

## References

- [OpenTelemetry .NET Documentation](https://opentelemetry.io/docs/instrumentation/net/)
- [OpenTelemetry Specification](https://opentelemetry.io/docs/specs/otel/)
- [Jaeger Documentation](https://www.jaegertracing.io/docs/)
- [Prometheus Documentation](https://prometheus.io/docs/)
- [Grafana Documentation](https://grafana.com/docs/)
