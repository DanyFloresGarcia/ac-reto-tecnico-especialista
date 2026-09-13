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
docker compose up --build -d
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

Este MVP no tiene login real (Fase 1 — ver Constitución §5.1): no hay usuario/contraseña ni IdP. En su lugar, un JWT se firma "offline" (con una herramienta de línea de comandos) usando la misma clave secreta que valida `EventService`, y ese JWT se manda como header `Authorization: Bearer <token>` en cada request.

**`<tu-JWT_SIGNING_KEY>` no es un JWT ni algo que se busca en internet**: es el valor de texto que vos mismo pusiste en la variable `JWT_SIGNING_KEY` de tu `.env` (paso 2). El comando toma esa clave y con ella genera un JWT nuevo, firmado, listo para usar.

Ejecutá esto en una terminal normal de tu máquina (no dentro de Docker — necesitás el .NET SDK local), parado en la raíz del repo:

```bash
dotnet run --project tools/generate-demo-token -- Admin "<tu-JWT_SIGNING_KEY>"
dotnet run --project tools/generate-demo-token -- User  "<tu-JWT_SIGNING_KEY>"
```

Ejemplo real (con una clave cualquiera de 32+ bytes):

```bash
dotnet run --project tools/generate-demo-token -- Admin "qnm268A8KXb/dzmpfCoYV5v5QcquACo5TlFp7ZSHbDk="
```

Cada comando imprime **una sola línea** en la consola (algo como `eyJhbGciOiJIUzI1NiIs...`) — ese texto completo es el JWT. Copialo tal cual; lo vas a necesitar en el paso 7 (`ADMIN_TOKEN=`) y/o en Postman. El token expira a las 24 horas, así que si pasa ese tiempo hay que volver a generarlo.

### ¿Cuándo hace falta tocar el Frontend?

El Frontend toma su token `Admin` de la variable de entorno **`VITE_ADMIN_TOKEN`** (en tu `.env` de la raíz), no de código. `.env.example` ya trae un valor por defecto firmado con el `JWT_SIGNING_KEY` placeholder.

- **Si dejaste `JWT_SIGNING_KEY` igual al placeholder de `.env.example`**: no tenés que hacer nada, el `VITE_ADMIN_TOKEN` de ejemplo ya es válido.
- **Si cambiaste `JWT_SIGNING_KEY`** por un valor propio (recomendado): el `VITE_ADMIN_TOKEN` que trae `.env.example` queda inválido (la API lo va a rechazar con 401) y tenés que:
  1. Generar un token `Admin` nuevo con el comando de arriba, usando tu `JWT_SIGNING_KEY` real.
  2. Pegar el resultado en `VITE_ADMIN_TOKEN` dentro de tu `.env`.
  3. Recrear el contenedor del Frontend para que tome el env var nuevo — no hace falta `--build` (el código no cambió): `docker compose up -d frontend`.

El token `User` **no lo usa el Frontend** — no tiene ningún selector de rol, solo actúa como Admin. Generalo aparte, solo para probar por Postman/curl los casos 403 (ver [quickstart.md](specs/001-core-mvp-events-platform/quickstart.md)).

El token `User` no está hardcodeado en ningún lado — generalo solo si vas a probar manualmente los casos de autorización 403 (por Postman/curl).

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

Revisá `docker compose logs api-notifications` para ver el correo (simulado por defecto; real si configuraste `SMTP_*` en `.env` — ver [`specs/001-core-mvp-events-platform/research.md`](specs/001-core-mvp-events-platform/research.md) §8b) y la fila insertada en `AuditLog`. Ver [`specs/001-core-mvp-events-platform/quickstart.md`](specs/001-core-mvp-events-platform/quickstart.md) para el recorrido completo (idempotencia, resiliencia/DLQ, matriz de roles), o [`specs/001-core-mvp-events-platform/manual-validation-checklist.md`](specs/001-core-mvp-events-platform/manual-validation-checklist.md) para una checklist de validación organizada por atributo de calidad (desacoplamiento, mensajería, consistencia, resiliencia, reintentos/idempotencia, caché, IdP, observabilidad).

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
- **El Frontend no puede llamar a la API**: confirmá que `VITE_API_EVENT_URL` (en `.env` o `src/Frontend/.env`) apunta al puerto real de `api-event`, y que `VITE_ADMIN_TOKEN` sigue siendo válido para el `JWT_SIGNING_KEY` actual (ver paso 5).
- **Volumen de datos de una corrida anterior con permisos/errores raros**: `docker compose down -v` borra los volúmenes y arranca todo desde cero.
- **Quiero ver/limpiar los datos directamente en Postgres sin borrar los volúmenes**: ver [`specs/001-core-mvp-events-platform/quickstart.md`](specs/001-core-mvp-events-platform/quickstart.md#10-consultas-sql-útiles-inspección-y-reset-de-datos) — consultas SQL listas para inspeccionar `Events`/`Zones` o resetearlas con `TRUNCATE`.

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
