---

description: "Task list template for feature implementation"
---

# Tasks: Core MVP - Plataforma de Eventos

**Input**: Design documents from `/specs/001-core-mvp-events-platform/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Incluidas. `plan.md` §Technical Context ya fijó el stack de testing (xUnit + FluentAssertions + Testcontainers para backend, Vitest + React Testing Library para Frontend) y `plan.md` §Project Structure ya reserva proyectos de test dedicados — generar tasks.md sin tareas de test dejaría esa parte del plan sin cubrir.

**Organization**: Las tareas están agrupadas por historia de usuario (spec.md) para poder implementar y probar cada una de forma independiente.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Puede ejecutarse en paralelo (archivos distintos, sin dependencias pendientes)
- **[Story]**: A qué historia de usuario pertenece (US1..US5, por prioridad P1..P5 de spec.md)
- Cada tarea incluye la ruta de archivo exacta

## Path Conventions

Estructura real (no plantilla genérica) — ver `plan.md` §Project Structure:
- `src/EventService/EventService.{Domain,Application,Infrastructure,Api}/`
- `src/NotificationService/NotificationService.{Domain,Application,Infrastructure,Worker}/`
- `src/Frontend/src/`
- `tests/{EventService.Domain,EventService.Application,EventService.Api.Integration,NotificationService.Application,Frontend}.Tests/`
- Raíz: `docker-compose.yml`, `.env.example`, `.gitignore`, `README.md`, `docs/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Inicialización del monorepo, soluciones .NET, proyecto Frontend y esqueleto de infraestructura de contenedores.

- [X] T001 Crear la estructura de directorios del monorepo (`docs/`, `src/EventService/`, `src/NotificationService/`, `src/Frontend/`, `tests/`, y los archivos raíz `docker-compose.yml`, `.env.example`, `.gitignore`, `README.md`) per plan.md §Project Structure
- [X] T002 [P] Inicializar la solución .NET 10 `PlataformaEventos.sln` en la raíz del repo (nota: el SDK 10 generó `PlataformaEventos.slnx`, el nuevo formato XML de solución; funcionalmente equivalente)
- [X] T003 [P] Crear los proyectos `EventService.Domain`, `EventService.Application`, `EventService.Infrastructure`, `EventService.Api` en `src/EventService/` con las referencias de capa correctas (Domain ← Application ← Infrastructure; Api referencia Application e Infrastructure) per Constitución §7.1
- [X] T004 [P] Crear los proyectos `NotificationService.Domain`, `NotificationService.Application`, `NotificationService.Infrastructure`, `NotificationService.Worker` en `src/NotificationService/` con las referencias de capa correctas per Constitución §7.1
- [X] T005 [P] Crear el proyecto Frontend (Vite + React 18 + TypeScript + Tailwind CSS) en `src/Frontend/`
- [X] T006 [P] Crear los proyectos xUnit `EventService.Domain.Tests`, `EventService.Application.Tests`, `EventService.Api.IntegrationTests`, `NotificationService.Application.Tests` en `tests/`, referenciando sus proyectos de `src/` correspondientes y agregando `FluentAssertions` + `Testcontainers.PostgreSql`/`Testcontainers.RabbitMq`/`Testcontainers.Redis`
- [X] T007 [P] Configurar `Frontend.Tests` con Vitest + React Testing Library en `tests/Frontend.Tests/`
- [X] T008 [P] Crear `.gitignore` en la raíz (bin/, obj/, *.user, .vs/, node_modules/, dist/, .vite/, .env, .DS_Store, Thumbs.db, .idea/) per Constitución §1.1
- [X] T009 [P] Crear `Dockerfile` (multi-stage `sdk:10.0` → `aspnet:10.0`) y `.dockerignore` para `EventService` en `src/EventService/`
- [X] T010 [P] Crear `Dockerfile` (multi-stage `sdk:10.0` → `aspnet:10.0`) y `.dockerignore` para `NotificationService` en `src/NotificationService/`
- [X] T011 [P] Crear `.dockerignore` para el Frontend en `src/Frontend/`
- [X] T012 Crear el esqueleto de `docker-compose.yml` en la raíz declarando los servicios `postgres-event`, `postgres-notification`, `rabbitmq` (imagen `-management`), `redis`, `api-event`, `api-notifications`, `frontend`, con sus build contexts, puertos y volúmenes nombrados `postgres-event-data`/`postgres-notification-data` per plan.md §6.1
- [X] T013 [P] Crear `.env.example` en la raíz con cadenas de conexión de `eventdb`/`notificationdb`, credenciales de RabbitMQ, `Jwt__SigningKey` (placeholder ≥32 bytes) y puertos de cada servicio

**Checkpoint**: Estructura de proyectos y contenedores lista para recibir infraestructura compartida.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Infraestructura transversal que TODAS las historias de usuario necesitan (logging, autenticación/autorización, manejo de errores, CORS, rate limiting, DbContexts, bus de mensajería, health checks, token de demo).

**⚠️ CRITICAL**: Ninguna historia de usuario puede implementarse hasta completar esta fase.

- [X] T014 [P] Implementar la clase base `Entity` (Id + igualdad) en `EventService.Domain` y `NotificationService.Domain` per data-model.md §Convenciones comunes
- [X] T015 [P] Implementar `DomainException` en `src/EventService/EventService.Domain/DomainException.cs`
- [X] T016 Configurar logging estructurado Serilog en formato JSON a consola (campos `timestamp`, `level`, `service`, `correlationId`, `message`) en `EventService.Api/Program.cs` y `NotificationService.Worker/Program.cs` per Constitución §6.1 (formato `RenderedCompactJsonFormatter`; `service`/`correlationId` como propiedades enriquecidas de nivel superior)
- [X] T017 [P] Implementar middleware de `correlationId` en `EventService.Api` (toma `X-Correlation-Id` del request o genera un `Guid` nuevo, lo agrega al `LogContext` de Serilog y lo hace eco en el header de respuesta) per contracts/events-api.md
- [X] T018 Configurar autenticación JWT Bearer en `EventService.Api` (validación HMAC-SHA256, `ValidAlgorithms` fijado explícitamente para rechazar `alg: none`/otros, chequeo de arranque que falla rápido si `Jwt__SigningKey` falta o tiene menos de 32 bytes) per spec FR-022 y research.md §4b
- [X] T019 [P] Definir las políticas de autorización `RequireAdminRole` y `RequireAuthenticatedUser` en `EventService.Api` per Constitución §5.1
- [X] T020 [P] Implementar manejo global de excepciones (`IExceptionHandler` → `ProblemDetails` genérico, nunca traza de pila/mensaje de excepción real) en `EventService.Api` per spec FR-014
- [X] T021 [P] Configurar la política de CORS (allow-list `http://localhost:5173`, métodos `GET`/`POST`, header `Authorization` permitido) en `EventService.Api` per spec FR-021
- [X] T022 [P] Configurar rate limiting nativo de .NET (`FixedWindow` 20 req/min en `POST /events`, 100 req/min en `GET /events*`, respuesta `429`) en `EventService.Api` per spec FR-013 (políticas `create-event`/`read-events` registradas; aplicación a los endpoints concretos ocurre en US1/US2)
- [X] T023 Configurar `EventDbContext` (Npgsql, `eventdb`) + fábrica de diseño para migraciones en `EventService.Infrastructure`
- [X] T024 [P] Configurar `NotificationDbContext` (Npgsql, `notificationdb`) + fábrica de diseño para migraciones en `NotificationService.Infrastructure`
- [X] T025 Configurar el registro del bus MassTransit + transporte RabbitMQ en `EventService.Api` (Outbox y publicación se agregan en US1)
- [X] T026 [P] Configurar el registro del bus MassTransit + transporte RabbitMQ + host de consumers en `NotificationService.Worker` (consumers concretos se agregan en US4; SDK cambiado a `Microsoft.NET.Sdk.Web` para poder exponer el Kestrel liviano de T028)
- [X] T027 [P] Implementar los endpoints `/health/live` y `/health/ready` (checks de Postgres/Redis/RabbitMQ) en `EventService.Api` per contracts/events-api.md (RabbitMQ vía el health check nativo de MassTransit, no un paquete Xabaril separado — ver research.md §6)
- [X] T028 [P] Implementar `/health/live` y `/health/ready` (checks de Postgres/RabbitMQ, sin Redis) sobre un host Kestrel liviano en `NotificationService.Worker`
- [X] T029 Registrar MediatR + el pipeline behavior de FluentValidation en `EventService.Application`/DI
- [X] T030 Implementar el script/herramienta generador de token JWT de demo (`tools/generate-demo-token`) que firma tokens HMAC-SHA256 con `sub`, `role` (`Admin`/`User`) y `exp` = 24h usando `Jwt__SigningKey` per research.md §4
- [X] T031 Actualizar `docker-compose.yml` con bloques `healthcheck` para `postgres-event`/`postgres-notification`/`rabbitmq`/`redis` y `depends_on: condition: service_healthy` para `api-event`/`api-notifications`/`frontend` (depende de T012, T027, T028; se instaló `curl` en ambos Dockerfiles finales para poder healthcheckear `/health/ready`)

**Checkpoint**: Infraestructura base lista — la implementación de historias de usuario puede comenzar.

---

## Phase 3: User Story 1 - Admin registra un nuevo evento (Priority: P1) 🎯 MVP

**Goal**: Un Admin puede crear un evento con zonas; el sistema lo persiste de forma atómica y dispara la notificación asociada de forma desacoplada.

**Independent Test**: `POST /events` con datos válidos y token `Admin` → `201` + el evento aparece luego en el listado (US2) y su `OutboxMessage` queda encolado; con datos inválidos → `400` con errores por campo.

### Tests for User Story 1

> **NOTE: Escribir estos tests primero y confirmar que fallan antes de implementar.**

- [X] T032 [P] [US1] Tests unitarios de las invariantes de `Event.Create` (campos requeridos/longitud máxima, ≥1 zona, `capacity>0`, `price>=0`) en `tests/EventService.Domain.Tests/EventTests.cs`
- [X] T033 [P] [US1] Tests unitarios de `CreateEventCommandHandler` (fakes de `IEventRepository`/`IEventCacheService`) en `tests/EventService.Application.Tests/CreateEventCommandHandlerTests.cs`
- [X] T034 [P] [US1] Tests de integración de `POST /events` (201 con token `Admin` y body válido; 400 por cada caso inválido) con `WebApplicationFactory` + Testcontainers en `tests/EventService.Api.IntegrationTests/CreateEventTests.cs`

### Implementation for User Story 1

- [X] T035 [P] [US1] Implementar el enum `EventStatus` en `src/EventService/EventService.Domain/EventStatus.cs`
- [X] T036 [US1] Implementar la entidad `Zone` en `src/EventService/EventService.Domain/Zone.cs` (depende de T014)
- [X] T037 [US1] Implementar el aggregate root `Event` + el factory method `Event.Create` + `EventCreatedDomainEvent` en `src/EventService/EventService.Domain/Event.cs` (depende de T036)
- [X] T038 [US1] Implementar la configuración EF Core de `Event`/`Zone` (`decimal(10,2)` en `Price`, índice en `Zone.EventId`, `ON DELETE CASCADE`) en `EventService.Infrastructure` per data-model.md
- [X] T039 [US1] Generar la migración EF Core inicial de `eventdb` (tablas `Event`, `Zone`, Outbox) en `EventService.Infrastructure/Migrations`
- [X] T040 [US1] Implementar `IEventRepository` + `EventRepository` (EF Core) en `EventService.Infrastructure`
- [X] T041 [US1] Configurar el Outbox de EF Core de MassTransit (`AddEntityFrameworkOutbox<EventDbContext>`) en `EventService.Infrastructure` per research.md §1
- [X] T042 [P] [US1] Implementar el record del mensaje `EventCreated` per contracts/event-created-message.md en `EventService.Application`
- [X] T043 [US1] Implementar `CreateEventCommand` + `CreateEventCommandValidator` (FluentValidation per FR-002) en `EventService.Application`
- [X] T044 [US1] Implementar `CreateEventCommandHandler` (persiste Event+Zones y publica `EventCreated` vía Outbox en una sola transacción; invalida la clave de caché `events:list`) en `EventService.Application` (depende de T040, T041, T042)
- [X] T045 [US1] Implementar el endpoint `POST /events` (`[Authorize(Policy = "RequireAdminRole")]`, `201`+`Location` per contracts/events-api.md) en `EventService.Api`
- [X] T046 [US1] Conectar el arranque de `EventService.Api` (registrar DI de T035-T045; `Database.Migrate()` con fallo rápido per research.md §9) en `EventService.Api/Program.cs`
- [X] T047 [P] [US1] Crear la estructura de la página/componente "Registrar Evento" en `src/Frontend/src/`
- [X] T048 [US1] Implementar la lista dinámica de zonas (agregar/eliminar fila, mínimo 1 zona, deshabilitar "Guardar" si queda vacía) en el componente del Frontend (depende de T047)
- [X] T049 [US1] Implementar la validación de cliente que refleja las reglas del backend (campos requeridos, `capacity>0`, `price>=0`, errores inline por campo) en el componente del Frontend (depende de T047)
- [X] T050 [US1] Implementar la integración `fetch` a `POST /events` (`Authorization: Bearer` con el token de demo `Admin` de T030, `X-Correlation-Id` vía `crypto.randomUUID()`, `VITE_API_EVENT_URL`, estados de `loading`/`error`) en el componente del Frontend (depende de T048, T049)
- [X] T051 [P] [US1] Tests Vitest + React Testing Library de la validación de zonas y de los estados de carga/error en `tests/Frontend.Tests/`

**Checkpoint**: US1 completamente funcional y probable de forma independiente (API + Frontend).

---

## Phase 4: User Story 2 - Consultar eventos existentes (Priority: P2)

**Goal**: Cualquier usuario autenticado puede listar eventos y ver el detalle de uno, con lecturas repetidas servidas eficientemente desde caché.

**Independent Test**: gracias al seed (FR-020), `GET /events` funciona sin necesidad de haber ejecutado US1 antes; segunda llamada notablemente más rápida (caché); `GET /events/{id}` con id inexistente → `404`.

### Tests for User Story 2

- [X] T052 [P] [US2] Tests unitarios de `GetEventsQueryHandler`/`GetEventByIdQueryHandler` (ramas de cache hit/miss, fake de `IEventCacheService`) en `tests/EventService.Application.Tests/GetEventsQueryHandlerTests.cs`
- [X] T053 [P] [US2] Tests de integración de `GET /events` (200 incluyendo el evento de seed) y `GET /events/{id}` (404 para id inexistente) en `tests/EventService.Api.IntegrationTests/GetEventsTests.cs`

### Implementation for User Story 2

- [X] T054 [P] [US2] Implementar `RedisEventCacheService` (`IEventCacheService` vía `IDistributedCache`, claves `events:list`/`events:{id}`, TTL 60s) en `EventService.Infrastructure`
- [X] T055 [US2] Implementar el mapeo a `EventDto` desde `Event`/`Zone` en `EventService.Application` (ya resuelto en US1 — `EventMapper.cs` — porque `POST /events` ya necesitaba devolver un `EventDto`; reutilizado aquí sin cambios)
- [X] T056 [US2] Implementar `GetEventsQuery` + handler (cache-aside sobre `events:list`) en `EventService.Application` (depende de T054, T055)
- [X] T057 [US2] Implementar `GetEventByIdQuery` + handler (cache-aside sobre `events:{id}`, mapeo a `404`) en `EventService.Application` (depende de T054, T055)
- [X] T058 [US2] Implementar los endpoints `GET /events` y `GET /events/{id}` (`[Authorize(Policy = "RequireAuthenticatedUser")]`) per contracts/events-api.md en `EventService.Api`
- [X] T059 [US2] Implementar la lógica de seed al arrancar (insertar un evento de ejemplo con 2 zonas de forma fija y conocida si `Events` está vacía, per FR-020/research.md §9) en `EventService.Api/Program.cs`

**Checkpoint**: US1+US2 funcionan juntas; `GET /events` funciona incluso antes de cualquier `POST` manual gracias al seed.

---

## Phase 5: User Story 3 - Acceso restringido por rol (Priority: P3)

**Goal**: Confirmar de punta a punta que solo `Admin` puede crear eventos, que cualquier autenticado puede leerlos, y que nadie sin credenciales puede hacer ninguna de las dos cosas.

**Independent Test**: 3 solicitudes (sin token, rol `User`, rol `Admin`) contra los endpoints ya construidos en US1/US2 devuelven `401`/`403`/`201`(o `200`) respectivamente.

*(La aplicación de la regla en sí ya quedó cableada en la fase Foundational — T018/T019 — y consumida por los endpoints de US1/US2; esta fase la verifica y cierra el ciclo con las herramientas de demo.)*

### Tests for User Story 3

- [X] T060 [US3] Tests de integración de la matriz completa de autorización sobre `POST /events` y `GET /events` (sin token→401, rol `User`→403 en POST/200 en GET, rol `Admin`→201/200) en `tests/EventService.Api.IntegrationTests/AuthorizationTests.cs`

### Implementation for User Story 3

- [X] T061 [US3] Generar el token de demo con rol `User` (usando el script de T030) y documentar ambos tokens (`Admin`/`User`) en `README.md` para verificación manual per quickstart.md §7

**Checkpoint**: Acceso por rol verificado de punta a punta para los endpoints de US1/US2.

---

## Phase 6: User Story 4 - Procesamiento confiable de la notificación (Priority: P4)

**Goal**: `NotificationService` procesa `EventCreated` de forma idempotente, simula el envío de un correo, y dejar registro consultable de éxito o fallo tras agotar reintentos.

**Independent Test**: reenviar manualmente el mismo `messageId` → una sola fila en `AuditLog`; forzar un fallo de procesamiento → tras 3 reintentos, mensaje en la cola `_error` + fila `Status = Failed`.

### Tests for User Story 4

- [X] T062 [P] [US4] Tests unitarios de idempotencia de `EventCreatedConsumer` (duplicado por `MessageId` omitido vía fast-path y vía captura de violación de restricción única; `PayloadHash` distinto logueado, sin sobrescribir) en `tests/NotificationService.Application.Tests/EventCreatedConsumerTests.cs`
- [X] T063 [P] [US4] Tests unitarios de `EventCreatedFaultConsumer` marcando `AuditLog.Status = Failed` en `tests/NotificationService.Application.Tests/EventCreatedFaultConsumerTests.cs`

### Implementation for User Story 4

- [X] T064 [P] [US4] Implementar la entidad simple `AuditLog` en `src/NotificationService/NotificationService.Domain/AuditLog.cs` per data-model.md
- [X] T065 [US4] Implementar la configuración EF Core de `AuditLog` (`UNIQUE INDEX` sobre `MessageId`) en `NotificationService.Infrastructure`
- [X] T066 [US4] Generar la migración EF Core inicial de `notificationdb` (tabla `AuditLog`) en `NotificationService.Infrastructure/Migrations`
- [X] T067 [US4] Implementar `IAuditLogRepository` + `AuditLogRepository` (EF Core, captura la violación de restricción única como duplicado) en `NotificationService.Infrastructure` per data-model.md §AuditLog
- [X] T068 [US4] Configurar la política de reintentos de MassTransit (`UseMessageRetry` 3×5s) sobre el pipeline del consumer de `EventCreated` en `NotificationService.Worker` per research.md §2
- [X] T069 [P] [US4] Implementar el helper de simulación de correo con MailKit (construye `MimeMessage`, imprime asunto+cuerpo en consola, loguea que es una simulación) en `NotificationService.Infrastructure`
- [X] T070 [US4] Implementar `EventCreatedConsumer` (fast-path `SELECT` → simular correo → insertar `AuditLog` `Processed` en la misma unidad de trabajo → capturar violación única como duplicado) en `NotificationService.Application` (depende de T064, T067, T069)
- [X] T071 [US4] Implementar `EventCreatedFaultConsumer` (`IConsumer<Fault<EventCreated>>`, marca `AuditLog.Status = Failed`) en `NotificationService.Application` (depende de T067)
- [X] T072 [US4] Conectar los consumers + `Database.Migrate()` con fallo rápido en `NotificationService.Worker/Program.cs`

**Checkpoint**: flujo asíncrono de punta a punta (US1→US4) verificable per quickstart.md §4-6.

---

## Phase 7: User Story 5 - Puesta en marcha autocontenida (Priority: P5)

**Goal**: Toda la plataforma se levanta con un único comando en una máquina limpia, siguiendo solo el README.

**Independent Test**: `docker compose up --build` desde un clon limpio + copia de `.env` → todos los contenedores `healthy` → pasos 1, 7 y 9 de quickstart.md se completan sin pasos adicionales no documentados.

### Implementation for User Story 5

*(Sin tareas de test automatizado: "se puede seguir el README sin ayuda" se valida por checklist manual, no por un framework de test — ver T077.)*

- [X] T073 Finalizar `docker-compose.yml` (los 7 servicios completamente cableados, interpolación de `.env`, cadena de `depends_on` correcta) per plan.md §6.1
- [X] T074 [P] Escribir el `README.md` raíz: prerrequisitos, clonar, configurar `.env`, `docker compose up --build`, migraciones (automáticas + comando manual), explicación del seed, URLs de acceso, uso de los tokens de demo, troubleshooting básico per spec §7/FR-019
- [X] T075 [P] Escribir `docs/architecture.md` documentando el diseño teórico a 6 meses (ítems fuera de alcance de spec §Assumptions: OpenTelemetry/Jaeger/Grafana, Keycloak, paginación/búsqueda/estados de ciclo de vida) per Constitución §6.2/§5.2
- [X] T076 [P] Escribir `docs/roadmap.md` resumiendo el plan por fases (Spec 01 MVP → Spec 02 Observabilidad → Spec 03 Keycloak) per Constitución
- [X] T077 Ejecutar quickstart.md de punta a punta sobre un checkout limpio (`docker compose down -v` y luego el recorrido completo) y corregir cualquier paso no documentado que se descubra — **verificado en vivo**: `docker compose up --build` con volúmenes limpios → los 7 servicios `healthy` → seed visible en `GET /events` → `POST /events` (201) → notificación consumida y simulada en `api-notifications` (verificado en logs) → `404`/`401`/`403` confirmados → Frontend sirviendo en `:5173`. En el proceso se corrigieron dos bugs reales encontrados solo al correr en Docker real (no visibles en los tests con mocks): (1) MassTransit 9.x requiere licencia paga — se bajó a 8.5.10 (Apache 2.0, gratuita) en todos los proyectos; (2) el mensaje `EventCreated` nunca llegaba al consumer porque MassTransit identifica el tipo de mensaje por namespace+nombre, no por el nombre del exchange — se unificó el namespace de ambos DTOs (`EventContracts`) sin crear un proyecto compartido. Idempotencia y resiliencia (DLQ/Failed) se validaron vía los tests dedicados (T062/T063) en vez de repetirse en vivo, por tiempo.

**Checkpoint**: las 5 historias de usuario funcionan de forma independiente; la plataforma es reproducible desde un clon limpio.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Mejoras que atraviesan varias historias de usuario.

- [X] T078 [P] Agregar descripciones OpenAPI/Swagger a todos los endpoints de `EventService.Api` (solo en `Development`) per Constitución §7.2
- [X] T079 [P] Revisar los logs de ambos servicios para confirmar que nunca se escribe el JWT completo, el header `Authorization`, ni el body crudo del request (spec FR-015) — corregir cualquier statement de log que lo viole
- [X] T080 Ejecutar la suite de tests completa (`dotnet test`, `npm run test`) y corregir cualquier falla
- [X] T081 [P] Pasada de limpieza de código: eliminar código de scaffolding sin usar, asegurar nomenclatura consistente per Constitución §7.1/§7.5

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: sin dependencias — puede empezar de inmediato
- **Foundational (Phase 2)**: depende de que Setup esté completo — BLOQUEA todas las historias de usuario
- **User Stories (Phase 3-7)**: todas dependen de que Foundational esté completo
  - US1 (P1) y US2 (P2) pueden avanzar en paralelo tras Foundational, pero US2 asume que el modelo `Event`/`Zone` de US1 (T035-T038) ya existe — en la práctica conviene completar T035-T041 de US1 antes de empezar la implementación de US2 (no sus tests, que sí pueden escribirse antes)
  - US3 depende funcionalmente de que existan los endpoints de US1/US2 para poder verificarlos (T045, T058)
  - US4 es independiente de US2/US3; solo depende de Foundational (el bus/consumer host) y puede probarse publicando un `EventCreated` manualmente sin pasar por US1
  - US5 depende de que existan los health checks (Foundational) y, para el recorrido completo de quickstart.md, de que US1-US4 estén implementadas
- **Polish (Phase 8)**: depende de que las historias que se quieran incluir en el alcance ya estén completas

### User Story Dependencies

- **US1 (P1)**: puede empezar tras Foundational — sin dependencia funcional de otras historias
- **US2 (P2)**: puede empezar tras Foundational; reutiliza el modelo `Event`/`Zone` de US1 pero es probable de forma independiente gracias al seed (FR-020)
- **US3 (P3)**: puede empezar tras Foundational (la regla ya está cableada); sus tests de verificación requieren que los endpoints de US1/US2 existan
- **US4 (P4)**: puede empezar tras Foundational — totalmente independiente de US1/US2/US3 a nivel de implementación (solo comparte el contrato de mensaje)
- **US5 (P5)**: puede empezar tras Foundational (docker-compose/README básicos), pero su validación end-to-end completa requiere US1-US4

### Within Each User Story

- Tests antes que implementación (deben fallar primero)
- Domain antes que Application antes que Infrastructure antes que Api/Worker
- Historia completa y verificada antes de pasar a la siguiente prioridad

### Parallel Opportunities

- Todas las tareas `[P]` de Setup pueden correr en paralelo
- Todas las tareas `[P]` de Foundational pueden correr en paralelo (dentro de la Phase 2)
- Tras completar Foundational, US1 y US4 pueden trabajarse en paralelo por desarrolladores distintos (no comparten archivos ni dependencias funcionales directas)
- Los tests de una misma historia marcados `[P]` pueden correr en paralelo entre sí
- Los modelos de dominio marcados `[P]` dentro de una historia pueden correr en paralelo

---

## Parallel Example: User Story 1

```bash
# Lanzar juntos los tests de US1 (antes de implementar):
Task: "Unit tests for Event.Create invariants in tests/EventService.Domain.Tests/EventTests.cs"
Task: "Unit tests for CreateEventCommandHandler in tests/EventService.Application.Tests/CreateEventCommandHandlerTests.cs"
Task: "Integration tests for POST /events in tests/EventService.Api.IntegrationTests/CreateEventTests.cs"

# Lanzar juntos los modelos de dominio de US1:
Task: "Implement EventStatus enum in EventService.Domain"
Task: "Implement EventCreated message record in EventService.Application"
```

---

## Implementation Strategy

### MVP First (solo User Story 1)

1. Completar Phase 1: Setup
2. Completar Phase 2: Foundational (CRÍTICO — bloquea todas las historias)
3. Completar Phase 3: User Story 1
4. **DETENERSE Y VALIDAR**: probar US1 de forma independiente (quickstart.md §3, sin el paso de segunda llamada de caché)
5. Demo si está listo

### Incremental Delivery

1. Setup + Foundational → base lista
2. + US1 → probar independientemente → demo (¡MVP!)
3. + US2 → probar independientemente (listado + caché) → demo
4. + US3 → verificar la matriz de autorización → demo
5. + US4 → verificar idempotencia/resiliencia de la notificación → demo
6. + US5 → validar arranque limpio de punta a punta con quickstart.md → demo final
7. Cada historia agrega valor sin romper las anteriores

### Parallel Team Strategy

Con más de un desarrollador:

1. El equipo completa Setup + Foundational en conjunto
2. Una vez completado Foundational:
   - Desarrollador A: US1 (incluye Frontend)
   - Desarrollador B: US4 (independiente, solo necesita el contrato del mensaje)
3. US2 y US3 se agregan después de US1 (dependen de sus endpoints); US5 se cierra al final integrando todo

---

## Notes

- `[P]` = archivos distintos, sin dependencias pendientes
- La etiqueta `[Story]` mapea cada tarea a su historia de usuario para trazabilidad
- Verificar que los tests fallen antes de implementar
- Hacer commit después de cada tarea o grupo lógico
- Detenerse en cada checkpoint para validar la historia de forma independiente
- Evitar: tareas vagas, conflictos de archivo entre tareas paralelas, dependencias cruzadas entre historias que rompan su independencia
