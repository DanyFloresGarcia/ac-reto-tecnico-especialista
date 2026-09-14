# Feature Specification: Observabilidad End-to-End (Trazas, Logs y Métricas)

**Feature Branch**: `002-add-observability-stack`

**Created**: 2026-09-13

**Status**: Draft

**Input**: User description: "Agregar observabilidad mínima end-to-end a EventService y NotificationService (OpenTelemetry, Grafana Tempo, Loki, Grafana con correlación trace-to-logs, dashboards de salud/logs/service graph/alertas SLO), corriendo en el docker-compose local existente sin romper el flujo actual de creación de eventos/notificaciones."

## Clarifications

### Session 2026-09-13

- Q: ¿Durante cuánto tiempo debe sostenerse una condición de error/latencia elevada antes de que la alerta pase a estado disparado? → A: 1 minuto.
- Q: ¿Qué presupuesto máximo de overhead de latencia puede añadir la instrumentación de observabilidad a las solicitudes? → A: +50ms en p95.
- Q: ¿Deben excluirse o enmascararse datos sensibles (ej. email, contenido del mensaje de notificación) al incluirlos como atributos de spans o campos de log? → A: Excluir/enmascarar email y contenido del mensaje; usar solo identificadores (eventId, notificationId).
- Q: ¿El dashboard de salud general y las métricas del sistema deben incluir también RabbitMQ, Postgres y Redis, o solo las métricas de aplicación de EventService y NotificationService? → A: Incluir también métricas básicas de infraestructura (RabbitMQ, Postgres, Redis) vía exporters de Prometheus.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Diagnosticar una notificación que no llegó (Priority: P1)

Un desarrollador recibe un reporte de que un evento fue creado pero su notificación nunca se disparó. Necesita buscar ese evento por su identificador en una única herramienta y ver en qué paso del flujo (creación → outbox → mensaje en el broker → consumo en NotificationService) se rompió, saltando directo de la traza a los logs correlacionados, sin entrar servicio por servicio a revisar logs sueltos.

**Why this priority**: Es el escenario de mayor valor de negocio: reduce directamente el tiempo de diagnóstico de fallas, que es el objetivo central de esta funcionalidad. Sin esto, la observabilidad agregada no resuelve el problema que la origina.

**Independent Test**: Puede probarse provocando (o tomando) un caso real donde la notificación de un evento no se generó, buscando el `eventId`/`traceId` correspondiente en la herramienta de visualización, y verificando que se puede ver la traza completa y saltar a los logs correlacionados de ambos servicios sin cambiar de herramienta.

**Acceptance Scenarios**:

1. **Given** un evento fue creado exitosamente y su notificación se procesó sin errores, **When** un desarrollador busca el `eventId`/`traceId` en la herramienta de visualización, **Then** ve una única traza end-to-end que cubre la creación, el paso por el outbox, la publicación en el broker de mensajería y el consumo en NotificationService.
2. **Given** un evento fue creado pero su notificación falló o nunca se generó, **When** un desarrollador busca ese `eventId`/`traceId`, **Then** la traza muestra visualmente en qué paso se detuvo o falló el flujo (ej. la traza termina en la publicación, o el span de consumo muestra error).
3. **Given** un desarrollador está viendo una traza o un span específico, **When** solicita ver los logs correlacionados, **Then** el sistema lo lleva directo a los logs de ese `traceId`/`correlationId` en ambos servicios, sin necesidad de copiar identificadores ni cambiar de herramienta.

---

### User Story 2 - Vista unificada de salud y dependencias (Priority: P2)

Un operador necesita ver, desde un único tablero, el estado general de EventService y NotificationService (tasa de error, latencia) y el mapa de dependencias entre ambos, para detectar una degradación sin tener que consultar trazas, logs y métricas por separado.

**Why this priority**: Es el uso diario/proactivo de la observabilidad (detectar antes de que alguien reporte), en contraste con la Historia 1 que es reactiva/diagnóstica. Depende de que exista la misma instrumentación de la Historia 1, por eso va en segundo lugar.

**Independent Test**: Puede probarse abriendo el dashboard de salud general con el sistema en marcha (con o sin tráfico de prueba) y verificando que muestra tasa de error y latencia p50/p95/p99 por servicio, además de un mapa de dependencias entre EventService y NotificationService, todo en una sola pantalla.

**Acceptance Scenarios**:

1. **Given** ambos servicios están corriendo y recibiendo tráfico, **When** un operador abre el dashboard de salud general, **Then** ve la tasa de error y la latencia p50/p95/p99 de cada servicio de forma individual.
2. **Given** el mismo dashboard, **When** el operador revisa el mapa de dependencias, **Then** ve representada la relación entre EventService y NotificationService (incluyendo el paso por el broker de mensajería) sin tener que consultarla en otra herramienta.
3. **Given** uno de los dos servicios empieza a fallar o a responder más lento de lo normal, **When** el operador consulta el dashboard, **Then** la degradación es visible en las métricas de ese servicio antes de que se reciba un reporte externo.

---

### User Story 3 - Alertas básicas de SLO (Priority: P3)

El equipo quiere enterarse de una degradación (tasa de error o latencia elevada) apenas ocurre, sin depender de estar mirando activamente un dashboard ni de que un usuario final la reporte primero.

**Why this priority**: Aporta valor incremental sobre las Historias 1 y 2 (que son de consulta activa) al convertir la observabilidad en algo proactivo/pasivo. No bloquea a las otras dos, por eso queda en tercer lugar.

**Independent Test**: Puede probarse configurando manualmente una condición que cruce el umbral definido (ej. generando errores o latencia artificial) y verificando que la alerta pasa a estado disparado y es visible para el equipo, sin necesidad de que las Historias 1 o 2 estén en uso al mismo tiempo.

**Acceptance Scenarios**:

1. **Given** se definieron umbrales de tasa de error y de latencia p95 por servicio, **When** alguno de los dos se cruza de forma sostenida, **Then** una alerta pasa a estado activo/disparado y queda visible para el equipo.
2. **Given** una alerta está disparada, **When** la condición vuelve a estar dentro del umbral normal, **Then** la alerta se resuelve automáticamente y queda reflejado el historial de cuándo estuvo activa.
3. **Given** no hay ninguna degradación, **When** el sistema opera dentro de los umbrales normales, **Then** no se generan alertas (ausencia de falsos positivos en operación normal).

---

### Edge Cases

- ¿Qué pasa si el backend de trazas, logs o métricas no está disponible momentáneamente? La creación de eventos y el envío de notificaciones deben seguir funcionando con normalidad (la observabilidad nunca debe bloquear ni hacer fallar el flujo de negocio).
- ¿Qué pasa si un mensaje se pierde o no incluye el contexto de traza al cruzar el broker de mensajería? Debe seguir siendo posible encontrar los logs de ese evento por `eventId`, aunque la traza quede incompleta.
- ¿Qué pasa si un desarrollador busca un `eventId`/`traceId` que no existe o ya no está disponible (fuera de la ventana de retención)? La herramienta debe indicarlo de forma clara, sin errores ambiguos.
- ¿Qué pasa si una alerta se dispara pero nadie la está mirando en ese momento? Debe quedar visible su historial (cuándo se disparó y cuándo se resolvió) para revisión posterior.
- ¿Qué pasa si dos servicios generan logs con el mismo `correlationId`/`traceId` pero en instantes muy distintos (ej. un reintento tardío)? La vista de logs correlacionados debe seguir agrupándolos como parte del mismo flujo.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: El sistema DEBE instrumentar EventService y NotificationService para generar trazas distribuidas que cubran el flujo completo de una solicitud "crear evento": recepción de la solicitud → persistencia (outbox) → publicación en el broker de mensajería → consumo en NotificationService, como una única traza end-to-end.
- **FR-002**: El sistema DEBE propagar el contexto de traza a través del broker de mensajería, de forma que el span de consumo en NotificationService quede vinculado al mismo trace que el de creación en EventService.
- **FR-003**: El sistema DEBE instrumentar ambos servicios con métricas (como mínimo: tasa de solicitudes, tasa de error y duración/latencia) expuestas de forma estándar.
- **FR-004**: El sistema DEBE contar con un backend de almacenamiento y consulta de trazas que reciba los spans de ambos servicios.
- **FR-005**: El sistema DEBE centralizar los logs estructurados de ambos servicios (y de la infraestructura de mensajería, en la medida de lo posible) en un backend de logs, eliminando la necesidad de revisar logs contenedor por contenedor.
- **FR-006**: El sistema DEBE contar con un backend de métricas que reciba o recolecte las métricas expuestas por ambos servicios.
- **FR-007**: El sistema DEBE ofrecer una única herramienta de visualización que consuma trazas, logs y métricas, sin requerir que el usuario abra herramientas distintas para cada señal.
- **FR-008**: La herramienta de visualización DEBE permitir, desde una traza o un span, navegar directamente a los logs correlacionados de esa misma operación (por `traceId`/`correlationId`), sin salir de la herramienta ni copiar identificadores manualmente.
- **FR-009**: El sistema DEBE ofrecer un dashboard de salud general con, como mínimo, tasa de error y latencia p50/p95/p99 desglosado por servicio (EventService, NotificationService), además de métricas básicas de la infraestructura compartida (broker de mensajería, base de datos, caché).
- **FR-010**: El sistema DEBE ofrecer una vista de logs que permita buscar y filtrar por `eventId`, `correlationId` o `traceId`.
- **FR-011**: El sistema DEBE ofrecer una vista tipo "service graph" que represente el mapa de dependencias entre EventService y NotificationService (incluyendo el paso por el broker de mensajería).
- **FR-012**: El sistema DEBE ofrecer alertas básicas de SLO configurables, como mínimo por tasa de error y por latencia p95, con umbrales definidos por servicio, sostenidos durante al menos 1 minuto antes de disparar la alerta.
- **FR-013**: Las alertas DEBEN reflejar un historial de cuándo se dispararon y cuándo se resolvieron, visible para el equipo.
- **FR-014**: Toda la instrumentación y los componentes de observabilidad DEBEN desplegarse dentro del entorno de docker-compose local ya existente, sin requerir servicios externos adicionales.
- **FR-015**: La instrumentación de observabilidad NO DEBE modificar la lógica de negocio existente de EventService ni de NotificationService, ni alterar el comportamiento actual del flujo de creación de eventos y disparo de notificaciones.
- **FR-016**: Si el backend de trazas, logs o métricas no está disponible, EventService y NotificationService DEBEN seguir procesando solicitudes con normalidad (la observabilidad es de mejor esfuerzo, nunca bloqueante ni causa de fallo del flujo de negocio).
- **FR-017**: El identificador de correlación ya existente en el sistema (`correlationId`, propagado hoy por header y por el mensaje de evento) DEBE poder usarse de forma intercambiable con el identificador de traza para buscar y correlacionar información en la herramienta de visualización.
- **FR-018**: La instrumentación NO DEBE incluir direcciones de email ni el contenido/cuerpo del mensaje de notificación como atributos de spans ni como campos de log; estos datos se identifican únicamente mediante identificadores (`eventId`, `notificationId`), nunca por su contenido.
- **FR-019**: El sistema DEBE recolectar métricas básicas de la infraestructura compartida (broker de mensajería, base de datos, caché) mediante exporters de métricas estándar, y exponerlas en el mismo backend de métricas usado por EventService y NotificationService.

### Key Entities

- **Trace (Traza)**: Representa el recorrido completo de una operación de negocio (ej. "crear evento") a través de ambos servicios y el broker de mensajería; se compone de uno o más spans y tiene un identificador único (`traceId`).
- **Span**: Representa un paso individual dentro de una traza (ej. "recibir solicitud HTTP", "persistir en outbox", "publicar en el broker", "consumir mensaje"), con su propia duración y estado (éxito/error).
- **Log Entry (Entrada de log)**: Línea de log estructurada emitida por un servicio, con campos mínimos de servicio, nivel, mensaje y los identificadores de correlación (`correlationId`/`traceId`, `eventId` cuando aplica).
- **Métrica de servicio**: Medición agregada por servicio a lo largo del tiempo (tasa de solicitudes, tasa de error, latencia), usada tanto en dashboards como en la evaluación de alertas.
- **Dashboard**: Vista preconfigurada en la herramienta de visualización (salud general, logs correlacionados, service graph) pensada para un tipo de consulta específico.
- **Regla de Alerta SLO**: Condición configurada sobre una métrica (ej. tasa de error > umbral, o latencia p95 > umbral) que, al cumplirse de forma sostenida, dispara un estado de alerta visible para el equipo.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Un desarrollador puede pasar de un `eventId`/`traceId` a los logs correlacionados de ambos servicios sin cambiar de herramienta ni copiar identificadores manualmente.
- **SC-002**: Ante una notificación que no se generó, un desarrollador puede identificar en qué paso del flujo se rompió usando únicamente la herramienta de visualización, sin revisar logs de cada contenedor por separado.
- **SC-003**: Un operador puede ver el estado (tasa de error, latencia p50/p95/p99) de ambos servicios y su mapa de dependencias desde una única pantalla, sin consultar trazas, logs y métricas en herramientas separadas.
- **SC-004**: El equipo recibe una señal visible de alerta cuando la tasa de error o la latencia p95 de un servicio se degrada de forma sostenida, sin depender de que un usuario final reporte el problema primero.
- **SC-005**: Toda solicitud de "crear evento" que efectivamente dispara su notificación queda representada como una única traza end-to-end, incluso cruzando el broker de mensajería.
- **SC-006**: La incorporación de observabilidad no interrumpe el flujo actual de creación de eventos y notificaciones y añade como máximo 50ms de latencia adicional en p95 por solicitud (el sistema sigue funcionando igual si el stack de observabilidad no está disponible).

## Assumptions

- Esta spec actualiza la elección de backend de trazas fijada en `constitution.md` §6.2 (que mencionaba Jaeger): por instrucción explícita de esta funcionalidad, el backend de trazas es **Grafana Tempo**, y se agrega **Prometheus** como backend de métricas (no contemplado en la redacción original de §6.2). El resto de decisiones de la constitución (Loki para logs, Grafana como visualización única, correlación vía `correlationId`) se mantienen sin cambios.
- Los umbrales exactos de las alertas de SLO (tasa de error y latencia p95) se definirán en la fase de planificación técnica; como punto de partida razonable para un entorno de demo/local se asume un umbral de tasa de error superior al 5% y de latencia p95 superior a 1 segundo, sostenidos durante al menos 1 minuto (ver Clarifications).
- Las alertas se consideran satisfechas con quedar visibles en el historial de la propia herramienta de visualización; no se requiere un canal externo de notificación (correo, Slack, webhook) para esta primera versión.
- Los logs de la infraestructura de mensajería (broker) se incorporan de forma best-effort (salida estándar del contenedor), dado que ese componente no se instrumenta con OpenTelemetry.
- No se modifica el modelo de autenticación/autorización existente; las interfaces del stack de observabilidad se exponen únicamente dentro del entorno local de docker-compose, no de cara a internet.
- La política de retención de logs y trazas no se define en esta spec (fuera de alcance); se usan los valores por defecto de cada backend para el entorno local/demo.
- El alcance de esta funcionalidad corresponde a la Fase 2 (Spec 02) descrita en `constitution.md` §6, construyendo sobre la base mínima ya definida en Fase 1 (logging estructurado, `correlationId`, health checks).
