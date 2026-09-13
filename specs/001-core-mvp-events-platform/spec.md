# Feature Specification: Core MVP - Plataforma de Eventos

**Feature Branch**: `001-core-mvp-events-platform`

**Created**: 2026-09-12

**Status**: Draft

**Input**: User description: "Spec 01: Core MVP - Plataforma de Eventos — andamiaje e implementación de la Fase 1 (MVP) del reto técnico: creación/listado de eventos con zonas, notificación asíncrona desacoplada de esa creación, resiliencia/idempotencia en el procesamiento de la notificación, seguridad basada en roles, y un entorno 100% local levantable con un solo comando, documentado de forma autocontenida."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Admin registra un nuevo evento (Priority: P1)

Un Admin necesita publicar un nuevo evento con sus zonas de precio/capacidad para que quede disponible en la plataforma, y espera que esa creación dispare, de forma desacoplada, el resto del procesamiento del sistema (por ejemplo, una notificación) sin tener que esperar a que ese procesamiento termine.

**Why this priority**: Es la única acción de escritura del MVP y el disparador de todo el flujo de valor (creación → notificación asíncrona). Sin esto no hay nada que listar ni que notificar.

**Independent Test**: Puede probarse enviando una solicitud de creación con datos válidos y verificando que (a) el evento aparece luego en el listado, y (b) el sistema deja evidencia de haber iniciado el procesamiento de notificación asociado, todo sin depender de las demás historias.

**Acceptance Scenarios**:

1. **Given** datos válidos de evento con al menos una zona, **When** el Admin envía la creación, **Then** el sistema crea el evento con estado inicial "borrador" y confirma la creación con un identificador único.
2. **Given** datos de evento incompletos o con una zona inválida (ej. capacidad ≤ 0, precio negativo, o cero zonas), **When** el Admin envía la creación, **Then** el sistema rechaza la solicitud con retroalimentación específica por campo y no crea ningún evento.
3. **Given** un evento recién creado exitosamente, **When** la creación termina de persistirse, **Then** el sistema registra el disparo de una notificación asociada a ese evento, de forma independiente a la respuesta ya entregada al Admin.

---

### User Story 2 - Consultar eventos existentes (Priority: P2)

Un usuario autenticado (Admin o User) necesita ver la lista de eventos disponibles y el detalle de uno en particular, para conocer qué eventos existen y sus zonas.

**Why this priority**: Es la acción de lectura principal del MVP; sin ella la creación de la Historia 1 no tiene forma de verificarse ni de aportar valor visible.

**Independent Test**: Puede probarse consultando el listado y el detalle por id de forma aislada, incluso con eventos precargados de ejemplo, sin depender de crear un evento nuevo en el mismo test.

**Acceptance Scenarios**:

1. **Given** que existen eventos en la plataforma, **When** un usuario autenticado solicita el listado, **Then** el sistema devuelve todos los eventos junto con sus zonas.
2. **Given** que un mismo listado se solicita dos veces en un lapso corto sin cambios en los datos, **When** llega la segunda solicitud, **Then** el sistema la responde de forma notablemente más rápida, sin repetir todo el procesamiento de la primera.
3. **Given** un identificador de evento que no existe, **When** un usuario autenticado solicita su detalle, **Then** el sistema responde de forma clara que no fue encontrado, sin errores ambiguos.

---

### User Story 3 - Acceso restringido por rol (Priority: P3)

El sistema necesita garantizar que solo un Admin pueda crear eventos, mientras que cualquier usuario autenticado (Admin o User) pueda consultarlos, y que nadie sin credenciales pueda hacer ninguna de las dos cosas.

**Why this priority**: Es una restricción de seguridad transversal a las Historias 1 y 2; sin ella cualquiera podría crear o alterar el catálogo de eventos.

**Independent Test**: Puede probarse enviando la misma solicitud de creación/lectura con tres combinaciones de credenciales (ninguna, rol sin privilegio, rol autorizado) y verificando la respuesta esperada en cada caso, de forma independiente al contenido específico del evento.

**Acceptance Scenarios**:

1. **Given** una solicitud sin credenciales, **When** se intenta crear o consultar un evento, **Then** el sistema la rechaza indicando que no está autenticada.
2. **Given** credenciales válidas pero de un rol sin privilegio de creación, **When** se intenta crear un evento, **Then** el sistema la rechaza distinguiendo claramente "no autorizado" de "no autenticado".
3. **Given** credenciales válidas de un rol autorizado, **When** se realiza la acción correspondiente, **Then** la solicitud se completa exitosamente.

---

### User Story 4 - Procesamiento confiable de la notificación (Priority: P4)

El sistema necesita procesar la notificación de "evento creado" de forma resiliente: sin duplicar el procesamiento si la misma notificación llega más de una vez, y sin perder rastro de un fallo si el procesamiento no puede completarse.

**Why this priority**: Valida el objetivo central del reto (desacoplamiento, mensajería, idempotencia, reintentos, observabilidad mínima) más allá del camino feliz de las Historias 1 y 2.

**Independent Test**: Puede probarse reenviando manualmente el mismo evento de notificación y, por separado, forzando un fallo de procesamiento, verificando en ambos casos el resultado esperado sin necesidad de ejecutar el flujo completo de creación.

**Acceptance Scenarios**:

1. **Given** que la notificación de un evento ya fue procesada exitosamente, **When** la misma notificación se entrega nuevamente, **Then** el sistema detecta el duplicado y no genera un segundo registro de procesamiento.
2. **Given** que el procesamiento de una notificación falla de forma persistente, **When** se agotan los reintentos definidos, **Then** el sistema deja un registro consultable de ese fallo y conserva la notificación en un lugar inspeccionable, en vez de descartarla silenciosamente.

---

### User Story 5 - Puesta en marcha autocontenida (Priority: P5)

Una persona que clona el repositorio por primera vez, con solo los prerrequisitos indicados instalados, necesita poder levantar la plataforma completa y ejecutar el flujo de punta a punta siguiendo únicamente la documentación del repo.

**Why this priority**: Condiciona la posibilidad de evaluar o usar cualquiera de las historias anteriores; sin esto, el resto del sistema es inaccesible para alguien nuevo.

**Independent Test**: Puede probarse en una máquina limpia siguiendo solo los pasos documentados, sin conocimiento previo del proyecto, y verificando que el sistema queda operativo sin pasos adicionales no documentados.

**Acceptance Scenarios**:

1. **Given** una máquina limpia con solo los prerrequisitos instalados, **When** se siguen los pasos documentados, **Then** la plataforma completa (todos sus componentes) queda disponible con un único comando de arranque.
2. **Given** que la plataforma acaba de iniciar, **When** se consulta el estado de cada componente, **Then** cada uno informa si está realmente listo para atender solicitudes (no solo si el proceso arrancó).

---

### Edge Cases

- ¿Qué pasa si se intenta crear un evento sin ninguna zona, o con nombre/lugar que excede la longitud máxima permitida? El sistema debe rechazarlo (ver Historia 1, escenario 2).
- ¿Qué pasa si un componente del que depende otro (ej. base de datos, cola de mensajes, caché) no está listo cuando el segundo intenta arrancar? El arranque debe ordenarse de forma que un componente dependiente solo inicie cuando sus dependencias estén verificablemente listas.
- ¿Qué pasa si ocurre un error interno no anticipado durante cualquier operación? El llamante nunca debe recibir detalles internos (traza de pila, mensaje de excepción real, detalles de conexión a datos); debe recibir un mensaje de error genérico y seguro.
- ¿Qué pasa si un mismo origen envía un volumen excesivo de solicitudes en poco tiempo? El sistema debe rechazar el exceso en vez de degradarse o caerse.
- ¿Qué pasa con los datos ya creados si la plataforma se reinicia? Deben seguir existiendo tras el reinicio (persistencia durable, no en memoria).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: El sistema DEBE permitir que un Admin autorizado cree un nuevo evento con nombre, fecha, lugar y una o más zonas (cada zona con nombre, precio y capacidad).
- **FR-002**: El sistema DEBE rechazar una solicitud de creación cuando falten campos requeridos, el nombre/lugar excedan sus límites de longitud definidos, no se incluya ninguna zona, o alguna zona tenga capacidad ≤ 0 o precio negativo — devolviendo retroalimentación específica por campo.
- **FR-003**: El sistema DEBE persistir un evento nuevo junto con todas sus zonas como una única operación atómica, de forma que nunca pueda existir un evento parcialmente creado (por ejemplo, sin sus zonas).
- **FR-004**: El sistema DEBE permitir a cualquier usuario autenticado (Admin o User) consultar el listado completo de eventos y el detalle de un evento específico por su identificador.
- **FR-005**: El sistema DEBE responder de forma clara e inequívoca cuando se solicita el detalle de un identificador de evento que no existe.
- **FR-006**: El sistema DEBE responder solicitudes repetidas de listado/detalle de forma notablemente más eficiente cuando los datos subyacentes no han cambiado en una ventana corta de tiempo (del orden de 60 segundos), y DEBE reflejar de inmediato cualquier evento nuevo en la siguiente consulta de listado tras su creación.
- **FR-007**: El sistema DEBE notificar a otra parte de la plataforma cada vez que un evento se crea exitosamente, de manera que el procesamiento adicional (por ejemplo, una comunicación simulada) ocurra de forma asíncrona, sin bloquear la respuesta al creador.
- **FR-008**: El sistema DEBE garantizar que la notificación de "evento creado" solo se emita para eventos que efectivamente quedaron persistidos de forma durable (nunca notificar un evento que falló al guardarse, ni dejar un evento persistido sin notificar).
- **FR-009**: El sistema DEBE procesar cada notificación de "evento creado" una única vez desde el punto de vista del resultado de negocio: si la misma notificación se entrega más de una vez, DEBE detectar el duplicado y omitir su reprocesamiento.
- **FR-010**: El sistema DEBE reintentar un procesamiento de notificación fallido un número acotado de veces (3 intentos, con espera entre cada uno) antes de desistir, y DEBE dejar registro consultable del fallo final — ninguna notificación puede perderse silenciosamente.
- **FR-011**: El sistema DEBE restringir la creación de eventos exclusivamente a usuarios con rol "Admin", y DEBE exigir autenticación para cualquier consulta de lectura; las solicitudes anónimas de creación o lectura DEBEN rechazarse.
- **FR-012**: El sistema DEBE rechazar solicitudes de escritura cuando el rol del solicitante no tenga el privilegio necesario (ej. un "User" intentando crear), distinguiendo esa respuesta de la de "no autenticado".
- **FR-013**: El sistema DEBE proteger las operaciones de creación y de listado contra un volumen excesivo de solicitudes provenientes de un mismo origen, rechazando el exceso por encima de un umbral definido por minuto.
- **FR-014**: El sistema NUNCA DEBE exponer al llamante detalles internos de un error inesperado (traza de pila, mensajes de excepción reales, detalles de conexión a datos); DEBE responder con un mensaje de error genérico y seguro, registrando el detalle completo únicamente en sus registros internos.
- **FR-015**: El sistema NUNCA DEBE registrar en sus logs datos sensibles (credenciales completas, encabezados de autorización crudos); cada entrada de log relacionada con una solicitud DEBE poder rastrearse mediante un identificador compartido de esa solicitud.
- **FR-016**: El sistema DEBE exponer una forma de verificar que cada componente está vivo y que sus dependencias críticas son alcanzables, de modo que el arranque/orquestación pueda esperar disponibilidad real en vez de una espera fija arbitraria.
- **FR-017**: La plataforma completa (todos sus componentes, almacenamiento de datos e infraestructura de mensajería) DEBE poder iniciarse con un único comando en la máquina de un desarrollador, arrancando los componentes dependientes solo después de que sus dependencias estén verificablemente listas.
- **FR-018**: El sistema DEBE persistir los datos de forma durable a través de reinicios (un evento creado antes de un reinicio debe seguir existiendo después).
- **FR-019**: La documentación DEBE permitir que una persona nueva, usando solo una máquina limpia con los prerrequisitos indicados, configure el entorno, aplique las migraciones de datos, cargue datos iniciales y ejecute el flujo completo de creación-y-notificación sin ningún paso no documentado.
- **FR-020**: El sistema DEBE iniciar con al menos un evento de ejemplo disponible (precargado o mediante un paso de siembra documentado), con una cantidad y forma de zonas conocida de antemano, de forma que la capacidad de listado pueda verificarse de forma automática (no solo "no vacía") inmediatamente tras el primer arranque.
- **FR-021**: El sistema DEBE restringir las solicitudes entre orígenes (CORS) del navegador a una lista explícita de orígenes confiables, sin permitir un origen comodín, tanto para operaciones de lectura como de escritura.
- **FR-022**: El sistema DEBE negarse a iniciar (fallar rápido) en vez de operar con un valor por defecto inseguro cuando falte o esté vacío un secreto crítico de seguridad (ej. la clave de firma de tokens).

### Key Entities *(include if feature involves data)*

- **Event**: representa un evento a realizarse. Atributos clave: nombre, fecha, lugar, estado de ciclo de vida (se crea en un estado inicial tipo "borrador") y una o más zonas asociadas.
- **Zone**: representa un nivel de precio/capacidad dentro de un evento (nombre, precio, capacidad). Solo existe como parte de un `Event`; no tiene identidad ni ciclo de vida propio fuera de él.
- **Registro de procesamiento de notificación**: representa la constancia durable de que una notificación de "evento creado" fue recibida y procesada (o finalmente falló) por la capacidad de notificación, usada para garantizar el procesamiento único y para dejar trazabilidad de los fallos.
- **Rol de usuario**: `Admin` (puede crear eventos y consultarlos) vs. `User` (solo puede consultarlos); determina qué acciones puede realizar cada solicitante autenticado.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Un Admin puede registrar un evento nuevo (con zonas) y recibir confirmación de creación en un único ciclo de solicitud-respuesta, con la notificación correspondiente observable en el flujo de notificación en cuestión de segundos.
- **SC-002**: Una segunda solicitud de listado idéntica, realizada poco después de la primera sin cambios en los datos, se responde de forma notablemente más rápida que la primera.
- **SC-003**: El 100% de las solicitudes de detalle sobre un identificador de evento inexistente reciben una respuesta de "no encontrado", nunca un error de servidor ni una respuesta vacía ambigua.
- **SC-004**: Entregar la misma notificación de creación de evento dos veces produce exactamente un registro de procesamiento durable — nunca dos.
- **SC-005**: Al forzar un fallo de procesamiento de notificación, el sistema intenta la recuperación exactamente 3 veces antes de desistir, y el 100% de esos fallos forzados queda registrado en un lugar consultable.
- **SC-006**: El 100% de los intentos de creación de evento sin autenticación o con un rol sin privilegio son bloqueados, y el 100% de los intentos con credenciales autorizadas se completan exitosamente.
- **SC-007**: En el 100% de los escenarios de error probados, ningún error no controlado expone detalles internos de excepción al llamante.
- **SC-008**: Una persona con solo los prerrequisitos documentados puede pasar de un repositorio recién clonado a un sistema completamente operativo (creación de evento → notificación observada) usando un único comando de arranque y las instrucciones del README, sin ningún paso manual no documentado.
- **SC-009**: Todos los componentes de la plataforma se reportan como "listos" únicamente cuando sus dependencias reales son alcanzables, eliminando fallos de arranque por orden incorrecto.

## Assumptions

- El stack tecnológico, el estilo de arquitectura y las decisiones específicas de infraestructura (motor de base de datos, mensajería, capa de caché, runtime) ya están fijados de antemano por la constitución del proyecto y no son una decisión abierta de esta feature; esta especificación describe comportamientos y resultados requeridos, y el plan de implementación mapeará esos resultados sobre ese stack ya definido.
- "Admin" y "User" son los únicos dos roles necesarios para este MVP; no hay alcance de registro propio ni flujo de login real — las credenciales/roles se provisionan fuera de banda para efectos de este ejercicio técnico.
- Los estados de ciclo de vida del evento más allá de la creación inicial (Publicado/Cancelado), así como la búsqueda avanzada y la paginación, están explícitamente fuera de alcance de este MVP y se difieren a fases futuras.
- El envío real de correos es **opcional y configurable**, no una limitación fija de este MVP: por defecto (sin configuración SMTP) el sistema simula la comunicación saliente dejando constancia de su contenido en los registros del sistema, lo cual satisface el requisito de "notificar" sin exigir credenciales de correo reales a quien evalúe el MVP; si se provee un servidor SMTP (variables `Smtp:*`), el sistema envía el correo real a través de él, cumpliendo la letra literal del enunciado ("enviar un correo") para quien quiera validarlo end-to-end.
- La observabilidad más allá de registros estructurados básicos, un identificador de correlación por solicitud y verificaciones de salud (es decir, trazas distribuidas/métricas completas con paneles visuales) se difiere a una fase posterior.
- Reemplazar el mecanismo de autenticación simplificado por un proveedor de identidad completo (login real, gestión de usuarios) se difiere a una fase posterior.
- Compartir un único token de demo con rol `Admin` entre todos los evaluadores/usuarios de este MVP es un **riesgo aceptado explícitamente**: no hay trazabilidad de auditoría por usuario individual en esta fase (se resuelve junto con la migración a un IdP real en una fase posterior).
- La ausencia de vulnerabilidades de tipo IDOR en este MVP se debe a que ningún endpoint expone datos "por usuario" (no existe, por ejemplo, un endpoint de "mis eventos"); si una fase futura agrega un endpoint de ese tipo, esta asunción deja de ser válida y debe revisarse explícitamente en ese momento.
- `POST /events` no está diseñado para ser idempotente ante reintentos de red iniciados por el propio cliente (un timeout seguido de un reintento puede crear dos eventos duplicados); se acepta como limitación conocida de este MVP, ya que no se define un mecanismo de idempotency-key en el contrato de esta fase.
