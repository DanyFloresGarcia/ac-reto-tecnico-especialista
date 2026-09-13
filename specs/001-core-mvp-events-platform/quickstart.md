# Quickstart: Validación end-to-end del MVP

Prerrequisitos: Docker Desktop (o Podman), .NET 10 SDK, Node 18+. Ver [contracts/events-api.md](./contracts/events-api.md) y [contracts/event-created-message.md](./contracts/event-created-message.md) para el detalle de request/response y del mensaje de integración; ver [data-model.md](./data-model.md) para las entidades.

## 1. Levantar el entorno

```bash
cp .env.example .env      # copy .env.example .env  en Windows
docker compose up --build
```

Verificar que todos los contenedores lleguen a `healthy` (`docker compose ps`): `postgres-event`, `postgres-notification`, `rabbitmq`, `redis`, `api-event`, `api-notifications`.

## 2. Obtener el token de demo

Ejecutar el script/snippet documentado en el README (`tools/generate-demo-token` o equivalente) para generar un JWT firmado con el mismo `Jwt__SigningKey` del `.env`, con `role: Admin`. Guardarlo en una variable de entorno local para los pasos siguientes:

```bash
export ADMIN_TOKEN="<jwt generado>"
```

## 3. Camino feliz — Historia 1 y 2 del spec (crear + listar)

```bash
curl -i -X POST http://localhost:<puerto>/events \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -H "X-Correlation-Id: $(uuidgen)" \
  -d '{
    "name": "Concierto Rock en el Parque",
    "date": "2026-12-01T20:00:00Z",
    "location": "Estadio Nacional, Lima",
    "zones": [
      { "name": "General", "price": 50.00, "capacity": 500 },
      { "name": "VIP", "price": 150.00, "capacity": 100 }
    ]
  }'
```

**Esperado**: `201 Created` + header `Location` + `EventDto` en el body (contrato en `contracts/events-api.md`).

```bash
curl -i http://localhost:<puerto>/events -H "Authorization: Bearer $ADMIN_TOKEN"
curl -i http://localhost:<puerto>/events -H "Authorization: Bearer $ADMIN_TOKEN"  # segunda llamada
```

**Esperado**: ambas `200 OK` con el evento recién creado incluido; la segunda llamada debe ser notablemente más rápida (servida desde `events:list` en Redis — verificable con `docker compose logs api-event` o `redis-cli KEYS "events:*"`), cumpliendo SC-002 del spec.

```bash
curl -i http://localhost:<puerto>/events/00000000-0000-0000-0000-000000000000" -H "Authorization: Bearer $ADMIN_TOKEN"
```

**Esperado**: `404 Not Found` (SC-003 del spec).

## 4. Verificar la notificación asíncrona — Historia 1 (escenario 3) y contrato de mensaje

```bash
docker compose logs api-notifications --tail=50
```

**Esperado**: log del `EventCreatedConsumer` mostrando el `correlationId` usado en el paso 3, y el contenido del correo simulado (MailKit) impreso en consola. Verificar también en RabbitMQ Management UI (`http://localhost:15672`) que la cola del consumer procesó el mensaje.

## 5. Idempotencia — Historia 4, escenario 1

Reenviar manualmente el mismo mensaje (mismo `messageId`) a la cola — por ejemplo, republicando desde la UI de RabbitMQ el mensaje capturado en el paso 4, o usando un pequeño script de prueba que invoque el mismo `EventCreatedConsumer` con el mismo payload.

**Esperado**: el log muestra "mensaje duplicado ignorado"; consultando `notificationdb` (`SELECT COUNT(*) FROM "AuditLog" WHERE "MessageId" = '<id>'`) el conteo sigue siendo `1` (SC-004 del spec).

## 6. Resiliencia y DLQ — Historia 4, escenario 2

Forzar un fallo en el consumer (por ejemplo, deteniendo temporalmente `postgres-notification` antes de publicar un nuevo evento, o mediante un flag de test que lance una excepción en `EventCreatedConsumer`).

**Esperado**: tras 3 reintentos (5s de espera entre cada uno), el mensaje aparece en la cola `_error` de RabbitMQ, y `notificationdb` tiene una fila con `Status = Failed` para ese `messageId` (SC-005 del spec).

## 7. Seguridad por rol — Historia 3

```bash
curl -i -X POST http://localhost:<puerto>/events -d '{}'                                    # sin token
curl -i -X POST http://localhost:<puerto>/events -H "Authorization: Bearer $USER_TOKEN" -d '...'  # rol User
curl -i -X POST http://localhost:<puerto>/events -H "Authorization: Bearer $ADMIN_TOKEN" -d '...' # rol Admin
```

**Esperado**: `401`, `403`, `201` respectivamente (SC-006 del spec).

## 8. Frontend

Abrir `http://localhost:5173`, completar el formulario "Registrar Evento" (agregar al menos una zona) y guardar. Verificar: validaciones inline si se deja un campo vacío o una zona con capacidad 0; estado de carga mientras se envía; el evento creado aparece luego en `GET /events`.

## 9. Arranque limpio — Historia 5

Repetir el paso 1 en una máquina distinta (o tras `docker compose down -v` para limpiar volúmenes) siguiendo únicamente el README, sin ningún paso adicional no documentado (SC-008 y SC-009 del spec).
