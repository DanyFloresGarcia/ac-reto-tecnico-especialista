# Phase 0 Research: Observabilidad End-to-End (Trazas, Logs y Métricas)

El spec (tras `/speckit-clarify`) no dejó ningún marcador `NEEDS CLARIFICATION` de negocio sin resolver. La investigación de esta fase se centra en **cómo** instrumentar concretamente OpenTelemetry sobre el stack .NET 10 + MassTransit + Serilog ya existente, y en las decisiones de integración entre Tempo/Loki/Prometheus/Grafana que el spec deja abiertas a nivel de implementación.

## 1. SDK de OpenTelemetry para .NET (traces + métricas)

- **Decision**: Un único extension method `AddObservability(this WebApplicationBuilder builder, string serviceName)` compartido (copiado, no referenciado como proyecto — mismo criterio que `EventContracts`, Constitución §7.3) en `EventService.Api/Observability/` y `NotificationService.Worker/Observability/`, que registra:
  - `TracerProvider`: `AddSource("MassTransit")` + `AddAspNetCoreInstrumentation()` + `AddHttpClientInstrumentation()` + `AddEntityFrameworkCoreInstrumentation()` + (solo `EventService`) `AddRedisInstrumentation()`, con `SetResourceBuilder(service.name = serviceName, service.namespace = "plataforma-eventos")`, sampler `AlwaysOnSampler` (ver §11), exportado vía `AddOtlpExporter(o => o.Endpoint = new Uri(configuration["Otel:ExporterEndpoint"] ?? "http://tempo:4317"))` (gRPC). El endpoint se lee de la variable de entorno `Otel__ExporterEndpoint` (mismo patrón de configuración que `RabbitMq__Host`/`Redis__ConnectionString` en `Program.cs`), con `http://tempo:4317` como default para poder correr `dotnet run` fuera de `docker compose` sin fallar al arrancar.
  - `MeterProvider`: `AddAspNetCoreInstrumentation()` + `AddHttpClientInstrumentation()` + `AddRuntimeInstrumentation()`, exportado vía `AddPrometheusExporter()`.
  - `app.MapPrometheusScrapingEndpoint("/metrics")` (sin autenticación — el endpoint solo es alcanzable dentro de la red interna de `docker-compose`, nunca publicado a internet, igual que el resto del stack de observabilidad).
- **Rationale**: Un único helper evita duplicar la configuración en dos `Program.cs` y hace explícito que ambos servicios se instrumentan de forma idéntica (mismo criterio de consistencia que la convención de capas de §7.1). Usar los paquetes `OpenTelemetry.Instrumentation.*` oficiales/contrib en vez de código manual de `ActivitySource` para HTTP/EF Core evita instrumentar a mano algo que ya resuelven, y no requiere tocar ningún controller/handler (FR-015).
- **Alternatives considered**: Un `OpenTelemetry Collector` intermedio (patrón *Collector sidecar/gateway*) recibiendo OTLP y reenviando a Tempo/Prometheus — descartado para esta fase: Tempo acepta OTLP nativamente y el exporter de Prometheus es *pull*-based (no necesita un Collector en el medio), así que un Collector solo agregaría un contenedor más y un punto de falla adicional sin resolver ningún requisito de esta spec.

## 2. Propagación de traza a través de RabbitMQ (MassTransit)

- **Decision**: No se escribe código de propagación manual. MassTransit 8.x publica automáticamente el contexto de traza W3C (`traceparent`) como header del mensaje de RabbitMQ cuando el `ActivitySource("MassTransit")` está registrado en el `TracerProvider` (ver §1) — tanto en el productor (`EventService.Api`, al publicar vía Outbox) como en el consumidor (`NotificationService.Worker`, al recibir en `EventCreatedConsumer`), sin relación con el campo `correlationId` propio del contrato `EventCreated` (que sigue viajando igual, ver FR-017).
- **Rationale**: Es soporte nativo de la librería ya usada (Constitución §3) — no requiere ningún paquete adicional ni cambiar la forma del mensaje `EventCreated`. El span de "publicar en el broker" (productor) y el de "consumir mensaje" (consumidor) quedan enlazados al mismo `traceId` automáticamente, cumpliendo FR-001/FR-002 sin tocar `EventService.Application`/`NotificationService.Application`.
- **Alternatives considered**: Propagar el contexto de traza manualmente como un campo adicional del contrato `EventCreated` — descartado por duplicar algo que MassTransit ya resuelve a nivel de header de mensaje, y por requerir tocar el schema del contrato de integración (que el spec fija como estable).

## 3. Instrumentación de EF Core / Npgsql

- **Decision**: `OpenTelemetry.Instrumentation.EntityFrameworkCore` (paquete contrib), que genera un span por comando SQL ejecutado por `EventDbContext`/`NotificationDbContext`, incluyendo el paso de persistencia en el Outbox (FR-001, tramo "persistencia").
- **Rationale**: Es agnóstico al proveedor (funciona igual si en el futuro cambia de Npgsql a otro motor) y no requiere reconfigurar `NpgsqlDataSource` manualmente para habilitar su `ActivitySource` nativo.
- **Alternatives considered**: Habilitar el `ActivitySource("Npgsql")` nativo de Npgsql 6+ vía `NpgsqlDataSourceBuilder.EnableOpenTelemetry()` — descartado porque requeriría reemplazar el registro actual de `AddDbContext(... UseNpgsql(...))` por un `NpgsqlDataSource` explícito en ambos servicios, un cambio más invasivo que agregar el paquete contrib para el mismo resultado observable.

## 4. Instrumentación de Redis (solo `EventService`)

- **Decision**: `OpenTelemetry.Instrumentation.StackExchangeRedis`, registrado sobre el mismo `IConnectionMultiplexer` que ya usa `RedisEventCacheService`.
- **Rationale**: Cubre las operaciones de Cache-Aside (`events:list`, `events:{id}`) como spans hijos del span HTTP, sin modificar `RedisEventCacheService`.
- **Alternatives considered**: No instrumentar Redis — descartado porque el diagnóstico de la Historia 1 se beneficia de ver si una lectura lenta viene de Postgres o de Redis; el costo de agregar el paquete es marginal.

## 5. Correlación logs-trazas (Serilog → Loki, enlazado desde Tempo)

- **Decision**: Agregar `Serilog.Enrichers.Span` al pipeline de Serilog de ambos servicios (`.Enrich.WithSpan()`), que añade `TraceId`/`SpanId` como propiedades del `Activity.Current` a cada línea de log — sin cambiar el sink (`WriteTo.Console(new RenderedCompactJsonFormatter())` se mantiene igual). Loki recibe esos logs vía **Promtail**, no vía un sink de Serilog directo. La correlación en Grafana se configura en el datasource de Tempo (`tracesToLogsV2`, apuntando a Loki, mapeando `service.name` → label `service` y usando `TraceId` como filtro `|=` sobre el campo ya presente en el JSON).
- **Rationale**: Cumple FR-008 (saltar de traza a logs sin copiar IDs) sin requerir que el código de la aplicación conozca la existencia de Loki — el enriquecedor solo agrega dos campos al log que ya existía desde la Fase 1 (§6.1). Usar Promtail (scraping de stdout del contenedor) en vez de un sink Serilog→Loki directo es lo que garantiza FR-016: si Loki está caído, Promtail simplemente deja de poder enviar (con su propio buffer/reintento), pero el proceso de `EventService`/`NotificationService` nunca se entera ni bloquea, porque nunca abre una conexión de red hacia Loki.
- **Alternatives considered**: `Serilog.Sinks.Grafana.Loki` (sink directo) — descartado precisamente por el riesgo de acoplar el flujo de negocio a la disponibilidad de Loki (un sink de red que falla puede bloquear o descartar logs de forma menos predecible que un recolector externo desacoplado del proceso).

## 6. Ingesta de logs (Promtail vs. Docker logging driver)

- **Decision**: Un contenedor **Promtail** dedicado (imagen `grafana/promtail`), con acceso de solo lectura al socket de Docker y a `/var/lib/docker/containers`, que descubre `api-event`/`api-notifications` por label de `docker-compose` y parsea cada línea como JSON (ya lo es, gracias a `RenderedCompactJsonFormatter`), promoviendo `service`, `level` y `TraceId` a labels/campos estructurados de Loki. El mismo Promtail también descubre el contenedor `rabbitmq` y envía su stdout a Loki **sin** parseo JSON (RabbitMQ no emite logs estructurados) — solo texto plano con el label `service=rabbitmq`, cumpliendo la Assumption del spec ("logs de la infraestructura de mensajería... de forma best-effort") y FR-005.
- **Rationale**: Es el patrón estándar de la comunidad Grafana para centralizar logs de contenedores sin tocar la app ni requerir el driver de logging nativo de Loki en el propio Docker daemon (que exigiría reconfigurar el daemon del desarrollador, fuera del alcance de "solo tocar este repo"). Incluir `rabbitmq` en el mismo Promtail (en vez de omitirlo) evita dejar sin cumplir un compromiso que el propio spec ya asume como parte del alcance.
- **Alternatives considered**: Docker Loki logging driver (`--log-driver=loki`) — descartado porque requiere instalar un plugin en el Docker daemon del desarrollador (fuera del propio `docker-compose.yml`, viola la portabilidad "clona y corre" del proyecto).

## 7. Backend de métricas y scraping de infraestructura (FR-019)

- **Decision**: Prometheus con `scrape_configs` apuntando a: `api-event:8080/metrics`, `api-notifications:8080/metrics`, `postgres-exporter-event:9187`, `postgres-exporter-notification:9187`, `redis-exporter:9121`, y `rabbitmq:15692` (plugin nativo `rabbitmq_prometheus`, habilitado vía `RABBITMQ_PLUGINS=rabbitmq_prometheus` en la imagen `rabbitmq:3-management`, sin exporter externo).
- **Rationale**: `postgres_exporter` (`prometheuscommunity/postgres-exporter`) y `redis_exporter` (`oliver006/redis_exporter`) son los exporters de facto de la comunidad Prometheus, ambos OSS (Constitución §2). RabbitMQ ya trae su propio plugin de métricas listo para producción — agregar un exporter externo sería redundante.
- **Alternatives considered**: Un único `postgres_exporter` con dos `DATA_SOURCE_NAME` (multi-target) — descartado por complejidad de configuración innecesaria a esta escala; un exporter por base de datos es más simple de razonar y de apagar/depurar de forma independiente.

## 8. Service Graph (FR-011)

- **Decision**: Habilitar el `metrics-generator` de Tempo (`overrides.defaults.metrics_generator.processors: [service-graphs, span-metrics]`), con `metrics_generator.storage.remote_write` apuntando a Prometheus (`prometheus --enable-feature=remote-write-receiver`). El dashboard de service graph en Grafana consume esas métricas generadas (`traces_service_graph_request_total`, etc.) desde el datasource de Prometheus.
- **Rationale**: Es la forma soportada de generar un service graph con Tempo sin desplegar un componente adicional (`grafana-agent` u otro) — Tempo ya recibe todos los spans (incluyendo el salto por RabbitMQ vía MassTransit, §2) y puede derivar el grafo de dependencias directamente de esos spans padre-hijo entre `EventService` y `NotificationService`.
- **Alternatives considered**: Construir el grafo manualmente en un dashboard con `count by (service.name, peer.service)` sobre spans — descartado porque el `metrics-generator` de Tempo ya resuelve esto de forma estándar y mantenible.

## 9. Alertas SLO (FR-012/FR-013, umbral de 1 minuto de `/speckit-clarify`)

- **Decision**: Reglas de **Grafana Unified Alerting** (no Prometheus Alertmanager) provisionadas como código YAML (`observability/grafana/provisioning/alerting/slo-alerts.yaml`), consultando el datasource de Prometheus. Ver el detalle exacto de expresiones/umbrales en [`contracts/alerting-rules.md`](./contracts/alerting-rules.md).
- **Rationale**: El spec (Assumptions) acepta que las alertas queden "visibles en el historial de la propia herramienta de visualización" sin canal externo — Grafana Alerting ya incluye vista de estado/historial nativa, evitando desplegar un Alertmanager separado solo para terminar mostrando el mismo estado en Grafana.
- **Alternatives considered**: Prometheus Alertmanager + integración con Grafana — descartado por agregar un contenedor y una capa de enrutamiento de notificaciones (email/Slack/webhook) que el spec explícitamente marca fuera de alcance para esta primera versión.

## 10. Exclusión de datos sensibles en spans/logs (FR-018)

- **Decision**: No se agrega ningún atributo custom con email o contenido del mensaje a los spans de `NotificationService` — las instrumentaciones automáticas (ASP.NET Core, HTTP client, EF Core) no leen bodies de request/response por defecto, así que no hace falta un filtro adicional; se documenta explícitamente en `contracts/observability-conventions.md` la prohibición de agregar `email`/`body` como atributo en cualquier instrumentación manual futura. Los logs existentes de envío de correo en `NotificationService` (Spec 01) ya solo registran `eventId`/`notificationId`, no destinatario ni contenido — se verifica como parte de esta feature, sin requerir cambios de código.
- **Rationale**: La forma más segura de cumplir FR-018 es no introducir la superficie que expondría esos datos, en vez de agregar un componente de redacción/masking que podría fallar silenciosamente y filtrar el dato igual.
- **Alternatives considered**: Un `Processor` custom de OpenTelemetry que inspeccione y redacte atributos por nombre (`email`, `body`) antes de exportar — descartado como innecesario mientras ningún código agregue esos atributos en primer lugar; queda documentado como salvaguarda a considerar si una fase futura instrumenta algo que sí toque esos campos.

## 11. Estrategia de sampling

- **Decision**: `AlwaysOnSampler` (100% de las trazas) en ambos servicios.
- **Rationale**: El volumen esperado en el entorno de demo/pruebas manuales de este reto es bajo; samplear parcialmente solo complicaría el diagnóstico de la Historia 1 (podría perderse justo la traza que un desarrollador busca) sin ningún beneficio de costo/almacenamiento real a esta escala.
- **Alternatives considered**: `TraceIdRatioBasedSampler` (ej. 10%) — descartado por el riesgo de que la traza de un fallo puntual (el caso de uso central de la Historia 1) no quede capturada.

## 12. Validación del presupuesto de overhead (+50ms p95, `/speckit-clarify`)

- **Decision**: No se agrega un test de carga automatizado nuevo (fuera de alcance de este MVP). El `quickstart.md` documenta un procedimiento manual: comparar la latencia de `POST /events` (ya medible vía el propio `http.server.request.duration` exportado a Prometheus) antes/después de habilitar la instrumentación, usando el mismo mecanismo de medición que valida el resto del SC-006.
- **Rationale**: Construir un harness de carga dedicado excede el alcance de "observabilidad mínima" de este reto técnico; la métrica que la propia instrumentación expone es suficiente para verificar el presupuesto de forma manual/exploratoria.
- **Alternatives considered**: Un benchmark automatizado con `k6`/`NBomber` en CI — descartado por ser una inversión de infraestructura de testing que el spec no pide y que introduciría una herramienta nueva no contemplada en la Constitución.
