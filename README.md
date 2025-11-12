# OpenTelemetry .NET Demo

Demo: .NET 9 Minimal API with OpenTelemetry Collector, Jaeger, Prometheus, and Grafana. Shows end-to-end tracing, metrics, and logs, including an external HTTP call for weather data.

---

## Overview

- API: `src/Presentation/OpenTelemetryDemo.WebApi` (OpenTelemetry for tracing, metrics, logs; service name `OpenTelemetryDemo.Api`).
- OTel Collector: receives OTLP, exports to Jaeger (traces) and logs own telemetry.
- Jaeger: Trace UI.
- Prometheus: Scrapes API metrics and Collector metrics.
- Grafana: Preprovisioned datasources for Prometheus and Jaeger.

DDD structure (now present):
- `src/Domain/`
- `src/Application/` (contains weather contracts)
- `src/Infrastructure/` (contains weather implementation)
- `src/Presentation/OpenTelemetryDemo.WebApi/`

Ports
- API: `http://localhost:8080` (metrics at `/metrics`)
- Jaeger UI: `http://localhost:16686`
- Prometheus: `http://localhost:9090`
- Grafana: `http://localhost:3000` (admin/admin)

---

## Prerequisites

- Docker Desktop
- .NET SDK 9.0 (optional — for local build/test)

Run
```bash
docker compose up -d --build
docker compose ps
# tear down
docker compose down
```

Quick test
```bash
curl http://localhost:8080/
curl http://localhost:8080/greet/test
curl "http://localhost:8080/weather"
curl "http://localhost:8080/weather?lat=25.04&lon=121.56"
```

Observe
- Traces: in Jaeger, service = `OpenTelemetryDemo.Api`.
- Metrics: in Prometheus (custom: `greetings.count`, `request.duration`; plus ASP.NET/runtime metrics).
- Grafana: add dashboards using the preprovisioned datasources.

---

## Key Configuration

- `src/Presentation/OpenTelemetryDemo.WebApi/Program.cs`
  - Tracing: `.AddAspNetCoreInstrumentation()`, `.AddHttpClientInstrumentation()`, `.AddSource(...)`, `.AddConsoleExporter()`, `.AddOtlpExporter()`.
  - Metrics: `.AddAspNetCoreInstrumentation()`, `.AddRuntimeInstrumentation()`, `.AddMeter(...)`, `.AddConsoleExporter()`, `.AddPrometheusExporter()`, `app.MapPrometheusScrapingEndpoint()`.
  - Logging: `builder.Logging.AddOpenTelemetry(...)`.
  - DI: `IWeatherService` → `OpenMeteoWeatherService`.

- `docker-compose.yml`
  - Services: `api`, `otel-collector`, `prometheus`, `grafana`, `jaeger`.
  - `api` sets `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317`.
  - Exposed ports: 8080/16686/9090/3000/13133/8888/4317/4318.

- `Dockerfile`: builds and publishes `OpenTelemetryDemo.WebApi` on port 8080.
- `otel-collector-config.yaml`: OTLP receiver (gRPC/HTTP); exports traces to Jaeger (`jaeger:4317`) and logging; exposes health `:13133`, metrics `:8888`.
- `prometheus.yml`: scrapes `api:8080/metrics` and `otel-collector:8888`.
- `grafana/provisioning/datasources/datasources.yml`: Prometheus and Jaeger datasources.

---

## API Endpoints

- `GET /` — Hello world; simple trace and counter.
- `GET /greet/{name}` — Simulated work with Activity events; increments `greetings.count`; records `request.duration`.
- `GET /error` — Demonstrates exception recording on Activity and logs.
- `GET /external` — Makes an outbound HTTP request (generates HttpClient span).
- `GET /weather?lat={lat}&lon={lon}` — Uses application-layer `IWeatherService` to call the external weather API (Open‑Meteo). Defaults to Taipei (lat 25.0330, lon 121.5654). Starts `GetCurrentWeather` Activity and produces an HttpClient child span to the external API, demonstrating API → external service trace.

---

## Weather External Service

- Open‑Meteo Current Weather API (no API key):
  - `https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current_weather=true`

Response is mapped into `WeatherDto` with fields: latitude, longitude, temperature, windSpeed, weatherCode, time.

---

## DDD Projects (New)

- Domain: `src/Domain/OpenTelemetryDemo.Domain`
- Application: `src/Application/OpenTelemetryDemo.Application`
  - `Weather/IWeatherService`
  - `Weather/WeatherDto`
- Infrastructure: `src/Infrastructure/OpenTelemetryDemo.Infrastructure`
  - `Weather/OpenMeteoWeatherService` (uses `IHttpClientFactory`)

---

## Notes

- Vulnerability warnings may appear for `OpenTelemetry.Instrumentation.AspNetCore` and `OpenTelemetry.Instrumentation.Http` 1.7.1; consider upgrading to patched versions when available.
- The sample file `OpenTelemetryDemo.http` contains a default template route; it is not used by this demo.

