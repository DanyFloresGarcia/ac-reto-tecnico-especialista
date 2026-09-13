# Data Model: Core MVP - Plataforma de Eventos

## Convenciones comunes

- **`Entity` (base, compartida entre servicios)**: `Id (Guid)` + igualdad por `Id`. No se persiste por sí misma; la heredan `Event`, `Zone` y `AuditLog` (Constitución §7.5 — herencia mínima y justificada).
- Ningún entity expone setters públicos; el estado solo cambia a través de métodos con nombre de negocio o del factory method de creación.

---

## EventService (`eventdb`)

### `Event` (Aggregate Root)

| Campo | Tipo | Reglas |
|---|---|---|
| `Id` | `Guid` | Generado al crear; PK |
| `Name` | `string` | Requerido, máx. 200 caracteres |
| `Date` | `DateTime` (UTC) | Requerido |
| `Location` | `string` | Requerido, máx. 300 caracteres |
| `Status` | `EventStatus` (enum: `Draft`, `Published`, `Cancelled`) | Se crea siempre en `Draft`; no hay transición de estado en este MVP (sin endpoint para ello) |
| `Zones` | `IReadOnlyCollection<Zone>` | Al menos 1 elemento; inmutable tras la creación en este MVP |

**Invariantes** (impuestas por el factory method `Event.Create(name, date, location, zonasIniciales)`, nunca por un setter externo):
- No puede crearse con una lista de zonas vacía → `DomainException`.
- Cada zona debe cumplir `Capacity > 0` y `Price >= 0`, si no → `DomainException`.
- La entidad nunca existe en memoria en un estado inválido (ver Constitución §7.5).

**Relaciones**: 1 `Event` → N `Zone` (composición; `Zone` no tiene ciclo de vida propio).

### `Zone` (Entity interna del Aggregate `Event`)

| Campo | Tipo | Reglas |
|---|---|---|
| `Id` | `Guid` | Generado al crear |
| `EventId` | `Guid` | FK a `Event`, **indexada** (soporta la carga de zonas por evento vía `GetEventByIdQuery`); nunca se expone para creación/edición independiente |
| `Name` | `string` | Requerido, máx. 100 caracteres |
| `Price` | `decimal(10,2)` | `>= 0`; precisión y escala fijas a nivel de columna para evitar ambigüedad de redondeo entre dominio y persistencia |
| `Capacity` | `int` | `> 0` |

**Límite de longitud de `Zone.Name` (cierre de punto diferido en `/speckit-clarify`)**: se fija en 100 caracteres — la mitad del límite de `Event.Name` (200), ya que un nombre de zona ("General", "VIP", "Platea Baja") es estructuralmente más corto que el nombre de un evento. Mantiene el mismo criterio ya aplicado a `Event.Name`/`Event.Location`: un máximo explícito evita valores de longitud arbitraria sin necesidad de una regla de negocio más compleja.

**Integridad referencial**: `Zone.EventId` usa `ON DELETE CASCADE` hacia `Event` — aunque este MVP no expone un endpoint de eliminación, se fija esta regla ahora para que el esquema no quede en un estado ambiguo (huérfanos) si una fase futura la agrega.

### `EventStatus` (enum)

`Draft = 0`, `Published = 1`, `Cancelled = 2`. Solo `Draft` es alcanzable en este MVP; los otros dos valores existen en el modelo para no romper el contrato de datos cuando una fase futura añada las transiciones (fuera de alcance de este Spec).

### `EventCreatedDomainEvent` (evento de dominio interno, no confundir con el mensaje de integración `EventCreated` de RabbitMQ)

Disparado por `Event.Create(...)`; consumido dentro de `EventService.Application` para construir y encolar el mensaje de integración descrito en [`contracts/event-created-message.md`](./contracts/event-created-message.md) dentro de la misma transacción (vía Outbox).

### `DomainException`

Excepción de dominio específica lanzada únicamente por invariantes violadas dentro de `EventService.Domain` (nunca por validación de formato/API, que es responsabilidad de FluentValidation en `Application`).

### `OutboxMessage`

Gestionada íntegramente por `AddEntityFrameworkOutbox` de MassTransit — no se modela como entidad de dominio propia; se documenta aquí solo como parte del esquema de `eventdb` porque vive en la misma base de datos.

**Retención**: MassTransit purga automáticamente los mensajes ya entregados de `OutboxMessage`/`OutboxState`/`InboxState` según su configuración por defecto. Para la escala de este MVP (demo local, bajo volumen) no se requiere una política de limpieza adicional; se documenta como decisión explícita, no como omisión — una tarea de mantenimiento periódico quedaría fuera de alcance de este Spec.

**Ownership de las migraciones**: aunque MassTransit gestiona el contenido de estas tablas, su *schema* (creación de las tablas en sí) se aplica como una migración más del propio `EventDbContext` de `EventService.Infrastructure` — no hay un mecanismo de migración externo a ese servicio ni acoplamiento con `NotificationService` (Constitución §7.3).

---

## NotificationService (`notificationdb`)

### `AuditLog` (entidad simple, sin tratamiento DDD rico — ver Constitución §7.5)

| Columna | Tipo | Reglas |
|---|---|---|
| `Id` | `uuid` (PK) | Generado al insertar |
| `MessageId` | `uuid` | **Índice único** — clave de idempotencia (FR-009 del spec) |
| `EventId` | `uuid` | Tomado del mensaje `EventCreated` |
| `EventName` | `text` | Tomado del mensaje `EventCreated` |
| `OccurredAt` | `timestamptz` | Tomado del mensaje `EventCreated` |
| `CorrelationId` | `uuid` | Tomado del mensaje `EventCreated`; usado también como campo de log |
| `PayloadHash` | `text` | SHA-256 del JSON crudo recibido — detecta payloads distintos bajo el mismo `MessageId` |
| `ProcessedAt` | `timestamptz` | Momento en que `NotificationService` lo procesó (éxito) o lo marcó fallido |
| `Status` | `text`/enum | `Processed` \| `Failed` |

**Garantía de idempotencia (FR-009)**: la unicidad de `MessageId` se garantiza con una **restricción única a nivel de base de datos** (`UNIQUE INDEX` sobre `AuditLog.MessageId`), no únicamente con un chequeo previo a nivel de aplicación — un `SELECT` antes del `INSERT` por sí solo deja una ventana de condición de carrera si dos entregas del mismo mensaje se procesan casi simultáneamente (ej. tras una redelivery de MassTransit). El flujo es:
1. `EventCreatedConsumer` hace un `SELECT` previo como *fast path* (evita trabajo innecesario en el caso común de duplicado) y, si encuentra la fila, loguea "mensaje duplicado ignorado" y sale sin reprocesar.
2. Si no la encuentra, simula el envío del correo (MailKit → consola) e inserta la fila con `Status = Processed` **dentro de la misma unidad de trabajo**, de forma que un fallo entre ambos pasos no deje un registro `Processed` sin su correo simulado correspondiente (ver `contracts/event-created-message.md`).
3. Si el `INSERT` falla por violación de la restricción única (dos consumers concurrentes procesando el mismo `MessageId` a la vez), el consumer captura esa excepción específica y la trata como "duplicado" — nunca como un fallo real que dispare reintentos/DLQ.
4. Si `PayloadHash` difiere del ya almacenado para ese `MessageId`, el sistema lo registra como advertencia en el log (posible payload distinto bajo el mismo id) pero **no** sobrescribe la fila existente — la primera entrega procesada es la que prevalece.
5. `EventCreatedFaultConsumer` (`IConsumer<Fault<EventCreated>>`) — al agotarse los reintentos (FR-010), inserta/actualiza la fila correspondiente con `Status = Failed`.

**Relaciones**: Ninguna hacia `EventService` a nivel de datos — el único acoplamiento es el contrato de mensaje (ver `contracts/event-created-message.md`); nunca hay una FK real ni acceso cross-DB.

---

## Roles (transversal, no persistido como entidad de negocio)

`Admin` y `User` son claims (`role`) dentro del JWT, no una tabla de la aplicación — no hay gestión de usuarios en este MVP (Constitución §5.1, Assumption del spec). Se documentan aquí únicamente porque condicionan las reglas de autorización de `EventService.Api` descritas en `contracts/events-api.md`.
