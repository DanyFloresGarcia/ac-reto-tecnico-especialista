# Quickstart: Validación end-to-end de la Observabilidad

Prerrequisitos: Docker Desktop (o Podman), el entorno del Spec 01 ya funcionando (`docker compose up --build` con `EventService`/`NotificationService` sanos). Ver [`contracts/observability-conventions.md`](./contracts/observability-conventions.md) para las convenciones de nombres/atributos y [`contracts/alerting-rules.md`](./contracts/alerting-rules.md) para las reglas exactas de alerta. Ver [`data-model.md`](./data-model.md) para el detalle de cada entidad.

## 1. Levantar el entorno (con el stack de observabilidad)

```bash
cp .env.example .env      # copy .env.example .env  en Windows — incluye ahora GRAFANA_ADMIN_USER/PASSWORD
docker compose up --build
```

Verificar `docker compose ps`: además de los contenedores del Spec 01, deben llegar a `healthy`/`running`: `tempo`, `loki`, `promtail`, `prometheus`, `grafana`, `postgres-exporter-event`, `postgres-exporter-notification`, `redis-exporter`.

Abrir Grafana en `http://localhost:3000` (usuario/password de `.env`). Los datasources (Tempo, Loki, Prometheus) y los 3 dashboards ya están provisionados — no requiere configuración manual.

## 2. Historia 1 — Diagnosticar una notificación (camino feliz)

Reutilizando el flujo del Spec 01 ([`specs/001-core-mvp-events-platform/quickstart.md`](../001-core-mvp-events-platform/quickstart.md) pasos 1-3), crear un evento:

```bash
curl -i -X POST http://localhost:8080/events \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -H "X-Correlation-Id: $(uuidgen)" \
  -d '{ "name": "Concierto Rock en el Parque", "date": "2026-12-01T20:00:00Z", "location": "Estadio Nacional, Lima",
        "zones": [ { "name": "General", "price": 50.00, "capacity": 500 } ] }'
```

**Esperado**: `201 Created`. Copiar el header de respuesta `traceresponse` (o el `X-Correlation-Id` enviado) — cualquiera de los dos sirve para la búsqueda (FR-017).

En Grafana → **Explore** → datasource **Tempo** → buscar por `traceId` (o por `correlationId` usando el filtro de atributos, si se propagó como tag). **Esperado**: una única traza con 4-5 spans (`POST /events` → comando SQL Outbox → `EventCreated publish` → `EventCreated receive/consume` → comando SQL `AuditLog`), sin ningún span en estado `Error` (Acceptance Scenario 1 de US1).

Click en cualquier span → botón **"Logs for this span"** (habilitado por `tracesToLogsV2`). **Esperado**: salta directo a Grafana Loki filtrado por ese `TraceId`, mostrando logs de ambos servicios sin cambiar de pestaña ni copiar el ID manualmente (Acceptance Scenario 3 de US1, SC-001).

## 3. Historia 1 — Camino de falla

Detener temporalmente `postgres-notification` (`docker compose stop postgres-notification`) y volver a crear un evento. **Esperado**: la traza muestra el span `EventCreated receive` con estado `Error` (o la traza "corta" antes de completar el consumo), permitiendo identificar visualmente en qué paso se rompió el flujo sin revisar logs contenedor por contenedor (Acceptance Scenario 2 de US1, SC-002). Reiniciar el contenedor (`docker compose start postgres-notification`) al terminar.

## 4. Historia 2 — Dashboard de salud y service graph

Abrir el dashboard **"health-overview"** en Grafana. **Esperado**: tasa de error y latencia p50/p95/p99 de `EventService` y `NotificationService` por separado, más el panel de métricas de infraestructura (RabbitMQ/Postgres/Redis, FR-019) — todo en una sola pantalla (Acceptance Scenario 1, SC-003).

Abrir el dashboard **"service-graph"**. **Esperado**: nodos `EventService` → `RabbitMQ` → `NotificationService` con tasa de solicitudes/errores por conexión (Acceptance Scenario 2 de US2).

## 5. Historia 3 — Alertas SLO

Generar tráfico de error sostenido por al menos 1 minuto, por ejemplo deteniendo `postgres-event` y enviando requests repetidos a `POST /events` durante >60s:

```bash
for i in $(seq 1 30); do curl -s -o /dev/null http://localhost:8080/events -X POST -H "Authorization: Bearer $ADMIN_TOKEN" -H "Content-Type: application/json" -d '{}'; sleep 2; done
```

En Grafana → **Alerting** → **Alert rules**, la regla de tasa de error de `EventService` debe pasar a estado **Firing** después de ~1 minuto sostenido (ver umbral exacto en `contracts/alerting-rules.md`). Restaurar `postgres-event` y esperar otro minuto: la alerta debe volver a **Normal** automáticamente, quedando el ciclo completo visible en **State history** (Acceptance Scenarios 1 y 2 de US3, SC-004).

## 6. Resiliencia (FR-016, edge case) — el stack de observabilidad no debe bloquear el negocio

```bash
docker compose stop tempo loki prometheus grafana
curl -i -X POST http://localhost:8080/events -H "Authorization: Bearer $ADMIN_TOKEN" -H "Content-Type: application/json" -H "X-Correlation-Id: $(uuidgen)" -d '{ "name": "Evento sin observabilidad", "date": "2026-12-01T20:00:00Z", "location": "Lima", "zones": [ { "name": "General", "price": 10, "capacity": 10 } ] }'
```

**Esperado**: sigue respondiendo `201 Created` con latencia normal (SC-006) y la notificación se procesa igual — la ausencia del stack de observabilidad no afecta el flujo de negocio. Restaurar con `docker compose start tempo loki prometheus grafana`.

## 7. Verificación del presupuesto de overhead (+50ms p95, SC-006)

Con el stack completo arriba, revisar el panel de latencia p95 de `POST /events` en el dashboard `health-overview` bajo tráfico de prueba (repetir el bucle del paso 5 sin detener ninguna dependencia). Comparar contra la latencia base documentada en el quickstart del Spec 01 (sin instrumentación) — la diferencia no debe superar 50ms en p95, según lo acordado en `/speckit-clarify`.
