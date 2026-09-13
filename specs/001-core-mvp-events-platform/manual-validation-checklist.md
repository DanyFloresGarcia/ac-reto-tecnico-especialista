# Checklist de Validación Manual: Core MVP - Plataforma de Eventos

Guía paso a paso para validar manualmente, contra el sistema real (`docker compose up` corriendo), los objetivos centrales que pide el reto técnico: desacoplamiento, mensajería, consistencia, resiliencia, reintentos, idempotencia, caché y observabilidad mínima.

**Diferencia con [quickstart.md](./quickstart.md)**: `quickstart.md` está organizado por Historia de Usuario (US1-US5) y sirve para validar que el flujo funcional funciona de punta a punta. Este documento está organizado por **atributo de calidad no funcional** — el mismo tipo de checklist con el que se suele evaluar un reto técnico de arquitectura event-driven. Varios pasos se apoyan en comandos ya documentados ahí; se referencian en vez de duplicarse.

**Prerrequisitos**: `docker compose up --build -d` corriendo, y `ADMIN_TOKEN`/`USER_TOKEN` generados (README §5).

---

## Estado actual de validación

Actualizá esta tabla a mano a medida que corras cada sección — es tu registro de avance, no se actualiza solo.

| # | Ítem | Estado | Nota |
|---|------|--------|------|
| 1 | [Desacoplamiento](#1-desacoplamiento-sin-http-síncrono-entre-servicios) | ✅ OK | Confirmado: con `api-notifications` detenido, `POST /events` igual respondió `201` |
| 2 | [Mensajería (colas)](#2-mensajería-rabbitmq--estructura-del-mensaje-eventcreated) | ✅ OK | Validado en vivo: se detuvo `api-notifications`, se capturó el mensaje con Get Message(s) (Nack requeue true) y se confirmó la estructura contra el contrato |
| 3 | [Consistencia](#3-consistencia-transactional-outbox) | ✅ OK | Validado en BD, RabbitMQ y logs |
| 4 | [Resiliencia (intermitencia BD)](#4-resiliencia-reintentos-agotados--dead-letter-queue) | ✅ OK | Probadas ambas opciones: A (BD caída) cae en DLQ pero no registra `Failed` salvo que la BD vuelva antes de agotar reintentos; B (SMTP falla) agota reintentos y sí registra `Status = Failed` en `AuditLog` |
| 5 | [Reintentos & Idempotencia](#5-reintentos--idempotencia-mismo-mensaje-entregado-2-veces--se-procesa-1-sola) | ✅ OK | Validado en vivo: se capturó el payload/`messageId` de un mensaje ya procesado y se republicó al exchange `EventCreated`; `api-notifications` lo ignoró (log `"Mensaje duplicado ignorado"`) y `AuditLog` siguió con 1 sola fila para ese `MessageId` |
| 6 | [Caché y Estado (Redis)](#6-caché-y-estado-redis) | ✅ OK | Se puebla en lectura (no en el `POST`), TTL 60s |
| 7 | [Proveedor de Identidad (IdP)](#7-proveedor-de-identidad-idp-local--jwt--roles) | ✅ OK | Validado localmente (JWT + roles, sin IdP real por diseño de Fase 1) |
| 8 | [Notificación por correo](#8-notificación-por-correo-smtp-real) | ✅ OK | Correo real recibido vía SMTP (Gmail) |
| 9 | [Observabilidad Mínima](#9-observabilidad-mínima) | ✅ OK | Cumple el mínimo exigido a nivel local (logs estructurados + correlationId de punta a punta + health checks reales); trazas/métricas completas quedan para Fase 2 (Spec 02), fuera de alcance de este MVP |

**Leyenda**: ✅ OK (validado) · ⏳ PENDIENTE (falta correr la prueba) · 🟡 MEJORA (cumple lo mínimo exigido, mejorable pero no bloqueante) · ❌ FALLA (algo no se comporta como se espera)

---

## 1. Desacoplamiento (sin HTTP síncrono entre servicios)

**Qué se valida**: `EventService` y `NotificationService` nunca se llaman entre sí por HTTP — el único canal es RabbitMQ.

**Cómo probarlo**:
- Revisión de código: `EventService` no tiene ningún `HttpClient`/URL apuntando a `NotificationService`, ni viceversa (`grep -ri "notification" src/EventService --include=*.cs` no debería devolver ninguna llamada HTTP).
- Prueba en caliente: `docker compose stop api-notifications`, crear un evento vía `POST /events` — debe seguir respondiendo `201` normalmente (no depende de que `NotificationService` esté arriba para aceptar la escritura). Reiniciar con `docker compose start api-notifications`: el mensaje pendiente en la cola se procesa apenas vuelve a estar disponible.

**Resultado esperado**: la creación de eventos nunca falla ni se bloquea por la disponibilidad de `NotificationService`.

---

## 2. Mensajería (RabbitMQ + estructura del mensaje `EventCreated`)

**Cómo probarlo**:
1. Abrir RabbitMQ Management UI: `http://localhost:15672` (`guest`/`guest` salvo que lo hayas cambiado en `.env`).
2. Crear un evento (Frontend, curl o Postman).
3. En la pestaña **Queues**, entrar a la cola de `NotificationService` y usar **Get Message(s)** para ver el payload real tal como viaja por la cola.
4. Comparar esa estructura contra el contrato documentado en [contracts/event-created-message.md](./contracts/event-created-message.md).

**Importante — con el flujo normal corriendo no vas a alcanzar a verlo**: `NotificationService.Worker` tiene un consumer activo escuchando esa cola, así que el mensaje se procesa y se ackea en milisegundos — mucho más rápido que hacer click en "Get Message(s)" desde la UI. Vas a ver `Ready: 0` siempre. Para capturarlo de verdad:
```bash
docker compose stop api-notifications
```
Creá el evento (ahora nadie lo consume, queda `Ready: 1`), inspeccionalo con **Get Message(s)** eligiendo **Ack Mode: "Nack message requeue true"** (así no lo perdés, vuelve a la cola), y reiniciá el consumer para que siga su flujo normal:
```bash
docker compose start api-notifications
```

**Resultado esperado**: el mensaje contiene `eventId`, `name`, `occurredAt`, `correlationId` y un `messageId` (usado para idempotencia), coincidiendo con el contrato.

**Resultado observado**: confirmado — se detuvo `api-notifications`, se capturó el mensaje con **Get Message(s)** (`Ack Mode: Nack message requeue true`) y la estructura del payload coincide con el contrato documentado.

---

## 3. Consistencia (Transactional Outbox)

**Qué se valida**: nunca existe un evento persistido sin su mensaje encolado, ni un mensaje publicado sin el evento persistido (atomicidad).

**Cómo probarlo**: crear un evento y, en la misma operación, verificar ambos lados:
```sql
SELECT * FROM public."Events" WHERE "Name" = 'tu-evento-de-prueba';
```
```bash
docker compose logs api-notifications --tail=20   # debe mostrar el EventCreatedConsumer procesándolo
```
Ambos deben reflejar el mismo evento — nunca uno sin el otro. La garantía real (Transactional Outbox vía MassTransit) hace que esto sea así incluso si el proceso se cae justo después de escribir en la base y antes de publicar a RabbitMQ (el outbox reintenta la publicación en el próximo arranque).

---

## 4. Resiliencia (reintentos agotados → Dead Letter Queue)

**Opción A — forzando el fallo en la base de datos**:
```bash
docker compose stop postgres-notification
```
Crear un evento para disparar el mensaje. Con Postgres caído, el consumer falla en cada intento — seguir en vivo:
```bash
docker compose logs -f api-notifications   # verás los 3 reintentos (5s de espera entre cada uno)
```
Después de ~20s (3 reintentos agotados), restaurar la base para que el fault consumer pueda escribir el registro de fallo:
```bash
docker compose start postgres-notification
```

**Resultado observado (Opción A)**: si la BD permanece caída durante todo el ciclo de reintentos, el mensaje termina únicamente en la cola `..._error` (DLQ nativa) — **no** queda fila `Failed` en `AuditLog`, porque el fault consumer también necesita la BD arriba para poder escribirla. Solo si restaurás la BD **antes** de que se agoten los reintentos, el mensaje llega a procesarse con éxito (ni DLQ ni `Failed`).

**Opción B — forzando el fallo en el envío de correo real** (más simple si ya configuraste SMTP real): poné un `SMTP_PASSWORD` incorrecto en `.env`, `docker compose up --build -d api-notifications`, y creá un evento — la autenticación SMTP falla de verdad, sin tocar la base de datos.

**Resultado observado (Opción B)**: agota los 3 reintentos de envío de correo y el fault consumer sí logra escribir la fila `Failed` en `AuditLog` (la BD nunca estuvo caída en este caso).

**Verificar el resultado esperado**:
1. **RabbitMQ UI** → debe aparecer una cola `..._error` con el mensaje fallido (la Dead Letter Queue nativa) — se cumple con ambas opciones si el fallo persiste todo el ciclo de reintentos.
2. En Postgres:
```sql
SELECT * FROM public."AuditLog" WHERE "Status" = 'Failed';
```
Solo la **Opción B** deja una fila `Failed` para ese `MessageId` — la Opción A depende de la misma BD caída para poder escribir ese registro, así que si esta no se restaura a tiempo, el mensaje no se pierde (queda en la DLQ) pero tampoco queda auditado como `Failed`.

---

## 5. Reintentos & Idempotencia (mismo mensaje entregado 2 veces → se procesa 1 sola)

**Rápido (recomendado)** — hay tests unitarios dedicados exactamente a esto:
```bash
dotnet test tests/NotificationService.Application.Tests
```
Casos relevantes en `EventCreatedConsumerTests.cs`:
- `Consume_WhenMessageIdAlreadyProcessedWithSamePayload_SkipsReprocessing` — duplicado exacto, no reprocesa.
- `Consume_WhenDuplicateRaceLosesUniqueConstraint_DoesNotThrow` — dos entregas simultáneas del mismo mensaje, ninguna rompe ni duplica.
- `Consume_WhenPayloadDiffersUnderSameMessageId_DoesNotOverwriteExisting` — mismo id, payload distinto, conserva el original.
- En `EventCreatedFaultConsumerTests.cs`: `Consume_WhenRetriesExhausted_MarksAuditLogAsFailed` — cubre el punto 4 (Resiliencia) a nivel de código.

**En vivo** (opcional, más laborioso): en RabbitMQ UI, sobre un mensaje ya procesado, **Get Message(s)** (sin descartarlo) para copiar su payload/headers, y **Publish message** al exchange `EventCreated` con esos mismos datos. Confirmar en base:
```sql
SELECT COUNT(*) FROM public."AuditLog" WHERE "MessageId" = '<el id>';
```
Debe seguir dando `1`.

---

## 6. Caché y Estado (Redis)

```bash
curl -w "\n%{time_total}s\n" http://localhost:8080/events -H "Authorization: Bearer $ADMIN_TOKEN"   # 1ra llamada
curl -w "\n%{time_total}s\n" http://localhost:8080/events -H "Authorization: Bearer $ADMIN_TOKEN"   # 2da, debe ser más rápida
docker compose exec redis redis-cli KEYS "events:*"
docker compose exec redis redis-cli GET "events:list"
```

**Invalidación al escribir**: crear un evento nuevo y confirmar que aparece de inmediato en el siguiente `GET /events` (no hay que esperar el TTL de 60s para verlo).

**Resultado esperado**: la 2da llamada es notablemente más rápida que la 1ra (SC-002 del spec), la key `events:list` existe en Redis con el JSON cacheado, y un evento recién creado aparece sin demora.

---

## 7. Proveedor de Identidad (IdP local — JWT + roles)

**Preparar los tokens de prueba**: desde la raíz del repositorio, ejecutar los siguientes comandos usando la misma clave compartida configurada para el entorno local:
```bash
dotnet run --project tools/generate-demo-token -- Admin "qnm268A8KXb/dzmpfCoYV5v5QcquACo5TlFp7ZSHbDk="
dotnet run --project tools/generate-demo-token -- User "qnm268A8KXb/dzmpfCoYV5v5QcquACo5TlFp7ZSHbDk="
```

Guardar cada resultado en una variable de entorno para no pegar el token repetidamente en los comandos:
```bash
export ADMIN_TOKEN='<token generado para Admin>'
export USER_TOKEN='<token generado para User>'
```
En PowerShell, usar `$env:ADMIN_TOKEN = '<token generado para Admin>'` y `$env:USER_TOKEN = '<token generado para User>'`.

Para una validación rápida con los valores demo actuales, los tokens generados son:
```text
ADMIN_TOKEN=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJkZW1vLWFkbWluIiwicm9sZSI6IkFkbWluIiwiZXhwIjoxNzg5NDA1ODg2fQ.Q0AEy7Rmq-ZUgFxIVm2jZxiRqDJl48xdGWhE2MFjd70
USER_TOKEN=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJkZW1vLXVzZXIiLCJyb2xlIjoiVXNlciIsImV4cCI6MTc4OTQwNTk5Mn0.X-9Z0O1xxo9McPsNIQy0iwetvsvMQHhLs6JQkDSIirw
```
Si estos tokens están vencidos, regenerarlos con los comandos anteriores.

**Matriz de autorización** (los 3 casos que definen FR-011/FR-012/SC-006):
```bash
curl -i -X POST http://localhost:8080/events -d '{}'                                          # sin token → 401
curl -i -X POST http://localhost:8080/events -H "Authorization: Bearer $USER_TOKEN" -d '{}'   # rol User → 403
curl -i -X POST http://localhost:8080/events -H "Authorization: Bearer $ADMIN_TOKEN" -d '...' # rol Admin → 201
```
También podés correr la carpeta **"Eventos - autorización (matriz)"** de la [collection de Postman](../../postman/) (o `npx newman run ...`), que ya tiene estos 3 casos armados con sus asserts.

**Resultado esperado**: `401` (no autenticado) y `403` (autenticado pero sin privilegio) son respuestas distintas y consistentes — nunca un `500` ni un `404` disfrazando el motivo real.

---

## 8. Notificación por correo (SMTP real)

**Qué se valida**: el correo llega de verdad, no solo queda simulado en logs (ver [research.md §8b](./research.md)).

**Cómo probarlo**:
1. Crear o usar una cuenta remitente de Gmail con verificación en dos pasos habilitada.
2. Generar una contraseña de aplicación en Gmail y usarla como `SMTP_PASSWORD`. Para la cuenta remitente de prueba se dispone actualmente de esta contraseña de aplicación:
	```text
	ygky umxo qqle iwkn
	```
3. Completar en `.env` `SMTP_HOST`/`SMTP_PORT`/`SMTP_USERNAME`/`SMTP_PASSWORD`/`SMTP_TO_ADDRESS` (README §5). Para Gmail, `SMTP_HOST` suele ser `smtp.gmail.com`, `SMTP_PORT` `587`, `SMTP_USERNAME` la cuenta remitente y `SMTP_PASSWORD` la contraseña de aplicación, no la contraseña normal de Gmail.
4. Reiniciar el servicio de notificaciones para cargar la configuración:
	```bash
	docker compose up --build -d api-notifications
	```
	El `--build` es necesario si cambió la configuración incluida en la imagen. Si también se reinició todo el entorno, usar `docker compose up --build -d`.
5. Crear un evento usando el token de `Admin`.
6. Revisar la bandeja de `SMTP_TO_ADDRESS` (y la carpeta de spam si el destinatario es un dominio corporativo).
7. Confirmar en el log:
rmar en el log:
`/fecha. Si `SMTP_HOST` queda vacío, el sistema sigue funcionando en modo simulado (solo log) — ambos modos son válidos, éste es el que confirma el envío real.

**Relación con el punto 4 (Resiliencia)**: forzar una contraseña SMTP incorrecta es la "Opción B" para probar reintentos/DLQ sin tocar la base de datos — mismo mecanismo, disparado desde un punto de falla distinto.

---

## 9. Observabilidad Mínima

**Logs estructurados JSON + `correlationId` de punta a punta**:
```bash
curl -i http://localhost:8080/events -H "Authorization: Bearer $ADMIN_TOKEN" -H "X-Correlation-Id: mi-id-de-prueba-123"
docker compose logs api-event | grep "mi-id-de-prueba-123"
docker compose logs api-notifications | grep "mi-id-de-prueba-123"
```
**Resultado esperado**: el mismo `correlationId` aparece en los logs de **ambos** servicios, en formato JSON, permitiendo rastrear una operación de punta a punta.

**Health checks reales** (no solo "el proceso está vivo"):
```bash
curl http://localhost:8080/health/live
curl http://localhost:8080/health/ready    # chequea Postgres/Redis/RabbitMQ de verdad
curl http://localhost:8081/health/live
curl http://localhost:8081/health/ready
```
Para confirmar que `/health/ready` depende de verdad de sus dependencias: `docker compose stop redis`, volver a pegarle a `/health/ready` de `api-event` — debe reportar no-listo hasta reiniciar Redis (`docker compose start redis`).

---

## Notas

- Este documento no reemplaza a `quickstart.md` — lo complementa con una vista organizada por atributo de calidad, útil para una revisión tipo checklist de evaluación técnica.
- Ningún comando de esta guía requiere modificar código; son todas verificaciones de caja negra contra el sistema ya corriendo.
- Si algún paso requiere reconstruir una imagen (ej. cambiar `SMTP_PASSWORD`), se indica explícitamente con `docker compose up --build -d <servicio>`.
