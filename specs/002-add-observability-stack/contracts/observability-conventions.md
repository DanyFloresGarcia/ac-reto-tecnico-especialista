# Contract: Convenciones de Observabilidad (OpenTelemetry + Serilog)

Este documento fija las convenciones que cualquier código de instrumentación nuevo (presente o futuro) DEBE seguir en `EventService` y `NotificationService`, para que trazas, logs y métricas sigan siendo correlacionables entre sí y entre ambos servicios. Es el "contrato" que expone esta feature hacia cualquier desarrollador que instrumente código nuevo — no un endpoint HTTP.

## Resource attributes (obligatorios en ambos servicios)

| Atributo | EventService | NotificationService |
|---|---|---|
| `service.name` | `EventService` | `NotificationService` |
| `service.namespace` | `plataforma-eventos` | `plataforma-eventos` |
| `deployment.environment` | `local` | `local` |

`service.name` DEBE coincidir exactamente (mismo casing) con el campo `service` que Serilog ya emite (Constitución §6.1) — es la clave que Grafana usa para cruzar métricas/trazas de un servicio con sus propios logs.

## Nomenclatura de spans

| Span | Nombre | Atributos mínimos |
|---|---|---|
| HTTP entrante | (automático: `{method} {route}`, ej. `POST /events`) | `http.route`, `http.response.status_code`, `correlationId` (custom — ver abajo) |
| Persistencia (Outbox) | (automático de EF Core: nombre de la operación SQL) | `db.system = postgresql`, `db.statement` (sin literales de parámetros — el instrumentador ya los omite por defecto) |
| Publicación en broker | `EventCreated publish` (automático de MassTransit) | `messaging.system = rabbitmq`, `messaging.destination.name = EventCreated` |
| Consumo en broker | `EventCreated receive` / `EventCreated consume` (automático de MassTransit) | idem + `messaging.message.id` (mapea a `EventCreated.MessageId`, la clave de idempotencia) + `correlationId` (custom — ver abajo) |

**Atributo custom `correlationId` (FR-017)**: es el único atributo de esta lista que no genera automáticamente ningún instrumentador — se agrega con una línea `Activity.Current?.SetTag("correlationId", correlationId)` en dos puntos: `CorrelationIdMiddleware` de `EventService.Api` (donde ya se resuelve el valor del header/`Guid` nuevo) y `EventCreatedConsumer` de `NotificationService.Application` (donde ya se lee `message.CorrelationId`). Esto permite buscar por `correlationId` directamente en Tempo (Explore → filtro de atributos), no solo en Loki, cumpliendo que `correlationId` y `traceId` sean intercambiables "para buscar y correlacionar información en la herramienta de visualización" (FR-017) sin un salto extra por Loki.

## Propagación de contexto

- El `traceparent` (W3C Trace Context) viaja como header del mensaje de RabbitMQ, generado y leído automáticamente por MassTransit — ningún código de `Application`/`Domain` lo toca ni lo conoce.
- El campo `correlationId` del contrato `EventCreated` (ver `specs/001-core-mvp-events-platform/contracts/event-created-message.md`) es independiente del `traceId` de OpenTelemetry y NO se reemplaza por él — ambos coexisten y son intercambiables solo como criterio de búsqueda en Grafana (FR-017), nunca a nivel de wire format.

## Campos de log obligatorios (Serilog)

Todo log emitido dentro del contexto de un `Activity` activo DEBE incluir, además de los campos ya fijados en Constitución §6.1 (`timestamp`, `level`, `service`, `correlationId`, `message`):

| Campo nuevo | Origen |
|---|---|
| `TraceId` | `Serilog.Enrichers.Span` (`.Enrich.WithSpan()`), leído de `Activity.Current.TraceId` |
| `SpanId` | Idem, `Activity.Current.SpanId` |

## Prohibiciones explícitas (FR-018 — datos sensibles)

Ningún span, atributo, log o métrica agregado por código de esta feature (presente o futuro) puede incluir:

- Direcciones de email (remitente o destinatario) de una notificación.
- El contenido/cuerpo (`body`, asunto, HTML) del mensaje de notificación.
- El body crudo de cualquier request/response HTTP (las instrumentaciones automáticas de ASP.NET Core/HttpClient usadas en esta feature no lo leen por defecto — no habilitar `EnrichWithHttpRequest`/`EnrichWithHttpResponse` para leer bodies).

Identificar una notificación en trazas/logs/métricas se hace exclusivamente vía `eventId`/`notificationId`/`MessageId` — nunca por el contenido que identifica al destinatario.

## Endpoints/puertos expuestos (solo red interna de `docker-compose`, nunca a internet — Assumption del spec)

| Componente | Puerto | Propósito |
|---|---|---|
| `api-event` / `api-notifications` | `/metrics` en el puerto HTTP existente (8080) | Scraping de Prometheus |
| Grafana | `3000` (host) | UI de visualización unificada (FR-007) |
| Prometheus | `9090` (host, opcional para debug) | Backend de métricas |
| Tempo | `3200` (host, opcional para debug), `4317`/`4318` (OTLP interno) | Backend de trazas |
| Loki | `3100` (interno, consumido por Grafana/Promtail) | Backend de logs |
