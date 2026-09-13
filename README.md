# Plataforma de Eventos — Core MVP

Monorepo del MVP (Fase 1) de una plataforma de gestión de eventos: dos microservicios .NET 10 desacoplados por mensajería (RabbitMQ), un Frontend React, y toda la infraestructura levantable con un único comando de Docker Compose.

Ver [`.specify/memory/constitution.md`](.specify/memory/constitution.md) para las decisiones de arquitectura y [`specs/001-core-mvp-events-platform/`](specs/001-core-mvp-events-platform/) para la especificación completa (spec, plan, contratos, modelo de datos).

## 1. Prerrequisitos

- Docker Desktop (o Podman) — con el motor realmente iniciado.
- .NET 10 SDK (solo necesario para generar/regenerar el token de demo o correr tests fuera de contenedores).
- Node 18+ (solo necesario para correr el Frontend fuera de Docker con `npm run dev`).

Ninguno de los pasos siguientes asume rutas absolutas, puertos ya ocupados, ni variables de entorno ya configuradas en tu sistema — todo se declara en el propio repo.

## 2. Clonar y configurar

```bash
git clone <url-del-repo>
cd ac-reto-tecnico-lt
cp .env.example .env        # Windows: copy .env.example .env
```

Abrí `.env` y reemplazá `JWT_SIGNING_KEY` por un valor propio de al menos 32 bytes (el sistema **falla al arrancar** si no lo hacés — es intencional, ver spec FR-022):

```bash
# cualquiera de estas dos formas sirve
openssl rand -base64 32
# o simplemente escribí a mano una cadena de 32+ caracteres
```

## 3. Levantar todo

```bash
docker compose up --build
```

Vas a ver `postgres-event`, `postgres-notification`, `rabbitmq`, `redis`, `api-event` y `api-notifications` reportarse `healthy` en orden (cada uno espera a sus dependencias reales, no a un timer fijo). El `frontend` arranca al final.

Verificá el estado con:

```bash
docker compose ps
```

## 4. Migraciones y datos iniciales

Las migraciones de EF Core se aplican **automáticamente** al arrancar cada servicio (`dbContext.Database.Migrate()` en `Program.cs`). Si preferís aplicarlas manualmente en lugar de dejar que el arranque lo haga:

```bash
dotnet ef database update --project src/EventService/EventService.Infrastructure --startup-project src/EventService/EventService.Api
dotnet ef database update --project src/NotificationService/NotificationService.Infrastructure --startup-project src/NotificationService/NotificationService.Worker
```

**Seed automático**: si la tabla `Events` está vacía, `EventService` inserta al arrancar un evento de ejemplo ("Concierto Rock en el Parque", 2 zonas) para que `GET /events` nunca devuelva una lista vacía en la primera prueba.

## 5. Token JWT de demo

Este MVP no tiene login real (Fase 1 — ver Constitución §5.1): se usa un JWT firmado offline con el mismo `Jwt__SigningKey` de tu `.env`. Generalo así:

```bash
dotnet run --project tools/generate-demo-token -- Admin "<tu-JWT_SIGNING_KEY>"
dotnet run --project tools/generate-demo-token -- User  "<tu-JWT_SIGNING_KEY>"
```

El Frontend trae un token `Admin` hardcodeado en [`src/Frontend/src/config.ts`](src/Frontend/src/config.ts) firmado con el secreto placeholder de `.env.example`. **Si cambiaste `JWT_SIGNING_KEY`, regenerá ese token** con el comando de arriba y reemplazá la constante `ADMIN_DEMO_TOKEN` en ese archivo.

## 6. URLs de acceso

| Servicio | URL |
|---|---|
| Frontend | http://localhost:5173 |
| Swagger de EventService | http://localhost:8080/swagger |
| API de EventService | http://localhost:8080 |
| API de NotificationService (solo health checks) | http://localhost:8081 |
| RabbitMQ Management UI | http://localhost:15672 (usuario/clave por defecto: `guest`/`guest`, o los que hayas puesto en `.env`) |

## 7. Probar el flujo completo

```bash
ADMIN_TOKEN="<pegar el token Admin generado en el paso 5>"

curl -X POST http://localhost:8080/events \
  -H "Authorization: Bearer $ADMIN_TOKEN" -H "Content-Type: application/json" \
  -d '{"name":"Concierto","date":"2026-12-01T20:00:00Z","location":"Estadio Nacional","zones":[{"name":"General","price":50,"capacity":500}]}'

curl http://localhost:8080/events -H "Authorization: Bearer $ADMIN_TOKEN"
```

Revisá `docker compose logs api-notifications` para ver el correo simulado (nunca se envía por SMTP real) y la fila insertada en `AuditLog`. Ver [`specs/001-core-mvp-events-platform/quickstart.md`](specs/001-core-mvp-events-platform/quickstart.md) para el recorrido completo (idempotencia, resiliencia/DLQ, matriz de roles).

### Collection de Postman

En [`postman/`](postman/) hay una collection lista para probar la API sin pasar por el Frontend: camino feliz (crear/listar/detalle), validaciones (400) y la matriz de autorización (401/403). Importá `PlataformaEventos.postman_collection.json` + el environment `PlataformaEventos.postman_environment.json`, completá `adminToken`/`userToken` (paso 5) y corré la colección — o con la CLI:

```bash
npx newman run postman/PlataformaEventos.postman_collection.json -e postman/PlataformaEventos.postman_environment.json
```

## 8. Correr los tests

```bash
# Backend — necesita Docker corriendo (los tests de integración usan Testcontainers)
dotnet test PlataformaEventos.slnx

# Frontend
cd src/Frontend
npm install
npm run test
```

## 9. Troubleshooting

- **Un contenedor no llega a `healthy`**: `docker compose logs <servicio>` para ver el motivo exacto. Los health checks tienen `retries` generosos, pero si Postgres/RabbitMQ tardan mucho en un equipo lento, subí `retries`/`start_period` en `docker-compose.yml`.
- **Puerto ya en uso**: cambiá el puerto correspondiente (`EVENT_API_PORT`, `NOTIFICATION_API_PORT`, `FRONTEND_PORT`, `EVENTDB_PORT`, `NOTIFICATIONDB_PORT`, `RABBITMQ_PORT`, `RABBITMQ_MANAGEMENT_PORT`, `REDIS_PORT`) en tu `.env`.
- **`api-event` no arranca / error de `Jwt:SigningKey`**: `JWT_SIGNING_KEY` en `.env` está vacío o tiene menos de 32 bytes — es un fallo intencional (spec FR-022), no un bug.
- **El Frontend no puede llamar a la API**: confirmá que `VITE_API_EVENT_URL` (en `.env` o `src/Frontend/.env`) apunta al puerto real de `api-event`, y que el token en `config.ts` sigue siendo válido para el `JWT_SIGNING_KEY` actual (ver paso 5).
- **Volumen de datos de una corrida anterior con permisos/errores raros**: `docker compose down -v` borra los volúmenes y arranca todo desde cero.

## 10. Estructura del repo

```
docs/                    # Diseño teórico a 6 meses (fuera del alcance de este MVP)
specs/                   # Spec-Kit: spec, plan, contratos, tareas de esta feature
src/EventService/        # API de creación/consulta de eventos (.NET 10, Clean Architecture)
src/NotificationService/ # Worker que consume EventCreated (.NET 10, Clean Architecture)
src/Frontend/             # React + Vite + TypeScript + Tailwind
tests/                    # Un proyecto de test por capa/servicio (xUnit / Vitest)
tools/generate-demo-token/# Generador de JWT de demo (Fase 1, sin IdP real)
docker-compose.yml
```
