# Contract: Reglas de Alerta SLO (Grafana Unified Alerting)

Provisionadas como código en `observability/grafana/provisioning/alerting/slo-alerts.yaml`, consultando el datasource de Prometheus. 4 reglas totales (2 métricas × 2 servicios), todas con `for: 1m` (`/speckit-clarify`, Session 2026-09-13) y evaluación cada 30s.

## Tasa de error > 5% sostenida 1 minuto

**Por servicio** (`EventService`, `NotificationService`):

```promql
100 * (
  sum(rate(http_server_request_duration_seconds_count{service_name="<Servicio>", http_response_status_code=~"5.."}[1m]))
  /
  sum(rate(http_server_request_duration_seconds_count{service_name="<Servicio>"}[1m]))
) > 5
```

- **for**: `1m`
- **Etiqueta de severidad**: `warning`
- **Resumen**: `Tasa de error de <Servicio> por encima del 5% durante al menos 1 minuto`
- **Nota de robustez**: si el servicio no recibe tráfico en la ventana (`sum(...) == 0` en el denominador), la expresión da `NaN`/sin serie — Prometheus no dispara la alerta en ese caso (evita falsos positivos por ausencia de tráfico, alineado con el edge case "no hay degradación → no se generan alertas").

## Latencia p95 > 1s sostenida 1 minuto

**Por servicio** (`EventService`, `NotificationService`):

```promql
histogram_quantile(
  0.95,
  sum by (le) (rate(http_server_request_duration_seconds_bucket{service_name="<Servicio>"}[1m]))
) > 1
```

- **for**: `1m`
- **Etiqueta de severidad**: `warning`
- **Resumen**: `Latencia p95 de <Servicio> por encima de 1s durante al menos 1 minuto`

## Resolución y estado (FR-013)

- Grafana Unified Alerting transiciona automáticamente `Firing → Normal` cuando la expresión vuelve a evaluar `false` durante el período de evaluación configurado — no requiere lógica adicional.
- El historial de transiciones (`Normal → Pending → Firing → Normal`, con timestamp de cada cambio) queda visible nativamente en la vista "State history" de cada regla en Grafana — es el mecanismo que satisface "queda visible para el equipo" y "queda reflejado el historial de cuándo estuvo activa" (Historia 3, escenarios 1 y 2) sin necesidad de un canal de notificación externo (email/Slack/webhook), tal como fija la Assumption del spec.

## Fuera de alcance (documentado, no un olvido)

- **Notification channels** (email/Slack/webhook): explícitamente fuera de alcance para esta primera versión (Assumption del spec) — las 4 reglas no tienen ningún *contact point* configurado más allá del historial visible en la UI de Grafana.
- **Umbrales configurables por variable de entorno**: los valores (`5%`, `1s`, `1m`) quedan fijos en el YAML de provisioning para esta fase; ajustarlos requiere editar `slo-alerts.yaml` y reiniciar Grafana — no se expone como configuración de `docker-compose`/`.env`, ya que el spec no pide que sean ajustables en runtime.
