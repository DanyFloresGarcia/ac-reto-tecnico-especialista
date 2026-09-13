# Implementation Plan: Core MVP - Plataforma de Eventos

**Branch**: `001-core-mvp-events-platform` | **Date**: 2026-09-12 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-core-mvp-events-platform/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Construir el MVP de la Plataforma de Eventos: un `EventService` (.NET 10) que permite crear y consultar eventos con zonas, publicando un evento de dominio `EventCreated` vía Transactional Outbox (MassTransit) hacia RabbitMQ; y un `NotificationService` (.NET 10 Worker) que consume ese mensaje de forma idempotente, simula el envío de un correo, y registra el resultado (éxito o fallo tras agotar reintentos) en una tabla de auditoría propia. Un Frontend en React permite registrar eventos contra `EventService` usando un JWT de demo hardcodeado. Toda la solución (2 APIs, 2 bases Postgres independientes, RabbitMQ, Redis, Frontend) se levanta con un único `docker-compose.yml`, siguiendo exactamente el stack, las reglas de diseño y las decisiones ya fijadas en `constitution.md`.

## Technical Context

**Language/Version**: C# / .NET 10 (backend: `EventService`, `NotificationService`); TypeScript 5.x + React 18+ (Frontend, vía Vite)

**Primary Dependencies**:
- Backend: Entity Framework Core (Npgsql provider), MediatR, MassTransit (transporte RabbitMQ + `AddEntityFrameworkOutbox`), FluentValidation, `Microsoft.Extensions.Caching.StackExchangeRedis` (`IDistributedCache`), MailKit (construcción de `MimeMessage`, sin envío SMTP real), Serilog (+ sink de consola JSON), `AspNetCore.HealthChecks.NpgSql` / `.Redis` / `.Rabbitmq`, `Microsoft.AspNetCore.RateLimiting` (nativo de .NET), autenticación JWT Bearer nativa (`Microsoft.AspNetCore.Authentication.JwtBearer`).
- Frontend: React 18+, Vite, TypeScript, Tailwind CSS, `fetch` nativo del navegador (sin cliente HTTP adicional).

**Storage**: PostgreSQL 16 — dos bases de datos independientes, cada una en su propio contenedor: `eventdb` (`EventService`) y `notificationdb` (`NotificationService`). Sin acoplamiento de schema ni de motor entre ambas (Constitución §7.3).

**Testing**:
- Backend: xUnit + FluentAssertions. Pruebas unitarias de dominio (invariantes de `Event.Create`) y de `Application` (handlers de MediatR con repos/caché fake). Pruebas de integración con `WebApplicationFactory` (EventService) y Testcontainers para Postgres/RabbitMQ/Redis reales en CI/local, evitando mocks de infraestructura crítica en los tests de integración.
- Frontend: Vitest + React Testing Library para el formulario "Registrar Evento" (validaciones inline, estados loading/error).

**Target Platform**: Contenedores Linux vía Docker Compose en la máquina del desarrollador (100% local, sin dependencias Cloud — Constitución §2).

**Project Type**: Web application multi-servicio (2 backends independientes + 1 SPA), comunicados de forma asíncrona vía RabbitMQ — no aplica ninguna de las opciones de estructura por defecto del template (single-project ni backend/frontend 1:1); se define una estructura propia en la sección siguiente.

**Performance Goals**: Sin metas de throughput de producción (es un MVP de evaluación local, no un sistema de alta concurrencia). Meta observable: `GET /events` debe responder perceptiblemente más rápido en la segunda llamada dentro de la ventana de caché de 60s (SC-002 del spec) que en la primera.

**Constraints**: 100% ejecutable en local con `docker compose up --build` (un solo archivo de orquestación, Constitución §2); solo librerías open source/gratuitas; comunicación inter-servicio exclusivamente asíncrona vía RabbitMQ (nunca HTTP síncrono, Constitución §4); sin exponer detalles internos de error al cliente; rate limiting obligatorio en los endpoints de escritura y lectura.

**Scale/Scope**: Escala de demo/evaluación técnica — un puñado de eventos y solicitudes concurrentes desde un único Frontend local; no se diseña para múltiples instancias ni balanceo de carga.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principio / Regla de la Constitución | Evaluación | Estado |
|---|---|---|
| §2 Local-first, un único `docker-compose.yml`, sin Cloud/librerías de pago | Todos los componentes (Postgres×2, RabbitMQ, Redis, ambas APIs, Frontend) se definen en un solo `docker-compose.yml`; todas las librerías elegidas son OSS | PASS |
| §4 Desacoplamiento HTTP — comunicación inter-servicio solo por RabbitMQ | `EventService` y `NotificationService` nunca se llaman entre sí por HTTP; único canal es el mensaje `EventCreated` | PASS |
| §4 Transactional Outbox obligatorio en `EventService` | `AddEntityFrameworkOutbox` de MassTransit persiste el mensaje en la misma transacción EF Core que `Event`/`Zone` | PASS |
| §4 Idempotencia y resiliencia en `NotificationService` (retries + DLQ) | `EventCreatedConsumer` verifica `MessageId` antes de procesar; `UseMessageRetry` (3×5s) + cola `_error` nativa + `Fault<EventCreated>` consumer para marcar `Failed` | PASS |
| §5.1 JWT local Fase 1, roles `Admin`/`User`, policies agnósticas al proveedor | JWT Bearer HMAC-SHA256 validado en `EventService`; policies `RequireAdminRole`/`RequireAuthenticatedUser` (sin acoplarse a Keycloak) | PASS |
| §6.1 Observabilidad mínima Fase 1 (sin herramientas nuevas) | Solo Serilog JSON a consola, `correlationId` propagado, `/health/live` y `/health/ready` — nada de OpenTelemetry/Jaeger/Grafana en este Spec | PASS |
| §7.1 Nomenclatura de capas (Domain/Application/Infrastructure/Api-Worker) | Estructura de proyectos sigue exactamente esa convención para ambos servicios | PASS |
| §7.3 Una base de datos independiente por servicio | `eventdb` y `notificationdb`, cada una en su propio contenedor Postgres | PASS |
| §7.4 CORS explícito (allow-list, sin `AllowAnyOrigin`) | Política CORS limitada a `http://localhost:5173` | PASS |
| §7.5 DDD táctico básico / SOLID / patrones justificados, sin sobre-ingeniería | `Event` como Aggregate Root vía factory method; `AuditLog` modelado como entidad simple a propósito; patrones limitados a los de la tabla de la Constitución | PASS |

**Resultado**: Sin violaciones. No se requiere la sección de Complexity Tracking.

**Re-check post-Phase 1**: `data-model.md` y `contracts/` no introducen ninguna entidad, endpoint ni flujo que se aparte de la tabla anterior (p. ej., `AuditLog` se modeló deliberadamente como entidad simple, sin Aggregate, tal como exige §7.5; el mensaje `EventCreated` mantiene el schema exacto de la Constitución/Spec). Constitution Check sigue en **PASS**.

## Project Structure

### Documentation (this feature)

```text
specs/001-core-mvp-events-platform/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── events-api.md
│   └── event-created-message.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
docs/
├── architecture.md
└── roadmap.md

src/
├── EventService/
│   ├── EventService.Domain/            # Event (Aggregate Root), Zone, EventStatus, DomainException, EventCreatedDomainEvent, Entity (base)
│   ├── EventService.Application/       # CreateEventCommand/Handler/Validator, GetEventsQuery, GetEventByIdQuery, IEventRepository, IEventCacheService
│   ├── EventService.Infrastructure/    # EventDbContext (eventdb), EventRepository, MassTransit Outbox config, RedisEventCacheService
│   ├── EventService.Api/               # Controllers/minimal API, middleware (excepciones, correlationId), JWT auth, policies, Swagger, Rate Limiting, health checks, CORS
│   ├── Dockerfile
│   └── .dockerignore
├── NotificationService/
│   ├── NotificationService.Domain/     # AuditLog (entidad simple), Entity (base compartida)
│   ├── NotificationService.Application/# EventCreatedConsumer (lógica de idempotencia/simulación de correo), interfaces
│   ├── NotificationService.Infrastructure/ # NotificationDbContext (notificationdb), MassTransit consumer/fault config, MailKit
│   ├── NotificationService.Worker/     # Host BackgroundService + Kestrel liviano para /health
│   ├── Dockerfile
│   └── .dockerignore
└── Frontend/
    ├── src/                            # Pantalla "Registrar Evento", formulario de zonas dinámicas, cliente fetch a EventService
    ├── package.json
    ├── Dockerfile                      # opcional en dev (se documenta `npm run dev` como alternativa)
    └── .dockerignore

tests/
├── EventService.Domain.Tests/          # invariantes de Event.Create (xUnit)
├── EventService.Application.Tests/     # handlers MediatR con fakes (xUnit + FluentAssertions)
├── EventService.Api.IntegrationTests/  # WebApplicationFactory + Testcontainers (Postgres/RabbitMQ/Redis)
├── NotificationService.Application.Tests/ # idempotencia y fault handling del consumer
└── Frontend.Tests/                     # Vitest + React Testing Library

docker-compose.yml
.env.example
.gitignore
README.md
```

**Structure Decision**: Monorepo con dos servicios backend independientes (`EventService`, `NotificationService`) siguiendo Clean Architecture de 4 capas cada uno (convención de Constitución §7.1), más un Frontend React separado. No se usa ninguna de las plantillas por defecto (single-project ni backend/frontend 1:1) porque el dominio requiere dos backends desacoplados que solo se comunican por mensajería; la estructura de `src/` y `tests/` refleja exactamente esa topología, con un test project dedicado por capa/servicio para mantener el aislamiento de dependencias definido en §7.5 (Dependency Inversion).

## Complexity Tracking

*No aplica — el Constitution Check no registró violaciones.*
