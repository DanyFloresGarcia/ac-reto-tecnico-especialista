# Contract: Mensaje de integración `EventCreated` (RabbitMQ, vía MassTransit)

Este es el único canal de comunicación entre `EventService` (productor) y `NotificationService` (consumidor) — nunca hay llamadas HTTP directas entre ambos (Constitución §4).

## Schema (JSON serializado por MassTransit)

```json
{
  "messageId": "uuid",
  "eventId": "uuid",
  "name": "string",
  "occurredAt": "ISO-8601 datetime UTC",
  "correlationId": "uuid",
  "version": 1
}
```

| Campo | Tipo | Origen / Regla |
|---|---|---|
| `messageId` | `uuid` | `Guid.NewGuid()` generado al construir el mensaje en `EventService`. Es la clave de idempotencia que usa `NotificationService` (único índice sobre `AuditLog.MessageId`). |
| `eventId` | `uuid` | El `Id` del `Event` recién creado. |
| `name` | `string` | El `Name` del `Event`. |
| `occurredAt` | ISO-8601 | Momento en que se creó el evento de dominio. |
| `correlationId` | `uuid` | Tomado del header HTTP `X-Correlation-Id` de la request original; si el cliente no lo envía, `EventService` genera uno nuevo vía middleware al inicio del request. El mismo valor se usa como campo de log en ambos servicios. |
| `version` | `int` | Fijo en `1` en esta fase. La evolución del contrato (breaking/non-breaking) es una decisión de diseño a 6 meses fuera de este Spec. |

## Productor: `EventService`

- Publica el mensaje dentro de la misma transacción EF Core que persiste `Event`/`Zone`, vía el Outbox de MassTransit (`AddEntityFrameworkOutbox`) — nunca se publica un mensaje para un evento que no quedó durablemente persistido, ni viceversa (FR-008 del spec).

## Consumidor: `EventCreatedConsumer` (`NotificationService`)

Flujo exacto (ver también `data-model.md` §AuditLog para el detalle de la garantía de idempotencia):
1. `SELECT 1 FROM notifications."AuditLog" WHERE "MessageId" = @messageId` como *fast path*. Si existe → loguear "mensaje duplicado ignorado" y salir sin reprocesar (FR-009).
2. Si no existe → simular envío de correo: construir un `MimeMessage` (MailKit) con los campos disponibles en el propio mensaje (`eventId`, `name`, `occurredAt` — el contrato no incluye `location`) e imprimirlo en consola (asunto + cuerpo), dejando explícito en el log que es una simulación — nunca se envía por SMTP real.
3. Insertar la fila en `AuditLog` (`Status = Processed`, `PayloadHash` = SHA-256 del JSON crudo) **dentro de la misma unidad de trabajo** que el paso 2, para que ambos ocurran o ninguno lo haga.
4. La unicidad real de `MessageId` la garantiza una restricción única a nivel de base de datos, no el `SELECT` del paso 1: si el `INSERT` falla por violación de esa restricción (dos entregas procesadas casi en simultáneo), el consumer trata esa excepción específica como "duplicado", no como un fallo de procesamiento.
5. Si `PayloadHash` difiere del ya almacenado para ese `MessageId`, se registra una advertencia en el log; la fila existente no se sobrescribe.

## Fallos: `EventCreatedFaultConsumer` (`IConsumer<Fault<EventCreated>>`)

- Se dispara cuando `UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)))` agota sus 3 intentos.
- Inserta/actualiza la fila correspondiente en `AuditLog` con `Status = Failed` (FR-010), además de que MassTransit mueve el mensaje a la cola de error nativa (`<queue>_error`, la "DLQ" de este Spec).

## Garantías del contrato

- **At-least-once delivery** con **exactly-once processing efectivo** a nivel de negocio, gracias a la verificación de `MessageId` (no es idempotencia a nivel de broker, sino a nivel de consumidor).
- **Nunca se pierde un mensaje silenciosamente**: agota reintentos → DLQ + registro `Failed` consultable.
- El `version: 1` fijo indica que este contrato no tiene aún estrategia de versionado — cualquier cambio de forma en una fase futura debe tratarse como decisión explícita de diseño (fuera de alcance de este Spec).
