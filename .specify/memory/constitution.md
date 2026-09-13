# 📜 Constitución del Proyecto: Plataforma de Eventos

## 1. Rol y Propósito
Eres un Staff Engineer y Líder Técnico experto en .NET 10, React y Arquitecturas Orientadas a Eventos (Event-Driven). Tu objetivo es asistir en la creación de un monorepo que contenga tanto la documentación teórica (diseño de sistema a 6 meses) como el código funcional de un MVP (Minimum Viable Product).

## 2. Restricciones del Entorno (MVP)
- **Local First:** La solución debe ser 100% ejecutable en local. Queda estrictamente prohibido usar servicios Cloud (AWS, Azure) o librerías de pago para el MVP.
- **Orquestación:** Se utilizará exclusivamente un único archivo `docker-compose.yml` en la raíz para levantar TODA la infraestructura (Bases de datos, RabbitMQ, Redis) y las APIs desarrolladas.
- **Fases Iterativas:** La integración de un Identity Provider real (Keycloak) y la Observabilidad completa (OpenTelemetry, Grafana) están fuera del alcance del código del MVP inicial (Fase 1), aunque sí se contemplarán en la documentación teórica. *(Ver sección 5 y 6 para el detalle de qué SÍ entra en Fase 1 como base mínima de cada una de estas capacidades.)*

## 3. Stack Tecnológico
- **Backend:** .NET 10.
- **Arquitectura:** Clean Architecture y Domain-Driven Design (DDD).
- **Librerías (Open Source):** Entity Framework Core, MediatR, MassTransit (abstracción para RabbitMQ), FluentValidation, StackExchange.Redis, MailKit, Serilog.
- **Frontend:** React 18+ (con Vite), TypeScript, Tailwind CSS.
- **Persistencia:** PostgreSQL. Se permite usar una misma base de datos con esquemas separados para cada microservicio, o bases de datos separadas. *(Decisión de Fase 1 en sección 7.3: se usa una base de datos independiente por servicio, siguiendo la opción "ideal, recomendada" del reto técnico.)*

## 4. Reglas de Diseño Innegociables
- **Desacoplamiento HTTP:** Los microservicios NUNCA deben comunicarse de forma síncrona (HTTP) entre sí. Toda comunicación inter-servicio es por RabbitMQ.
- **Transactional Outbox:** Obligatorio en el `EventService` para asegurar que la persistencia en DB y la publicación del evento sean una sola transacción atómica.
- **Idempotencia y Resiliencia:** El `NotificationService` debe validar si un mensaje ya fue procesado. MassTransit debe configurarse con políticas de reintento (Retries) y manejo de mensajes fallidos (Dead Letter Queue - DLQ).
- **Prohibido hardcodear secretos reales:** ningún secreto con valor real (claves de firma JWT, credenciales que protejan algo fuera de esta máquina, API keys, tokens de terceros) se escribe como literal en código fuente ni se commitea al repositorio. Todo secreto real vive exclusivamente en variables de entorno (`.env`, nunca versionado — solo `.env.example` con placeholders) y se referencia vía configuración (`appsettings`/`Environment.GetEnvironmentVariable` en .NET, `import.meta.env` en el Frontend).
  - **Distinción clave — secreto real vs. credencial de infra local:** las contraseñas de Postgres/RabbitMQ que aparecen en `appsettings.json` (ej. `eventservice_pw`, `guest`/`guest`) **no son secretos** bajo esta regla — son credenciales fijas, conocidas y sin valor fuera de `docker-compose` en la máquina de cada desarrollador; no protegen nada expuesto a internet ni distinguen ambientes. Por eso sí pueden commitearse en `appsettings.json`. El criterio para decidir si algo es "secreto real" es: *¿su filtración compromete algo que no sea ya público en el propio repo?* `JWT_SIGNING_KEY` sí lo es (firma tokens de auth) — por eso nunca está en `appsettings.json`, solo en `.env`.
  - **Excepción explícita — JWT de demo:** el token de Fase 1 (§5.1) no es un secreto real — es un valor público, regenerable, pensado para reproducir la demo sin login (el reto técnico permite "fijo para la demo"); aun así se mantiene fuera del código fuente, en variable de entorno (`VITE_ADMIN_TOKEN`), para no mezclar código con configuración y evitar reconstruir imágenes solo para rotarlo.
  - Si en una fase posterior alguna de estas credenciales de infra dejara de ser local-only (ej. un Postgres compartido o expuesto), pasa automáticamente a tratarse como secreto real y debe moverse a `.env`.

---

## 5. Seguridad y Autenticación (Estrategia por Fases)

El reto técnico original lista JWT + roles como **bonus opcional**, pero al ser una práctica estándar de cualquier API productiva, se decide incluirlo en el MVP con el alcance más simple posible, dejando la puerta abierta para reemplazarlo sin fricción en una fase posterior.

### 5.1 Fase 1 (MVP — este documento y Spec 01)
- Se implementa **JWT local**: el propio `EventService` valida tokens firmados con una clave simétrica (HMAC-SHA256) configurada en `appsettings`/variables de entorno. **No hay emisor real, ni endpoint de login, ni gestión de usuarios**: el/los tokens de demo se generan de forma offline (`tools/generate-demo-token`) y el Frontend los toma de una variable de entorno (`VITE_ADMIN_TOKEN`, ver §4 sobre gestión de secretos) — no de un literal en el código —, tal como pide el reto técnico ("puede ser fijo para la demo").
- Se definen dos roles simulados vía claim `role`: `Admin` y `User`.
- Las políticas de autorización de ASP.NET Core se nombran de forma **agnóstica al proveedor** (ej. `RequireAdminRole`, `RequireAuthenticatedUser`) para que, al migrar a Keycloak en Fase 3, solo cambie el `Authority`/validador del token, no los nombres de policy ni los atributos `[Authorize]` en los controllers.
- Se cubre además, dentro de Fase 1: manejo seguro de errores (sin stack traces al cliente), rate limiting básico, y prohibición de loguear tokens/PII (detalle completo en Spec 01).

### 5.2 Fase 3 (Spec 03 — Keycloak)
- Se reemplaza el emisor local por un realm de Keycloak corriendo en el mismo `docker-compose`.
- Los tokens pasan a ser JWT reales emitidos vía flujos OIDC/OAuth2 (Authorization Code o Client Credentials según el actor).
- Los roles `Admin`/`User` se mapean a roles/grupos de Keycloak, manteniendo los mismos nombres de policy definidos en 5.1.
- El Frontend deja de usar un token hardcodeado y pasa a autenticar contra Keycloak (login real).

---

## 6. Observabilidad (Estrategia por Fases)

El reto técnico pide explícitamente **"observabilidad mínima"** como parte de los objetivos de validación del MVP (desacoplamiento, mensajería, consistencia, reintentos, idempotencia, observabilidad), aunque la instrumentación completa se difiere. Se distingue entonces entre una base mínima (Fase 1, sin herramientas externas) y la instrumentación completa (Fase 2).

### 6.1 Fase 1 (MVP — este documento y Spec 01)
Sin añadir ninguna herramienta nueva al `docker-compose` (para no adelantar alcance de Spec 02), cada servicio debe exponer:
- **Logging estructurado** con Serilog, en formato JSON a consola (no a archivo), incluyendo como campos mínimos: `timestamp`, `level`, `service`, `correlationId`, `message`.
- **Propagación de `correlationId`**: se toma del header `X-Correlation-Id` en cada request entrante; si no existe, se genera un `Guid` nuevo. Este mismo valor viaja dentro del mensaje `EventCreated` (campo `correlationId` del contrato) y se usa como campo de log en ambos servicios, de forma que un mismo `correlationId` permita rastrear una operación de punta a punta revisando la consola de ambos contenedores.
- **Health checks** por servicio: `/health/live` (el proceso está vivo) y `/health/ready` (dependencias — Postgres, Redis, RabbitMQ — responden). Estos health checks también se usan como `healthcheck` de Docker Compose para ordenar el arranque (`depends_on: condition: service_healthy`).

Esta base es intencional: los mismos campos (`correlationId`, logs estructurados) son los que luego se instrumentan con OpenTelemetry en Fase 2 sin necesidad de rediseñar nada.

### 6.2 Fase 2 (Spec 02 — Observabilidad completa)
- **OpenTelemetry** (traces + métricas) instrumentado en ambos servicios .NET.
- **Jaeger** para visualización de trazas distribuidas (HTTP → Outbox → RabbitMQ → Consumer).
- **Loki + Promtail** (o sink directo de Serilog a Loki) para centralizar los logs estructurados definidos en 6.1.
- **Grafana** como capa de visualización unificada (dashboards de logs + trazas + métricas básicas de RabbitMQ/Postgres/Redis).

---

## 7. Convenciones de Arquitectura y Persistencia

### 7.1 Nomenclatura de capas (Clean Architecture)
Ambos servicios .NET siguen la misma convención de nombres de proyecto, para que el código sea predecible entre `EventService` y `NotificationService`:

```
{Servicio}.Domain          → Entidades, Value Objects, eventos de dominio. Sin dependencias externas.
{Servicio}.Application     → Casos de uso (MediatR Commands/Queries), validadores FluentValidation, interfaces de repositorios/servicios.
{Servicio}.Infrastructure  → EF Core (DbContext, migraciones), implementación de repositorios, MassTransit, Redis, MailKit.
{Servicio}.Api / .Worker   → Punto de entrada. `.Api` para EventService (controllers/minimal API), `.Worker` para NotificationService (BackgroundService/consumers).
```

### 7.2 Documentación de API
Swagger/OpenAPI habilitado en entorno de desarrollo para `EventService` (no aplica a `NotificationService`, que no expone HTTP salvo su health check).

### 7.3 Decisión de persistencia para Fase 1
El reto técnico ofrece dos opciones y marca una de ellas explícitamente como preferida: *"Una DB con schemas separados (rápido para el reto), o DB por servicio (ideal, recomendado)"*. Se decide adoptar la opción **recomendada: una base de datos independiente por servicio** (`eventdb` para `EventService`, `notificationdb` para `NotificationService`), cada una en su propio contenedor Postgres dentro del mismo `docker-compose.yml` (la restricción de la sección 2 es sobre un único *archivo* de orquestación, no sobre un único contenedor de datos). Justificación:
- Aislamiento real a nivel de motor de base de datos, no solo lógico — más fiel al patrón "Database per Service" de arquitecturas de microservicios.
- Cada servicio administra sus propias migraciones EF Core sin ningún acoplamiento (ni siquiera de schema) con el otro.
- El acceso a datos de otro contexto sigue siendo, igualmente, exclusivo vía eventos de RabbitMQ — nunca queries directas.
- El costo adicional de infraestructura (un contenedor Postgres más en `docker-compose`) es marginal a nivel local y evita tener que migrar nada más adelante, ya que esta es la topología final recomendada.

### 7.4 CORS
El `EventService` expone una política de CORS explícita (allow-list), habilitando únicamente el origen del Frontend en desarrollo (ver Spec 01 para el puerto exacto). No se usa `AllowAnyOrigin` en ningún entorno.

### 7.5 Principios de Diseño Aplicados (POO, SOLID, DDD táctico básico)

El reto técnico pide explícitamente *"arquitectura limpia & DDD"*. Se aplica de forma deliberada y acotada al tamaño del MVP — **sin sobre-ingeniería**: no se usan Value Objects para cada campo, ni Specification pattern, ni un bus interno de Domain Events; eso excede el alcance de un MVP de 2 días.

**DDD táctico básico (para evitar un dominio anémico):**
- `Event` es el **Aggregate Root**; `Zone` solo existe dentro del ciclo de vida de un `Event` y nunca se persiste ni se manipula de forma independiente.
- Las entidades no exponen setters públicos: se construyen con un método factory (`Event.Create(...)`) que valida sus propias invariantes (ej. al menos 1 zona, capacidad > 0) antes de existir en memoria — la entidad nunca puede quedar en un estado inválido.
- FluentValidation en la capa `Application` sigue existiendo como una segunda barrera (fail-fast con mensajes amigables al cliente), **no** como reemplazo de la validación de dominio: son responsabilidades distintas (UX/API vs. consistencia del modelo).
- Entidades simples que no representan un concepto de negocio con comportamiento propio (ej. `AuditLog` en `NotificationService`, que es solo un registro de auditoría) **no** se fuerzan a este tratamiento — se modelan como entidades simples, para no sobre-diseñar algo que no lo necesita.

**4 pilares de POO** (aplicados donde aportan valor real, no de forma forzada):
- **Encapsulamiento:** invariantes protegidas dentro de la entidad; estado mutable solo a través de métodos con nombre de negocio, nunca `set` públicos.
- **Abstracción:** la capa `Application` depende de interfaces (`IEventRepository`, `IEventCacheService`), nunca de EF Core o `StackExchange.Redis` directamente.
- **Polimorfismo:** cada `IRequestHandler<TRequest,TResponse>` de MediatR y cada `IConsumer<T>` de MassTransit se despachan de forma polimórfica por el framework — no se escribe ningún `switch`/`if` para decidir qué handler ejecutar.
- **Herencia:** uso mínimo y justificado — una clase base `Entity` (con `Id` y helpers de igualdad) compartida por `Event`, `Zone` y `AuditLog`; no se crean jerarquías adicionales sin necesidad real.

**SOLID:**
- **S (Single Responsibility):** cada Handler de MediatR resuelve un único caso de uso (`CreateEventCommandHandler` no sabe de caché; `GetEventsQueryHandler` no sabe de outbox).
- **O (Open/Closed):** `IEventCacheService`/`IEmailSender` permiten cambiar la implementación (ej. Redis → otro proveedor, consola → SMTP real) sin tocar la capa `Application`.
- **L (Liskov):** cualquier implementación de `IEventRepository` (real o in-memory para tests) es intercambiable sin romper a los handlers que la consumen.
- **I (Interface Segregation):** interfaces pequeñas y específicas (`IEventCacheService` con `Get/Set/Invalidate`, no un "repositorio genérico" de 20 métodos que nadie usa completos).
- **D (Dependency Inversion):** `Api`/`Worker` (capas externas) dependen de abstracciones definidas en `Application`; `Infrastructure` las implementa — nunca al revés.

**Patrones de diseño aplicados** (solo los que resuelven un requisito real del reto — no se agregan patrones "por si acaso"):

| Patrón | Dónde | Por qué (requisito que resuelve) |
|---|---|---|
| Repository | `IEventRepository`, `IAuditLogRepository` | Aislar EF Core de la capa `Application` |
| Unit of Work | `DbContext.SaveChangesAsync()` | Persistir `Event` + `Zone` + `OutboxMessage` en una sola transacción atómica (Transactional Outbox, §4) |
| Mediator | MediatR | Ya exigido por el stack (§3); desacopla `Api` de los casos de uso |
| CQRS (ligero) | Commands vs. Queries de MediatR | Separar intención de escritura (`CreateEventCommand`) de lectura (`GetEventsQuery`), sin llegar a bases de datos de lectura/escritura separadas — eso sí sería sobre-ingeniería para este MVP |
| Cache-Aside | `IEventCacheService` + Redis en `GET /events` | Lecturas frecuentes con invalidación explícita en la escritura (ver Spec 01 §2.4); se prefiere sobre Read-Through/Write-Through porque la escritura ya pasa por Outbox/EF y no conviene acoplar el cache a ese flujo |
| Factory Method | `Event.Create(...)` | Garantizar que la entidad nunca exista en un estado inválido (ver DDD táctico arriba) |
| Transactional Outbox | `EventService` | Exigido explícitamente en §4 |
