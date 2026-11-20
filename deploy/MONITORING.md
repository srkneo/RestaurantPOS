# Monitoring setup (Grafana Cloud + Sentry, free plans)

Everything is off until the keys are in `~/pos/.env` on the cloud server. Shop PCs send nothing.
After changing `.env`, run `./deploy.sh <current version>` so the containers pick it up.

## 1. Sentry (server errors)

1. sentry.io → create project → platform **ASP.NET Core** → copy the DSN.
2. `.env`: `SENTRY_DSN=https://...` (optional `SENTRY_ENVIRONMENT=production`).
3. Sentry → Alerts → "Send a notification for new issues" (email or the Sentry app). Only errors are sent; no cookies, IPs, names or request bodies.

## 2. Grafana Cloud traces and metrics (OpenTelemetry)

1. grafana.com → your stack → **Connections → OpenTelemetry (OTLP)** → generate a token.
2. `.env`:
   - `OTEL_EXPORTER_OTLP_ENDPOINT=https://otlp-gateway-prod-<region>.grafana.net/otlp`
   - `OTEL_EXPORTER_OTLP_HEADERS=Authorization=Basic%20<base64 of instanceId:token>` (Grafana shows this line ready to copy)
   - optional `TELEMETRY_TRACE_RATIO=0.1` (share of requests traced, 0 to 1)
3. The service shows up as `restaurantpos-api` (metrics label `job="restaurantpos-api"`).

## 3. Grafana Cloud logs (Loki, via the Alloy container)

1. Grafana Cloud → **Loki → Details** → copy URL (ends in `/loki/api/v1/push`), user (a number) and create a token with logs:write.
2. `.env`: `GRAFANA_LOKI_URL=...`, `GRAFANA_LOKI_USER=...`, `GRAFANA_LOKI_TOKEN=...`
3. `./deploy.sh <version>` now also starts the `alloy` container (`docker ps` shows `pos-alloy-1`).
4. Explore → Loki: `{service_name="restaurantpos-api"}`. Find one request: `{service_name="restaurantpos-api"} | json | requestId="<id>"`.

## 4. Telegram contact point

Grafana → Alerting → Contact points → New → **Telegram**: the same bot token and chat ID as `TELEGRAM_TOKEN` / `TELEGRAM_CHAT_ID`.
Set it as the default contact point (Notification policies).

## 5. Alert rules (Alerting → Alert rules → New)

Metric names can differ slightly; check them in Explore first (type `http_server` and pick from the list).

| Alert | Query | Condition | Pending |
|---|---|---|---|
| API errors (5xx over 2%) | `sum(rate(http_server_request_duration_seconds_count{job="restaurantpos-api", http_response_status_code=~"5.."}[5m])) / sum(rate(http_server_request_duration_seconds_count{job="restaurantpos-api"}[5m]))` | > 0.02 | 5 min |
| API slow (p95 over 2 s) | `histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket{job="restaurantpos-api"}[5m])))` | > 2 | 10 min |
| Error lines in the log | `sum(count_over_time({service_name="restaurantpos-api", level="ERROR"}[5m]))` (Loki) | > 0 | 0 min |
| API not reporting | `absent_over_time(http_server_request_duration_seconds_count{job="restaurantpos-api"}[15m])` | = 1 | 0 min |
| Many browser errors | `sum(count_over_time({service_name="restaurantpos-api"} \|= "Browser error" [15m]))` (Loki) | > 20 | 0 min |

"API not reporting" only works if something calls the API at least every 15 minutes; the Uptime workflow (GitHub Actions, every 15 min) does that.
Keep the rules few: an alert that fires every day gets ignored.

## 6. Already in place without Grafana

- Uptime workflow: Telegram message when `/health/ready` fails 3 times (repository secrets `SITE_URL`, `POS_TG_TOKEN`, `POS_TG_CHAT`).
- Error log lines (Error level and up) go straight to Telegram from the API (`TELEGRAM_TOKEN` / `TELEGRAM_CHAT_ID`).
- Nightly backup and bill check send Telegram on failure.
- Slow database queries (over 500 ms): `docker logs pos-db-1 2>&1 | grep duration`.
