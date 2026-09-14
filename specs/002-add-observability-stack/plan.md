# Implementation Plan: Observabilidad End-to-End (Trazas, Logs y Métricas)

**Branch**: `002-add-observability-stack` | **Date**: 2026-09-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-add-observability-stack/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Instrumentar `EventService` y `NotificationService` con OpenTelemetry (trazas + métricas) para que el flujo "crear evento" (HTTP → Outbox → RabbitMQ → consumo) quede representado como una única traza end-to-end, propagada automáticamente a través de MassTransit sin tocar la lógica de negocio existente. Se agregan cuatro componentes nuevos al `docker-compose.yml` existente — **Grafana Tempo** (trazas), **Prometheus** (métricas de ambos servicios + RabbitMQ/Postgres/Redis vía exporters), **Loki + Promtail** (logs centralizados, reutilizando el JSON estructurado que Serilog ya emite a consola desde la Fase 1) y **Grafana** (visualización unificada con correlación trace-to-logs, dashboard de salud, service graph y alertas SLO nativas) — todos de mejor esfuerzo (FR-016): si el stack de observabilidad cae, la creación de eventos y el envío de notificaciones siguen funcionando sin cambios.

## Technical Context

**Language/Version**: C# / .NET 10 (sin cambios — mismo stack de `EventService` y `NotificationService` del Spec 01); configuración declarativa YAML para Tempo/Loki/Prometheus/Grafana (sin código nuevo fuera de .NET).

**Primary Dependencies**:
- Instrumentación .NET (ambos servicios): `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Instrumentation.EntityFrameworkCore` (spans de EF Core/Npgsql), `OpenTelemetry.Instrumentation.StackExchangeRedis` (solo `EventService`, único que usa Redis), `OpenTelemetry.Exporter.OpenTelemetryProtocol` (spans → Tempo vía OTLP), `OpenTelemetry.Exporter.Prometheus.AspNetCore` (expone `/metrics` para scraping de Prometheus). MassTransit 8.5.10 ya trae su propio `ActivitySource("MassTransit")` nativo — no requiere un paquete adicional para generar/propagar spans de publish/consume.
- Logs: `Serilog.Enrichers.Span` (agrega `TraceId`/`SpanId` del `Activity.Current` como campos del JSON ya emitido por `RenderedCompactJsonFormatter`) — sin cambiar el sink (sigue siendo consola).
- Infraestructura nueva (contenedores, no código): `grafana/tempo`, `prom/prometheus`, `grafana/loki` + `grafana/promtail`, `grafana/grafana`, `prometheuscommunity/postgres-exporter` (×2, uno por base), `oliver006/redis_exporter`; RabbitMQ activa su plugin nativo `rabbitmq_prometheus` (ya incluido en la imagen `rabbitmq:3-management`, sin exporter externo).

**Storage**: Sin cambios en `eventdb`/`notificationdb`. Nuevo: volúmenes locales para Tempo (bloques de trazas), Loki (chunks de logs) y Prometheus (TSDB de métricas) — retención por defecto de cada backend (Assumptions del spec, fuera de alcance definir política propia).

**Testing**: xUnit + FluentAssertions + Testcontainers (sin cambios de framework). Se agregan: (a) pruebas de integración que verifican que un `traceId` generado en `EventService` llega intacto al span de consumo en `NotificationService` (propagación vía MassTransit), y (b) una prueba manual de resiliencia (Tempo/Loki/Prometheus caídos → `POST /events` sigue respondiendo `201`) documentada en `quickstart.md`, ya que un backend de observabilidad caído no es simulable de forma limpia con Testcontainers sin acoplar el test a la infraestructura de observabilidad misma.

**Target Platform**: Mismo `docker-compose.yml` local (Constitución §2) — no se agrega ningún servicio externo ni Cloud.

**Project Type**: Extensión de instrumentación sobre el monorepo backend existente (2 servicios .NET) + stack de observabilidad como contenedores adicionales en la misma orquestación. No aplica ninguna plantilla por defecto del template.

**Performance Goals / Constraints** (de las clarificaciones del spec):
- La instrumentación NO DEBE agregar más de **+50ms de latencia en p95** por solicitud (SC-006) — se logra usando exportación **por lotes** (`BatchActivityExportProcessor`, el default de `AddOtlpExporter`) y el exporter de métricas Prometheus en modo *pull* (sin bloquear el request path).
- Las alertas de SLO (tasa de error / latencia p95) DEBEN sostenerse **al menos 1 minuto** antes de disparar (FR-012).
- La instrumentación NO DEBE incluir email ni contenido del mensaje de notificación como atributo de span/campo de log (FR-018) — se filtra explícitamente en la configuración de instrumentación de HTTP/EF Core (`EnrichWithHttpRequest`/`FilterHttpRequestMessage` no deben leer el body; los logs del envío de correo en `NotificationService` ya solo registran `eventId`, no destinatario/contenido — se verifica, no se agrega nada nuevo que loguee esos campos).
- Si el backend de trazas/logs/métricas no está disponible, `EventService`/`NotificationService` DEBEN seguir procesando con normalidad (FR-016) — todos los exporters usan reintentos internos acotados y descarte silencioso ante fallo, nunca una excepción que interrumpa el request/consumer.
- Todo corre dentro del único `docker-compose.yml` existente, sin servicios Cloud (Constitución §2).

**Scale/Scope**: Entorno de demo/local — 2 servicios .NET instrumentados + 4 componentes de visualización/almacenamiento (Tempo, Loki, Prometheus, Grafana) + 1 sidecar (Promtail) + 3 exporters de infraestructura (2× postgres-exporter, 1× redis_exporter; RabbitMQ usa su plugin propio). Sampling: **100% (`AlwaysOnSampler`)** dado el volumen bajo esperado en demo/pruebas manuales — no se justifica una estrategia de sampling parcial a esta escala.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principio / Regla de la Constitución | Evaluación | Estado |
|---|---|---|
| §2 Local-first, un único `docker-compose.yml`, sin Cloud/librerías de pago | Tempo, Loki, Promtail, Prometheus, Grafana y los 3 exporters se agregan como servicios nuevos en el mismo `docker-compose.yml`; todas las imágenes/paquetes son OSS | PASS |
| §4 Prohibido hardcodear secretos reales | El usuario/password admin de Grafana se define vía `GRAFANA_ADMIN_USER`/`GRAFANA_ADMIN_PASSWORD` en `.env` (nuevo, con placeholder en `.env.example`), nunca en `docker-compose.yml` ni en `provisioning/` versionado | PASS |
| §4 Desacoplamiento HTTP / Transactional Outbox / Idempotencia | Sin cambios — la instrumentación es transversal (middleware/`ActivitySource`), no modifica el flujo de negocio ni agrega llamadas síncronas nuevas entre servicios | PASS |
| §6.1 Base mínima Fase 1 (Serilog JSON, `correlationId`, health checks) | Se reutiliza tal cual: mismo sink de consola, mismo middleware de `correlationId`, mismos endpoints `/health/*` — Fase 2 solo añade campos (`TraceId`/`SpanId`) y consumidores (Promtail) sobre esa base, según el diseño intencional de §6.1 | PASS |
| §6.2 Fase 2 — Observabilidad completa | **Actualizado por el propio Spec 002** (ver `spec.md` §Assumptions): el backend de trazas pasa de **Jaeger** (texto original de §6.2) a **Grafana Tempo**, y se agrega **Prometheus** como backend de métricas (no mencionado en la redacción original de §6.2). Loki y Grafana como visualización unificada se mantienen sin cambios. Esto es una sustitución de herramienta explícitamente instruida por el negocio para esta feature, no una desviación no autorizada ni una complejidad añadida — no requiere entrada en Complexity Tracking | PASS (con sustitución documentada) |
| §7.1–7.5 Convenciones de arquitectura/DDD/SOLID | Sin cambios — no se toca la estructura de capas ni el modelo de dominio de ningún servicio | PASS |

**Resultado**: Sin violaciones. La única desviación respecto al texto literal de §6.2 (Jaeger→Tempo, +Prometheus) está explícitamente autorizada por la instrucción de esta feature y documentada en `spec.md`; no se requiere la sección de Complexity Tracking.

**Re-check post-Phase 1**: `data-model.md` y `contracts/` no introducen ningún componente fuera de esta tabla (los exporters de infraestructura y el plugin de RabbitMQ están cubiertos por FR-019, agregado en `/speckit-clarify`). Constitution Check sigue en **PASS**.

## Project Structure

### Documentation (this feature)

```text
specs/002-add-observability-stack/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── observability-conventions.md
│   └── alerting-rules.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── EventService/
│   ├── EventService.Api/
│   │   ├── Observability/            # NUEVO: extension method AddObservability(builder) — registra
│   │   │                              #   TracerProvider/MeterProvider, ActivitySource("MassTransit"),
│   │   │                              #   exporters OTLP (Tempo) y Prometheus (/metrics)
│   │   └── Program.cs                # 1 línea nueva: builder.AddObservability(); + Serilog.Enrichers.Span
│   └── ... (Domain/Application/Infrastructure sin cambios)
└── NotificationService/
    ├── NotificationService.Worker/
    │   ├── Observability/            # NUEVO: misma extensión reutilizada (mismo paquete, sin StackExchangeRedis)
    │   └── Program.cs                # mismo patrón que EventService.Api
    └── ... (Domain/Application/Infrastructure sin cambios)

tests/
├── EventService.Api.IntegrationTests/
│   └── ObservabilityTests.cs         # NUEVO: traceId end-to-end (HTTP → Outbox → RabbitMQ → consumer)
└── NotificationService.Application.Tests/
    └── (sin cambios — la propagación se prueba desde el lado de EventService)

observability/                         # NUEVO directorio, montado como volúmenes read-only en docker-compose
├── tempo/
│   └── tempo.yaml                    # OTLP receiver + metrics-generator (service-graphs, span-metrics)
├── prometheus/
│   ├── prometheus.yml                # scrape configs: api-event, api-notifications, exporters, remote-write receiver
│   └── (sin reglas de alerta acá — las alertas viven en Grafana, ver contracts/alerting-rules.md)
├── loki/
│   └── loki-config.yaml
├── promtail/
│   └── promtail-config.yaml          # scrapea stdout de api-event/api-notifications (JSON) y de rabbitmq
│                                      #   (texto plano, best-effort, FR-005) vía Docker service discovery
└── grafana/
    └── provisioning/
        ├── datasources/
        │   └── datasources.yaml      # Tempo + Loki (tracesToLogsV2) + Prometheus, todo como código
        ├── dashboards/
        │   ├── dashboards.yaml       # provider
        │   ├── health-overview.json  # US2: tasa de error + latencia p50/p95/p99 por servicio + infra
        │   ├── correlated-logs.json  # US1: búsqueda por eventId/correlationId/traceId
        │   └── service-graph.json    # US2: mapa de dependencias EventService ⇄ RabbitMQ ⇄ NotificationService
        └── alerting/
            └── slo-alerts.yaml       # US3: reglas de alerta unificadas de Grafana (ver contracts/alerting-rules.md)

docker-compose.yml                     # MODIFICADO: + tempo, loki, promtail, prometheus, grafana,
                                        #   postgres-exporter-event, postgres-exporter-notification, redis-exporter;
                                        #   rabbitmq activa RABBITMQ_PLUGINS=rabbitmq_prometheus;
                                        #   api-event/api-notifications ganan la env var Otel__ExporterEndpoint
                                        #   (mismo patrón que RabbitMq__Host/Redis__ConnectionString existentes)
.env.example                           # MODIFICADO: + GRAFANA_ADMIN_USER/PASSWORD, puertos de Tempo/Loki/Prometheus/Grafana
```

**Structure Decision**: No se crean proyectos .NET nuevos ni se reorganiza la Clean Architecture existente — la instrumentación es un *cross-cutting concern* que vive en una carpeta `Observability/` dentro de la capa de entrada de cada servicio (`Api`/`Worker`, igual que el `Middleware/` de `correlationId` de la Fase 1), sin tocar `Domain`/`Application`/`Infrastructure` (FR-015). Todo lo demás (Tempo/Loki/Prometheus/Grafana/exporters) es configuración declarativa nueva bajo `observability/` en la raíz del repo, montada como volúmenes en el mismo `docker-compose.yml` — refleja la restricción de un único archivo de orquestación (Constitución §2) sin mezclar esa configuración con el código de los servicios.

## Complexity Tracking

*No aplica — el Constitution Check no registró violaciones que requieran justificación (la sustitución Jaeger→Tempo está autorizada explícitamente por el spec, no es una complejidad añadida por esta fase de planificación).*
