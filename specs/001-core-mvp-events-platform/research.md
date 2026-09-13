# Phase 0 Research: Core MVP - Plataforma de Eventos

Todas las decisiones tecnológicas centrales (stack, mensajería, persistencia, seguridad Fase 1, observabilidad mínima) ya estaban fijadas por `constitution.md` antes de este plan, así que la investigación de esta fase se centra en **cómo** implementarlas concretamente y en las pocas decisiones que la Constitución y el spec dejaron abiertas (frameworks de testing, forma exacta de generar el token de demo, librerías de health checks). No quedan marcadores `NEEDS CLARIFICATION` sin resolver.

## 1. Transactional Outbox

- **Decision**: Usar el soporte nativo de MassTransit para EF Core Outbox (`AddEntityFrameworkOutbox<EventDbContext>`), no un poller manual.
- **Rationale**: Persiste el `OutboxMessage` en la misma transacción de EF Core que `Event`/`Zone` sin código de infraestructura adicional; un `BusOutboxDeliveryService` en background publica a RabbitMQ tras el commit. Ya es la decisión explícita de la Constitución §4 y del Spec 01 §3.2.
- **Alternatives considered**: Outbox manual con tabla propia + `IHostedService` de polling — descartado por duplicar algo que MassTransit ya resuelve, sin beneficio adicional para un MVP.

## 2. Mensajería y resiliencia

- **Decision**: MassTransit sobre RabbitMQ como único transporte; `UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)))` en el consumer; DLQ = cola de error nativa `<queue>_error`; `IConsumer<Fault<EventCreated>>` dedicado para marcar `AuditLog.Status = Failed` tras agotar reintentos.
- **Rationale**: Evita implementar retry/DLQ a mano; el patrón `Fault<T>` de MassTransit es la forma idiomática de reaccionar al agotamiento de reintentos sin acoplar ese código al propio `EventCreatedConsumer`.
- **Alternatives considered**: Circuit breaker adicional (Polly) — descartado por sobre-ingeniería para el volumen de este MVP; los reintentos nativos de MassTransit ya cubren el requisito.

## 3. Cache-Aside con Redis

- **Decision**: `IDistributedCache` (`Microsoft.Extensions.Caching.StackExchangeRedis`) con claves `events:list` y `events:{id}` (TTL 60s); invalidación explícita de `events:list` tras cada `POST /events` exitoso.
- **Rationale**: Cache-Aside es más simple que Read-Through/Write-Through y no acopla el flujo de escritura (que ya pasa por Outbox/EF) a la capa de caché — evita que un fallo de Redis bloquee la creación de eventos.
- **Alternatives considered**: Write-Through (actualizar el caché en el propio `POST`) — descartado porque acoplaría innecesariamente la transacción de escritura al estado del caché.

## 4. Seguridad Fase 1 (JWT local + token de demo)

- **Decision**: Validación JWT Bearer HMAC-SHA256 nativa de ASP.NET Core (`AddAuthentication().AddJwtBearer(...)`), secreto vía `Jwt__SigningKey` (variable de entorno). El token de demo se genera con un pequeño script de consola (`tools/generate-demo-token` o snippet documentado en el README) que firma un JWT con `sub`, `role: Admin|User` y `exp`, usando el mismo secreto de `.env`. Ese token se pega como constante en el Frontend.
- **Rationale**: No existe emisor real ni login en el MVP (Constitución §5.1); generar el token offline con el mismo secreto configurado es la única forma de que `EventService` lo acepte sin introducir un IdP.
- **Alternatives considered**: Emitir el token desde un endpoint temporal de `EventService` — descartado porque el reto/Constitución piden explícitamente "sin endpoint de login" en esta fase.
- **Detalle adicional (cierre de checklist de seguridad)**: el token de demo se genera con `exp` = 24 horas desde su emisión (suficiente para una sesión de evaluación, sin quedar vigente indefinidamente); `Jwt__SigningKey` debe tener al menos 32 bytes de longitud (ver §4b). Las credenciales de Postgres/RabbitMQ se manejan con el mismo criterio que el secreto JWT: solo vía variables de entorno (`.env`, nunca versionado), nunca impresas en logs. El logging de intentos de autenticación fallidos (para detección de fuerza bruta) queda **explícitamente fuera de alcance** de este MVP — se propone como candidato natural para la fase de Observabilidad completa (Spec 02), no se pierde de vista, solo se difiere.

## 4b. Endurecimiento de la validación JWT (alg confusion, arranque inseguro)

- **Decision**: `TokenValidationParameters` fija explícitamente `ValidAlgorithms = [SecurityAlgorithms.HmacSha256]` (nunca se acepta `alg: none` ni RS/ES aunque el token los declare). Al arrancar, `EventService` valida que `Jwt__SigningKey` esté presente y tenga una longitud mínima (≥ 32 bytes para HMAC-SHA256); si falta o es demasiado corta, el proceso **no arranca** (falla rápido) en vez de aceptar un default inseguro (FR-022).
- **Rationale**: Sin fijar `ValidAlgorithms`, algunas librerías JWT son vulnerables a ataques de confusión de algoritmo (ej. aceptar `alg: none`). Fallar rápido ante un secreto ausente/débil evita que el servicio quede "funcionando" con una validación de facto inexistente.
- **Alternatives considered**: Confiar en los defaults de la librería — descartado porque no documentan explícitamente el comportamiento ante `alg: none`, y es una mitigación de bajo costo y alto valor.

## 4c. Respuesta uniforme ante fallas de autenticación

- **Decision**: Un token ausente, mal formado, con firma inválida o expirado producen la misma respuesta `401` genérica (mismo body `ProblemDetails`, sin distinguir el motivo exacto).
- **Rationale**: Evita convertir el endpoint en un oráculo que permita a un atacante distinguir "casi válido" (expirado) de "inválido" (firma incorrecta) para tantear tokens.
- **Alternatives considered**: Mensajes de error específicos por motivo — descartado por el riesgo de information leakage que no aporta valor a un cliente legítimo.

## 5. Rate Limiting

- **Decision**: `Microsoft.AspNetCore.RateLimiting` (nativo de .NET, sin librería externa) con políticas `FixedWindow`: 20 req/min/IP en `POST /events`, 100 req/min/IP en `GET /events*`; respuesta `429`.
- **Rationale**: Viene incluido en el framework — cumple la restricción de "solo librerías OSS/gratuitas" sin añadir una dependencia nueva.
- **Alternatives considered**: `AspNetCoreRateLimit` (paquete de terceros) — descartado porque el rate limiter nativo ya cubre el caso `FixedWindow` requerido.
- **Nota sobre el entorno de demo (cierre de checklist de seguridad)**: en `docker compose` local, todo el tráfico proviene de la misma IP (`localhost`/red del host), por lo que el límite por IP tiene valor demostrativo/de diseño más que preventivo real en esta topología — se documenta como decisión consciente pensada para el entorno de producción futuro, no como un descuido. El límite aplica igual a tráfico autenticado y no autenticado (no hay excepción para `Admin`), para mantener la política simple y uniforme en el MVP. Un `X-Correlation-Id` con formato inválido no se rechaza: se usa igual como valor de log (es un identificador de trazabilidad, no un dato de seguridad), documentado así para evitar tratarlo como una superficie de validación adicional innecesaria.
- **Replay de tokens (aceptado)**: no existe revocación de tokens en esta fase (no hay lista de revocación ni rotación de sesión); un JWT válido capturado sigue siendo válido hasta su expiración (24h). Se acepta como limitación conocida del esquema "JWT local sin IdP" de la Fase 1 (Constitución §5.1) — se resuelve naturalmente al migrar a Keycloak en Fase 3.

## 6. Health Checks

- **Decision**: `AspNetCore.HealthChecks.NpgSql` y `AspNetCore.HealthChecks.Redis` (paquetes OSS de la comunidad `Xabaril/AspNetCore.Diagnostics.HealthChecks`) para Postgres/Redis; para RabbitMQ se usa el health check **nativo que `AddMassTransit` ya registra automáticamente** (tag `ready`) en vez de un paquete Xabaril separado. `/health/live` como check trivial siempre-`200` (`Predicate = _ => false`, no ejecuta ningún check registrado). `NotificationService` expone el mismo patrón en un host Kestrel liviano (mismo proceso que el `BackgroundService`, vía `WebApplication.CreateBuilder` en vez de `Host.CreateApplicationBuilder`), sin checks de Redis (no lo usa).
- **Rationale**: Son los paquetes estándar de facto para health checks de Postgres/Redis en .NET, y se integran directamente con `healthcheck` de Docker Compose. Para RabbitMQ, `AspNetCore.HealthChecks.Rabbitmq` 9.0.0 expone una API de `AddRabbitMQ` basada en `Func<IServiceProvider, IConnection>` con `RabbitMQ.Client` 7.x (asíncrono en su mayoría), que resultó frágil de cablear correctamente contra la versión de `MassTransit.RabbitMQ` usada aquí; el health check que MassTransit ya expone de fábrica cubre el mismo caso (conectividad al broker) sin una dependencia adicional ni ese acoplamiento de versiones.
- **Alternatives considered**: Checks caseros (`TcpClient` a cada puerto) — descartado por reinventar algo ya resuelto y probado. `AspNetCore.HealthChecks.Rabbitmq` — descartado por la fricción de API descrita arriba frente a una alternativa (MassTransit) ya presente en el proyecto y sin costo adicional.

## 7. Testing

- **Decision**:
  - Backend: **xUnit** + **FluentAssertions** para unit tests de dominio (`Event.Create` invariantes) y de `Application` (handlers con fakes/mocks de `IEventRepository`/`IEventCacheService`); **Testcontainers for .NET** + `WebApplicationFactory` para integration tests de `EventService.Api` contra Postgres/RabbitMQ/Redis reales en contenedores efímeros (no mocks de infraestructura crítica).
  - Frontend: **Vitest** + **React Testing Library** para el formulario de creación de evento (validaciones inline, estado loading/error, zonas dinámicas).
- **Rationale**: Es el stack de testing estándar de facto para .NET 10 + React/Vite; Testcontainers evita falsos positivos de tests de integración que mockean la infraestructura que el reto pide validar (mensajería, outbox, idempotencia).
- **Alternatives considered**: NUnit/MSTest — descartados sin razón de peso para cambiar respecto al estándar más extendido (xUnit) en proyectos .NET modernos.

## 8. Frontend — cliente HTTP

- **Decision**: `fetch` nativo del navegador, sin Axios ni otra librería HTTP.
- **Rationale**: El Spec 01 (§5.1) ya lo pide explícitamente; para un único formulario con una sola llamada `POST`, una dependencia adicional no aporta valor.
- **Alternatives considered**: Axios — descartado por no ser necesario a esta escala.

## 9. Migraciones y seed de datos

- **Decision**: `dbContext.Database.Migrate()` en `Program.cs` de cada servicio al arrancar (auto-aplica migraciones EF Core); comando manual equivalente (`dotnet ef database update --project ...`) documentado en el README como alternativa. Seed: si `Events` está vacío al iniciar, `EventService` inserta un evento de ejemplo con 2 zonas.
- **Rationale**: Es el enfoque más simple para un entorno 100% local de un solo desarrollador/evaluador; evita depender de un contenedor "migrator" separado, que sería sobre-ingeniería para este MVP.
- **Alternatives considered**: Contenedor dedicado de migraciones en `docker-compose` — descartado por complejidad innecesaria a esta escala.
- **Detalle adicional (cierre de checklist de DB)**:
  - Si `Database.Migrate()` falla al arrancar (ej. migración inválida), el servicio **no debe** iniciar en un estado parcial — debe fallar rápido y salir con código de error distinto de cero, para que Docker Compose lo reporte como `unhealthy`/reiniciando en vez de aceptar tráfico con un esquema inconsistente.
  - "Tabla `Events` vacía" (FR-020) significa específicamente `COUNT(*) = 0` sobre `Events` al momento del arranque — no una verificación del contenido del evento de ejemplo en sí; el evento sembrado tiene una forma fija y conocida (nombre, fecha y 2 zonas específicas) para que el criterio de aceptación sea automatizable.
  - No se contempla más de una instancia concurrente de cada servicio en este MVP (topología de un solo contenedor por servicio en `docker-compose`), por lo que no se requiere un mecanismo de lock para migraciones concurrentes; se documenta como supuesto de escala, no como brecha.
  - La versión de PostgreSQL (16) es una decisión de esta fase de planificación, no una restricción de la Constitución (que solo exige "PostgreSQL" sin fijar versión) — queda documentada aquí como la versión de referencia para `docker-compose.yml`.
  - Cada base de datos (`eventdb`, `notificationdb`) se accede con un usuario de Postgres con permisos acotados a esa única base — ningún servicio recibe credenciales con acceso a la base del otro, reforzando a nivel de infraestructura la regla de "sin acceso cruzado" ya impuesta a nivel de arquitectura (Constitución §7.3).
