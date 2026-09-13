# Contract: EventService HTTP API

Base URL (dev): `http://localhost:<puerto>` (configurable vía `.env`). Todas las rutas protegidas requieren `Authorization: Bearer <jwt>`.

## `POST /events`

- **Auth**: Requiere rol `Admin` (policy `RequireAdminRole`).
- **Request body**:
  ```json
  {
    "name": "string (requerido, max 200)",
    "date": "ISO-8601 datetime UTC (requerido)",
    "location": "string (requerido, max 300)",
    "zones": [
      { "name": "string (requerido)", "price": "decimal >= 0", "capacity": "int > 0" }
    ]
  }
  ```
  `zones` debe tener al menos 1 elemento.
- **Responses**:
  | Código | Cuándo | Body |
  |---|---|---|
  | `201 Created` | Evento creado; header `Location: /events/{id}` | `EventDto` (ver abajo) |
  | `400 Bad Request` | Falla FluentValidation (campo requerido, longitud, zona inválida, cero zonas) | `ProblemDetails` con errores por campo |
  | `401 Unauthorized` | Sin token o token inválido/expirado | `ProblemDetails` genérico |
  | `403 Forbidden` | Token válido pero rol distinto de `Admin` | `ProblemDetails` genérico |
  | `429 Too Many Requests` | Excede 20 req/min por IP | — |
- **Efecto colateral**: persiste `Event` + `Zones` + `OutboxMessage` en una sola transacción; invalida la clave de caché `events:list`.
- **Idempotencia ante reintentos del cliente**: este endpoint **no** implementa una idempotency-key; un timeout seguido de un reintento del propio cliente puede crear dos eventos duplicados. Se documenta como limitación aceptada de este MVP (ver Assumptions del spec) — no está en alcance introducir un mecanismo de deduplicación a nivel HTTP en esta fase.

## `GET /events`

- **Auth**: Requiere autenticación (`Admin` o `User`, policy `RequireAuthenticatedUser`).
- **Response**: `200 OK` con `EventDto[]`. Servido desde caché (`events:list`, TTL 60s) cuando está vigente.
- **Errores**: `401` (sin token), `429` (excede 100 req/min por IP).
- Sin paginación ni filtros en este MVP (ver Assumptions del spec). **Decisión explícita de forma**: la respuesta es un array plano `EventDto[]`, no un envoltorio `{ items, page, ... }` — se documenta a propósito como decisión de no sobre-diseñar un contrato de paginación que el reto no pide (Constitución §7.5); si una fase futura agrega paginación, se tratará como un cambio de contrato versionado (ver CHK023 de `checklists/api.md`), no como una extensión retrocompatible implícita.

## `GET /events/{id}`

- **Auth**: Requiere autenticación (`Admin` o `User`).
- **Responses**:
  | Código | Cuándo |
  |---|---|
  | `200 OK` | Evento encontrado (`EventDto`); servido desde caché `events:{id}` (TTL 60s) cuando está vigente |
  | `404 Not Found` | No existe un evento con ese `id` |
  | `401` / `429` | Igual que `GET /events` |

## `EventDto` (forma de respuesta común a los tres endpoints anteriores)

```json
{
  "id": "uuid",
  "name": "string",
  "date": "ISO-8601 datetime UTC",
  "location": "string",
  "status": "Draft | Published | Cancelled",
  "zones": [
    { "id": "uuid", "name": "string", "price": "decimal", "capacity": "int" }
  ]
}
```

## `GET /health/live`

- **Auth**: Público.
- **Response**: `200 OK` si el proceso responde. Sin verificación de dependencias.

## `GET /health/ready`

- **Auth**: Público.
- **Response**: `200 OK` si Postgres (`eventdb`), Redis y RabbitMQ son alcanzables; `503 Service Unavailable` si alguna falla. Usado como `healthcheck` de Docker Compose.

## Fallas de autenticación (todas las rutas protegidas)

Sin token, token mal formado, con firma inválida o expirado producen **la misma respuesta `401`** (mismo `ProblemDetails` genérico), sin distinguir el motivo exacto — evita que el endpoint sirva de oráculo para tantear tokens casi válidos (ver `research.md` §4c). Un token válido pero de rol insuficiente sigue devolviendo `403`, que sí es distinguible de `401` (autenticado, pero no autorizado). El algoritmo del JWT se valida estrictamente contra HMAC-SHA256 (`research.md` §4b) — nunca se acepta `alg: none` ni otro algoritmo declarado por el propio token.

## Errores no controlados (cualquier endpoint)

Cualquier excepción no anticipada se traduce a `500 Internal Server Error` con un `ProblemDetails` genérico (`"Ocurrió un error interno"`), sin stack trace, mensaje de excepción real, ni detalles de conexión a datos. El detalle completo solo se registra en los logs del servidor (Serilog), nunca en la respuesta HTTP ni en el log en texto plano de headers/tokens.

## Aclaraciones adicionales (cierre de checklist)

- **Formato de errores de validación (`400`)**: se usa el `ValidationProblemDetails` estándar de ASP.NET Core (`{ "errors": { "campoX": ["mensaje 1", "mensaje 2"] }, ... }`), devolviendo **todos** los errores de validación encontrados en una sola respuesta, no solo el primero.
- **`Content-Type`**: `application/json` tanto en request como en cualquier response con body (incluyendo errores). Un body no-JSON o JSON malformado en `POST /events` se trata como error de validación (`400`), no como `500`.
- **Header `Location`**: se devuelve como ruta relativa (`/events/{id}`), consistente con `EventDto` — no se requiere URL absoluta en este MVP de un solo host.
- **`correlationId` en la respuesta**: se hace eco del valor usado (recibido o generado) en el header de respuesta `X-Correlation-Id`, para que el cliente pueda correlacionar su log con el del servidor sin tener que adivinar si el suyo fue el usado.
- **Zonas**: no hay límite máximo explícito de zonas por evento en este MVP (se documenta como decisión, no como omisión — el volumen de un evento de demo es trivial); tampoco hay restricción de nombres de zona duplicados dentro del mismo evento — dos zonas con el mismo `name` son válidas si cumplen sus propias reglas de precio/capacidad.
- **`/health/live` y `/health/ready`**: sin body — solo código de estado; es información suficiente para su único consumidor (Docker Compose `healthcheck`).
- **Versionado**: no se introduce un esquema de versionado de URL/header en este MVP (no aplica — Constitución/Spec no lo piden); cualquier cambio incompatible de contrato en una fase futura se trata como una decisión de diseño explícita, no como una extensión implícita del actual.

## CORS

Origen permitido: `http://localhost:5173` (Frontend en desarrollo). Métodos: `GET, POST`. Headers permitidos: incluye `Authorization`. Nunca `AllowAnyOrigin`.
