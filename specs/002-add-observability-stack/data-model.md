# Data Model: Observabilidad End-to-End (Trazas, Logs y Métricas)

A diferencia del Spec 01, ninguna de estas "entidades" se persiste en `eventdb`/`notificationdb`: son estructuras de datos generadas por el SDK de OpenTelemetry/Serilog y almacenadas por los backends de observabilidad (Tempo, Loki, Prometheus) o definidas como configuración declarativa de Grafana. Se documentan aquí con el mismo nivel de detalle que una entidad de dominio porque el spec las define explícitamente en su sección "Key Entities".

## Resource attributes comunes (aplican a toda traza/métrica de ambos servicios)

| Atributo | Valor | Uso |
|---|---|---|
| `service.name` | `EventService` \| `NotificationService` | Identifica el servicio en Tempo/Prometheus/Grafana; debe coincidir exactamente con el campo `service` que Serilog ya emite en los logs (Constitución §6.1), para que la correlación por nombre de servicio funcione entre las tres señales |
| `service.namespace` | `plataforma-eventos` | Agrupa ambos servicios como un mismo sistema en el service graph |
| `deployment.environment` | `local` | Fijo — no hay otros ambientes en este MVP |

## Trace (Traza)

| Campo | Origen | Notas |
|---|---|---|
| `traceId` | Generado por el SDK de OTel al iniciar el span raíz (recepción HTTP en `EventService.Api`) | Es el mismo identificador usable de forma intercambiable con `correlationId` (FR-017) solo a efectos de búsqueda en Grafana — son valores distintos e independientes en el sistema (`correlationId` es un campo de negocio propio, `traceId` es generado por OTel); Grafana permite buscar logs por cualquiera de los dos porque ambos quedan como campos indexables en Loki |
| Spans que la componen | Ver tabla `Span` abajo | Como mínimo 4 en el camino feliz (US1, escenario 1) |
| Estado global | Derivado — sin campo propio | Una traza "sin fallos" es aquella donde ningún span hijo tiene `status = Error`; Tempo/Grafana lo muestran visualmente, no se calcula ni almacena aparte |

## Span

| Nombre de span (convención, ver `contracts/observability-conventions.md`) | Servicio | Generado por |
|---|---|---|
| `POST /events` (span raíz HTTP) | EventService.Api | `OpenTelemetry.Instrumentation.AspNetCore` (automático) |
| Comando SQL de persistencia (`Event`/`Zone`/`OutboxMessage`) | EventService.Api | `OpenTelemetry.Instrumentation.EntityFrameworkCore` (automático) |
| `EventCreated publish` | EventService.Api | `ActivitySource("MassTransit")` nativo (automático al publicar vía Outbox) |
| `EventCreated receive`/`consume` | NotificationService.Worker | `ActivitySource("MassTransit")` nativo (automático en `EventCreatedConsumer`) |
| Comando SQL de auditoría (`AuditLog` insert) | NotificationService.Worker | `OpenTelemetry.Instrumentation.EntityFrameworkCore` (automático) |

**Campos por span** (todos provistos por el SDK salvo `correlationId`): `spanId`, `parentSpanId`, `name`, `startTime`/`endTime` (duración derivada), `status` (`Unset`/`Error`), `attributes` (`http.route`, `http.response.status_code`, `db.statement` — sin parámetros sensibles, `messaging.system = rabbitmq`, `messaging.destination.name = EventCreated`). El span raíz HTTP (`EventService.Api`) y el span de consumo (`NotificationService.Worker`) agregan además `correlationId` como atributo custom (único campo de esta lista que sí requiere una línea de código manual, ver `contracts/observability-conventions.md`), de forma que FR-017 sea buscable directamente en Tempo y no solo en Loki.

**Regla de exclusión (FR-018)**: ningún span de esta lista incluye `email`, contenido del mensaje de notificación, ni el body de ninguna request/response — ver `contracts/observability-conventions.md`.

## Log Entry (Entrada de log)

Extiende el formato ya definido en Constitución §6.1 (`timestamp`, `level`, `service`, `correlationId`, `message`) con dos campos nuevos, agregados por `Serilog.Enrichers.Span` sin cambiar el sink:

| Campo | Tipo | Regla |
|---|---|---|
| `TraceId` | `string` (hex, 32 caracteres) | Presente solo si el log se emite dentro de un `Activity` activo (prácticamente siempre, dado que ambos servicios instrumentan HTTP/consumer como span raíz); ausente en logs de arranque previos a cualquier request |
| `SpanId` | `string` (hex, 16 caracteres) | Idem — identifica el span específico dentro de la traza |
| `correlationId` | `Guid` (ya existente, Fase 1) | Sin cambios — sigue siendo el campo de negocio propagado por header/mensaje; usable de forma intercambiable con `TraceId` para búsqueda (FR-017), aunque son identificadores distintos |
| `eventId` | `Guid`, opcional | Ya presente donde aplica desde Fase 1; clave de búsqueda explícita de FR-010 |

**Labels de Loki** (agregados por Promtail, no por la app): `service` (desde el campo `service` del JSON), `level`. `TraceId`/`correlationId`/`eventId` se dejan como campos estructurados dentro del log (no como labels de alta cardinalidad — labels de alta cardinalidad degradan el rendimiento de Loki).

## Métrica de servicio

| Métrica (nombre semántico OTel) | Tipo | Exportada como (Prometheus) | Uso |
|---|---|---|---|
| `http.server.request.duration` | Histograma (segundos) | `http_server_request_duration_seconds_{bucket,sum,count}` | Tasa de error (vía label `http.response.status_code`/`code`) y latencia p50/p95/p99 (FR-003, FR-009) |
| `http.server.active_requests` | UpDownCounter | `http_server_active_requests` | Complementaria — no requerida por ningún FR, se incluye "gratis" por la instrumentación automática |
| Métricas de infraestructura (FR-019) | Varias | `pg_up`, `pg_stat_database_*` (postgres-exporter); `redis_*` (redis-exporter); `rabbitmq_*` (plugin nativo) | Panel de infraestructura del dashboard de salud general |

## Dashboard

| Dashboard | Historia que cubre | Contenido mínimo |
|---|---|---|
| `health-overview.json` | US2 | Tasa de error y latencia p50/p95/p99 por servicio (`EventService`, `NotificationService`) + panel de infraestructura (RabbitMQ/Postgres/Redis, FR-019) |
| `correlated-logs.json` | US1 | Buscador de logs con variables de template `eventId`/`correlationId`/`traceId` (FR-010), filtrando por `service` |
| `service-graph.json` | US2 | Grafo de dependencias `EventService → RabbitMQ → NotificationService` (datos del `metrics-generator` de Tempo, ver `research.md` §8) |

Las tres viven como JSON provisionado en `observability/grafana/provisioning/dashboards/` (código, no configuración manual en la UI de Grafana) — reproducible en cualquier `docker compose up`.

## Regla de Alerta SLO

| Campo | Valor |
|---|---|
| Métrica base | `http_server_request_duration_seconds_*` (la misma que el dashboard de salud) |
| Umbral tasa de error | > 5% sostenido (valor de partida, Assumptions del spec) |
| Umbral latencia | p95 > 1s sostenido (valor de partida, Assumptions del spec) |
| Ventana de sostenimiento (`for`) | **1 minuto** (`/speckit-clarify`, Session 2026-09-13) |
| Alcance | Por servicio (`EventService`, `NotificationService`) — 2 reglas de error + 2 de latencia = 4 reglas totales |
| Historial | Nativo de Grafana Unified Alerting (estados `Normal`/`Pending`/`Firing`/`Resolved` con timestamps, FR-013) — sin tabla propia |

Ver expresiones PromQL exactas en [`contracts/alerting-rules.md`](./contracts/alerting-rules.md).
