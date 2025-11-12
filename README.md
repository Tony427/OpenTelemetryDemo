# OpenTelemetry .NET Demo

一鍵啟動 .NET 9 API + OpenTelemetry Collector + Jaeger + Prometheus + Grafana 的觀測性示範專案。

參考：Microsoft .NET Observability with OpenTelemetry

---

## 內容與架構

- API: `src/Presentation/OpenTelemetryDemo.WebApi`（.NET 9 Minimal API，導出 OTLP、Prometheus 指標；組件名稱 `OpenTelemetryDemo`）
- OTel Collector: 接收 OTLP，轉送至 Jaeger（Traces），自身輸出 Metrics
- Jaeger: Trace UI（16686）
- Prometheus: 指標收集與查詢（9090）
- Grafana: 視覺化儀表板（3000），預設連線 Prometheus 與 Jaeger

專案結構（DDD 分層，僅整理目錄，未新增功能）
- `src/Domain/`
- `src/Application/`
- `src/Infrastructure/`
- `src/Presentation/OpenTelemetryDemo.WebApi/`

Ports
- API: `http://localhost:8080`（Metrics: `/metrics`）
- Jaeger UI: `http://localhost:16686`
- Prometheus: `http://localhost:9090`
- Grafana: `http://localhost:3000`（admin/admin）

---

## 快速開始

前置需求
- Docker Desktop（必需）
- .NET SDK 9.0（選用，僅本機直接執行/開發時需要）

啟動與關閉
```bash
# 建置並啟動所有服務
docker compose up -d --build

# 查看服務狀態
docker compose ps

# 停止並移除
docker compose down
```

產生一些資料
```bash
curl http://localhost:8080/
curl http://localhost:8080/greet/test
```

觀察
- Traces: 打開 Jaeger，搜尋 service = `OpenTelemetryDemo.Api`
- Metrics: 在 Prometheus 查詢 `greetings_count`, `request_duration`, 或 ASP.NET 內建指標
- Grafana: 已預設 Prometheus/Jaeger 資料來源，可自行建立儀表板

---

## 重要設定與檔案

- `src/Presentation/OpenTelemetryDemo.WebApi/Program.cs`
  - 已啟用 `.AddOtlpExporter()`，端點由環境變數 `OTEL_EXPORTER_OTLP_ENDPOINT` 提供
  - 已啟用 `.AddPrometheusExporter()` 與 `app.MapPrometheusScrapingEndpoint()`

- `docker-compose.yml`
  - 服務：`api`、`otel-collector`、`prometheus`、`grafana`、`jaeger`
  - `api` 設定 `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317`
  - 對外映射埠：8080/16686/9090/3000/13133/8888

- `Dockerfile`（根目錄）：用於建置 `OpenTelemetryDemo.WebApi` 並於 8080 執行
- `otel-collector-config.yaml`（根目錄）：接收 OTLP（gRPC/HTTP），轉送 Jaeger OTLP（`jaeger:4317`）
- `prometheus.yml`（根目錄）：抓取 `api:8080/metrics` 與 `otel-collector:8888`
- `grafana/provisioning/datasources/datasources.yml`：預設 Prometheus 與 Jaeger 資料來源

---

## 常見問題

- 檔案副檔名 `.yml` vs `.yaml` 有差嗎？
  - 沒有功能上的差別，兩者對工具而言等價；屬於慣例與風格選擇。
  - Docker/Compose、Prometheus、Grafana 都能接受兩種副檔名。
  - 本專案同時存在 `.yml` 與 `.yaml`，僅為維持各工具生態的常見習慣，若需要也可統一其中一種。

- 埠號衝突
  - 如果本機已有程式佔用 16686/9090/3000 等埠，請調整 `docker-compose.yml` 中對外映射的左側主機埠。

---

## API 範例端點

- `GET /`：簡單回應與 Trace
- `GET /greet/{name}`：自訂標籤、延遲與 Histogram 記錄
- `GET /error`：拋出例外並記錄於 Activity 與 Logs
- `GET /external`：示範外部 HTTP 呼叫（可能受網路/防火牆影響）

