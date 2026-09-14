---

description: "Task list template for feature implementation"
---

# Tasks: Observabilidad End-to-End (Trazas, Logs y Métricas)

**Input**: Design documents from `/specs/002-add-observability-stack/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: `plan.md` (§Technical Context, Testing) pide explícitamente una prueba de integración para la propagación de `traceId` a través de MassTransit — se incluye como tarea de test en US1. El resto de la validación es manual, vía `quickstart.md` (Fase Polish).

**Organization**: Tareas agrupadas por historia de usuario (US1/US2/US3, prioridad P1/P2/P3 de `spec.md`) para permitir implementación y prueba independiente de cada una.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Puede ejecutarse en paralelo (archivo distinto, sin dependencias pendientes)
- **[Story]**: Historia de usuario a la que pertenece (US1, US2, US3)
- Cada tarea incluye la ruta de archivo exacta

## Path Conventions

Monorepo existente (ver `plan.md` §Project Structure): `src/EventService/`, `src/NotificationService/`, `tests/`, y un nuevo directorio `observability/` en la raíz, montado como volúmenes en `docker-compose.yml`. No se crea ningún proyecto .NET nuevo.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Estructura base y dependencias antes de tocar cualquier código de instrumentación.

- [X] T001 Crear el árbol de directorios `observability/` (`observability/tempo/`, `observability/prometheus/`, `observability/loki/`, `observability/promtail/`, `observability/grafana/provisioning/{datasources,dashboards,alerting}/`) en la raíz del repo
- [X] T002 [P] Agregar a `.env.example`: `GRAFANA_ADMIN_USER`, `GRAFANA_ADMIN_PASSWORD`, `GRAFANA_PORT` (default 3000), `PROMETHEUS_PORT` (default 9090), `TEMPO_PORT` (default 3200) — con comentario explicando que son credenciales/puertos solo del entorno local (Constitución §4)
- [X] T003 [P] Agregar referencias de paquete `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Instrumentation.EntityFrameworkCore`, `OpenTelemetry.Instrumentation.StackExchangeRedis`, `OpenTelemetry.Instrumentation.Runtime`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Exporter.Prometheus.AspNetCore`, `Serilog.Enrichers.Span` en `src/EventService/EventService.Api/EventService.Api.csproj` (se agregó `OpenTelemetry.Instrumentation.Runtime`, no listado originalmente, porque T010 requiere `AddRuntimeInstrumentation()`)
- [X] T004 [P] Agregar referencias de paquete `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Instrumentation.EntityFrameworkCore`, `OpenTelemetry.Instrumentation.Runtime`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Exporter.Prometheus.AspNetCore`, `Serilog.Enrichers.Span` (sin `StackExchangeRedis` — este servicio no usa Redis) en `src/NotificationService/NotificationService.Worker/NotificationService.Worker.csproj`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: El backbone de señales (trazas/logs/métricas de aplicación + Grafana) que las 3 historias necesitan, según `research.md` y `plan.md` §Project Structure.

**⚠️ CRITICAL**: Ninguna historia de usuario puede probarse hasta que esta fase esté completa.

- [X] T005 [P] Crear `observability/tempo/tempo.yaml`: receiver OTLP (gRPC `4317`/HTTP `4318`), storage de bloques local (research.md §1) — sin `metrics_generator` todavía (eso es US2, FR-011)
- [X] T006 [P] Crear `observability/prometheus/prometheus.yml` con `scrape_configs` solo para `api-event:8080/metrics` y `api-notifications:8080/metrics` (research.md §1/§7) — sin targets de infraestructura todavía (eso es US2, FR-019)
- [X] T007 [P] Crear `observability/loki/loki-config.yaml` (config mínima de un solo binario, storage local, research.md §6)
- [X] T008 [P] Crear `observability/promtail/promtail-config.yaml`: descubrimiento de contenedores Docker vía service discovery, con dos scrape jobs — (1) `api-event`/`api-notifications`: parseo JSON de cada línea (ya la emite `RenderedCompactJsonFormatter`), promoviendo `service`/`level`/`TraceId`; (2) `rabbitmq`: sin parseo JSON (texto plano, RabbitMQ no emite logs estructurados), solo label `service=rabbitmq` — best-effort, cumple la Assumption de spec.md sobre logs de infraestructura de mensajería (research.md §6, FR-005)
- [X] T009 Agregar a `docker-compose.yml` los servicios `tempo`, `loki`, `promtail`, `prometheus` y `grafana` (imágenes `grafana/tempo`, `grafana/loki`, `grafana/promtail`, `prom/prometheus`, `grafana/grafana`), montando como volúmenes de solo lectura los archivos de T005-T008 y todo `observability/grafana/provisioning/` en Grafana (`/etc/grafana/provisioning`), con `GF_SECURITY_ADMIN_USER`/`GF_SECURITY_ADMIN_PASSWORD` desde `.env` (T002); agregar además la variable de entorno `Otel__ExporterEndpoint: http://tempo:4317` a los servicios existentes `api-event` y `api-notifications` (mismo patrón que `RabbitMq__Host`/`Redis__ConnectionString` ya presentes en esos servicios, research.md §1) (depende de T005, T006, T007, T008)
- [ ] T010 [P] Crear `src/EventService/EventService.Api/Observability/ObservabilityExtensions.cs`: extension method `AddObservability(this WebApplicationBuilder builder)` que registra `TracerProvider` (`AddSource("MassTransit")`, `AddAspNetCoreInstrumentation()`, `AddHttpClientInstrumentation()`, `AddEntityFrameworkCoreInstrumentation()`, `AddRedisInstrumentation()`, `SetResourceBuilder(service.name="EventService", service.namespace="plataforma-eventos")`, `AlwaysOnSampler`, `AddOtlpExporter` leyendo el endpoint de `builder.Configuration["Otel:ExporterEndpoint"]` con `http://tempo:4317` como default si la variable no está seteada) y `MeterProvider` (`AddAspNetCoreInstrumentation()`, `AddHttpClientInstrumentation()`, `AddRuntimeInstrumentation()`, `AddPrometheusExporter()`) (research.md §1, depende de T003)
- [ ] T011 [P] Crear `src/NotificationService/NotificationService.Worker/Observability/ObservabilityExtensions.cs`: mismo patrón que T010 (incluyendo la lectura de `Otel:ExporterEndpoint` con el mismo default) pero `service.name="NotificationService"` y sin `AddRedisInstrumentation()` (research.md §1, depende de T004)
- [ ] T012 [P] En `src/EventService/EventService.Api/Program.cs`: llamar `builder.AddObservability()`, agregar `app.MapPrometheusScrapingEndpoint("/metrics")`, y agregar `.Enrich.WithSpan()` al pipeline de `UseSerilog` existente (depende de T010)
- [ ] T013 [P] En `src/NotificationService/NotificationService.Worker/Program.cs`: mismos tres cambios que T012 (depende de T011)
- [ ] T014 [P] Crear `observability/grafana/provisioning/datasources/datasources.yaml`: registrar los 3 datasources (Tempo → `http://tempo:3200`, Loki → `http://loki:3100`, Prometheus → `http://prometheus:9090`) como código, sin correlación trace-to-logs todavía (eso es US1, FR-008) (depende de T009)
- [ ] T015 [P] Crear `observability/grafana/provisioning/dashboards/dashboards.yaml`: provider que apunta a `observability/grafana/provisioning/dashboards/` para autocargar los JSON de dashboards (depende de T009)

**Checkpoint**: `docker compose up --build` levanta el stack completo; un `POST /events` genera spans visibles en Tempo (Explore), logs con `TraceId` visibles en Loki (Explore), y métricas `http_server_request_duration_seconds_*` visibles en Prometheus — sin dashboards ni alertas todavía.

---

## Phase 3: User Story 1 - Diagnosticar una notificación que no llegó (Priority: P1) 🎯 MVP

**Goal**: Un desarrollador busca un `eventId`/`correlationId`/`traceId` en Grafana y ve la traza completa (HTTP → Outbox → RabbitMQ → consumo), saltando directo a los logs correlacionados de ambos servicios sin cambiar de herramienta.

**Independent Test**: Crear un evento, buscar su `traceId` en Tempo (Grafana Explore), ver la traza de 4-5 spans, click en "Logs for this span" y verificar que lleva a los logs correlacionados de ambos servicios (`quickstart.md` §2). Repetir deteniendo `postgres-notification` y verificar que la traza muestra visualmente el punto de falla (`quickstart.md` §3).

### Tests for User Story 1

- [ ] T016 [P] [US1] Prueba de integración en `tests/EventService.Api.IntegrationTests/ObservabilityTests.cs`: `POST /events` con `WebApplicationFactory` + Testcontainers, capturar el `traceId` del `Activity` raíz generado por el request, y verificar (vía el exportador OTLP apuntado a un collector de prueba en memoria, o inspeccionando `ActivityListener`) que el mismo `traceId` aparece en el span de publicación (`EventCreated publish`) — cubre research.md §2 (propagación nativa de MassTransit)

### Implementation for User Story 1

- [ ] T017 [P] [US1] Extender `observability/grafana/provisioning/datasources/datasources.yaml` (de T014): agregar `tracesToLogsV2` al datasource de Tempo, apuntando a Loki, mapeando `service.name` → label `service` y usando `TraceId` como filtro de consulta (contracts/observability-conventions.md, FR-008)
- [ ] T018 [P] [US1] Crear `observability/grafana/provisioning/dashboards/correlated-logs.json`: dashboard de Loki con variables de template `eventId`, `correlationId`, `traceId` para filtrar logs por cualquiera de los tres, agrupado por `service` (data-model.md §Dashboard, FR-010)
- [ ] T019 [P] [US1] En `src/NotificationService/NotificationService.Application/Consumers/EventCreatedConsumer.cs`: agregar `EventId` como propiedad estructurada en los tres `_logger.Log*` existentes (payload distinto, duplicado ignorado), agregar un nuevo `_logger.LogInformation` con `EventId`/`MessageId` tras un `TryAddAsync` exitoso (no existe log alguno en el camino feliz hoy), y agregar `Activity.Current?.SetTag("correlationId", message.CorrelationId.ToString())` al inicio de `Consume` — solo agrega campos/líneas de log y un tag de span, no cambia ningún comportamiento de negocio (FR-015, FR-010, FR-017)
- [ ] T020 [P] [US1] En `src/EventService/EventService.Application/Events/CreateEventCommandHandler.cs`: inyectar `ILogger<CreateEventCommandHandler>` y agregar un `_logger.LogInformation` con `EventId`/`CorrelationId` justo después de `SaveChangesAsync` (línea 51) — hoy este handler no emite ningún log, por lo que el lado de `EventService` queda sin ningún registro buscable por `eventId` en Loki (hallazgo C1 de `/speckit-analyze`). Solo agrega logging, no cambia ningún comportamiento de negocio (FR-015, FR-010)
- [ ] T021 [P] [US1] En `src/EventService/EventService.Api/Middleware/CorrelationIdMiddleware.cs`: agregar `Activity.Current?.SetTag("correlationId", correlationId.ToString())` junto a donde ya resuelve el valor del header `X-Correlation-Id`/genera el `Guid` nuevo — deja el span raíz HTTP taggeado con `correlationId`, para que sea buscable directamente en Tempo (Explore → filtro de atributos), no solo vía Loki (contracts/observability-conventions.md, FR-017, hallazgo C3 de `/speckit-analyze`)

**Checkpoint**: Historia 1 completamente funcional e independientemente probable — `quickstart.md` §2 y §3 pasan.

---

## Phase 4: User Story 2 - Vista unificada de salud y dependencias (Priority: P2)

**Goal**: Un operador ve, desde un único dashboard, la tasa de error y latencia p50/p95/p99 de ambos servicios, el estado de la infraestructura compartida (RabbitMQ/Postgres/Redis) y el mapa de dependencias entre servicios.

**Independent Test**: Abrir el dashboard `health-overview` con el sistema en marcha y verificar tasa de error/latencia por servicio + panel de infraestructura; abrir `service-graph` y verificar el mapa `EventService ⇄ RabbitMQ ⇄ NotificationService` (`quickstart.md` §4).

### Implementation for User Story 2

- [ ] T022 [US2] En `docker-compose.yml`: agregar los servicios `postgres-exporter-event` y `postgres-exporter-notification` (imagen `prometheuscommunity/postgres-exporter`, cada uno con su propio `DATA_SOURCE_NAME`) y `redis-exporter` (imagen `oliver006/redis_exporter`); agregar `RABBITMQ_PLUGINS=rabbitmq_prometheus` a las variables de entorno del servicio `rabbitmq` existente; agregar `command: --web.enable-remote-write-receiver` (junto a los flags por defecto) al servicio `prometheus` de T009 (research.md §7/§8, FR-019)
- [ ] T023 [US2] Extender `observability/prometheus/prometheus.yml` (de T006) con `scrape_configs` para `postgres-exporter-event:9187`, `postgres-exporter-notification:9187`, `redis-exporter:9121` y `rabbitmq:15692` (depende de T022)
- [ ] T024 [P] [US2] Extender `observability/tempo/tempo.yaml` (de T005) habilitando `overrides.defaults.metrics_generator.processors: [service-graphs, span-metrics]` con `remote_write` apuntando a `http://prometheus:9090/api/v1/write` (research.md §8, FR-011)
- [ ] T025 [P] [US2] Crear `observability/grafana/provisioning/dashboards/health-overview.json`: tasa de error y latencia p50/p95/p99 por servicio (`http_server_request_duration_seconds_*` con label `service_name`) + panel de infraestructura (`pg_up`, `redis_*`, `rabbitmq_*`) (data-model.md §Dashboard, FR-009, depende de T023)
- [ ] T026 [P] [US2] Crear `observability/grafana/provisioning/dashboards/service-graph.json`: panel de service graph consumiendo las métricas `traces_service_graph_request_total` generadas por Tempo (data-model.md §Dashboard, FR-011, depende de T024)

**Checkpoint**: Historias 1 y 2 funcionan de forma independiente — `quickstart.md` §4 pasa.

---

## Phase 5: User Story 3 - Alertas básicas de SLO (Priority: P3)

**Goal**: El equipo recibe una alerta visible (con historial) cuando la tasa de error o la latencia p95 de un servicio se degrada de forma sostenida durante al menos 1 minuto.

**Independent Test**: Generar tráfico de error sostenido >1 minuto, ver la alerta pasar a `Firing` en Grafana Alerting; restaurar el servicio y ver la alerta volver a `Normal` con el ciclo reflejado en el historial (`quickstart.md` §5).

### Implementation for User Story 3

- [ ] T027 [US3] Crear `observability/grafana/provisioning/alerting/slo-alerts.yaml`: 4 reglas de Grafana Unified Alerting (tasa de error > 5% y latencia p95 > 1s, por cada uno de `EventService`/`NotificationService`), todas con `for: 1m` sobre el datasource de Prometheus — expresiones PromQL exactas en `contracts/alerting-rules.md` (FR-012, FR-013)

**Checkpoint**: Las 3 historias de usuario funcionan de forma independiente — `quickstart.md` §5 pasa.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Verificaciones transversales que no pertenecen a ninguna historia específica.

- [ ] T028 [P] Revisar todos los spans/atributos/logs agregados en T010-T027 y confirmar que ninguno incluye email, contenido del mensaje de notificación, ni bodies de request/response HTTP (contracts/observability-conventions.md, FR-018) — documentar el resultado de la revisión
- [ ] T029 [P] Actualizar `README.md` con: cómo acceder a Grafana/Tempo/Loki/Prometheus localmente, las nuevas variables de `.env.example` (T002), y un resumen de las 3 historias de observabilidad cubiertas
- [ ] T030 Ejecutar `quickstart.md` completo (las 7 secciones, incluyendo §6 resiliencia y §7 presupuesto de overhead de +50ms p95) contra el stack completo levantado, y registrar el resultado de cada paso (SC-001 a SC-006)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Sin dependencias — puede iniciar de inmediato
- **Foundational (Phase 2)**: Depende de Setup — BLOQUEA las 3 historias de usuario
- **User Stories (Phase 3-5)**: Todas dependen de que Foundational esté completo
  - US1 (P1) y US3 (P3) no dependen de US2 y pueden avanzar en paralelo si hay capacidad
  - US2 (P2) es independiente de US1/US3, pero comparte el archivo `docker-compose.yml`/`prometheus.yml`/`tempo.yaml` con Foundational (nunca con US1/US3)
- **Polish (Phase 6)**: Depende de que las historias que se quieran entregar estén completas

### User Story Dependencies

- **US1 (P1)**: Puede iniciar tras Foundational — sin dependencia de US2/US3
- **US2 (P2)**: Puede iniciar tras Foundational — sin dependencia de US1/US3 (comparte contenedores base, pero ningún archivo específico de US1/US3)
- **US3 (P3)**: Puede iniciar tras Foundational — solo depende del datasource de Prometheus (T014/T006), no de US1/US2

### Parallel Opportunities

- T002, T003, T004 (Setup) en paralelo
- T005, T006, T007, T008 (Foundational, archivos de config independientes) en paralelo
- T010, T011 (Foundational, un `ObservabilityExtensions.cs` por servicio) en paralelo
- T012, T013 (Foundational, un `Program.cs` por servicio) en paralelo
- T014, T015 (Foundational, ambos dependen solo de T009) en paralelo
- T016, T017, T018, T019, T020, T021 (toda la Fase 3 / US1) en paralelo — 6 archivos independientes
- T024, T025, T026 (US2, tras T022/T023) en paralelo entre sí
- T028, T029 (Polish) en paralelo

---

## Parallel Example: User Story 1

```bash
# Los 6 archivos de la Historia 1 son independientes entre sí:
Task: "Prueba de integración de traceId en tests/EventService.Api.IntegrationTests/ObservabilityTests.cs"
Task: "Agregar tracesToLogsV2 a observability/grafana/provisioning/datasources/datasources.yaml"
Task: "Crear observability/grafana/provisioning/dashboards/correlated-logs.json"
Task: "Agregar EventId y tag de correlationId a los logs/span de EventCreatedConsumer.cs"
Task: "Agregar logging de EventId/CorrelationId a CreateEventCommandHandler.cs"
Task: "Agregar tag de correlationId al span raíz en CorrelationIdMiddleware.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 solamente)

1. Completar Fase 1: Setup
2. Completar Fase 2: Foundational (CRÍTICO — bloquea las 3 historias)
3. Completar Fase 3: US1
4. **DETENER y VALIDAR**: `quickstart.md` §2-§3 de forma independiente
5. Esto ya cumple el escenario de mayor valor de negocio del spec (diagnóstico de una notificación que no llegó)

### Incremental Delivery

1. Setup + Foundational → pipeline de señales listo (sin UI de negocio todavía)
2. + US1 → diagnóstico trace-to-logs funcional (MVP)
3. + US2 → dashboard de salud + service graph
4. + US3 → alertas SLO con historial
5. Cada historia agrega valor sin romper la anterior — ninguna modifica código de negocio (`Domain`/`Application` de dominio, FR-015; solo `Consumers`/`Program.cs`/`CommandHandler`/`Middleware` a nivel de logging/tags/DI)

## Notes

- [P] = archivos distintos, sin dependencia pendiente entre sí
- [Story] mapea cada tarea a su historia de usuario para trazabilidad
- Ninguna tarea de este feature modifica `Domain` ni las reglas de negocio de `Application` (FR-015) — todos los cambios de código son de instrumentación (`Observability/`, `Program.cs`), logging adicional (T019, T020) o un tag de span (T019, T021)
- El endpoint OTLP de Tempo se lee por configuración (`Otel__ExporterEndpoint`, T009/T010/T011), no queda hardcodeado en el código — mismo patrón que `RabbitMq__Host`/`Redis__ConnectionString` ya usado en el proyecto
- `correlationId` queda como atributo de span (T019, T021) además de campo de log ya existente desde la Fase 1 — así es buscable directamente en Tempo, no solo en Loki (FR-017)
- Confirmar en cada checkpoint que el flujo de creación de eventos/notificaciones sigue funcionando exactamente igual que en el Spec 01 (FR-016)
