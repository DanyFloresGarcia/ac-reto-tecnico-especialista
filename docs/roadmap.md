# Roadmap — Backlog a 6 Meses (Sistema Completo)

**Marco de trabajo**: Scrum, **12 Sprints de 2 semanas** cada uno (24 semanas ≈ 6 meses). Cada tarea avanza por 4 entornos — **DEV → QA → Staging → Production** — y solo pasa al siguiente cuando cumple su Definition of Done (build verde, tests pasando, y aprobación explícita del área de Soporte TI correspondiente, cuando aplica).

> Los Sprints 1–2 cubren el **MVP Core** del sistema (`EventService`, `NotificationService` y el frontend de registro de eventos) — la primera pieza funcional de punta a punta sobre la que se apoya el resto del roadmap. Los Sprints 3–12 extienden esa base al sistema completo descrito en `architecture.md`: identidad, observabilidad, búsqueda avanzada, ticketing con alta concurrencia, pagos, emisión de tickets QR, check-in, notificaciones multicanal e infraestructura Cloud/Híbrida.

Ver [`architecture.md`](./architecture.md) para el diseño técnico detrás de cada épica.

## Equipo (12 personas, roles fijos — no se asignan otros)

| Rol | Cantidad | Abreviatura usada en la tabla |
|---|---|---|
| Backend Developer | 2 | Backend |
| Frontend Developer | 2 | Frontend |
| Full Stack Developer | 1 | FullStack |
| QA Engineer | 2 | QA |
| UX Designer | 1 | UX |
| UI Designer | 1 | UI |
| Scrum Master | 1 | SM |
| Product Owner | 1 | PO |
| Líder Técnico | 1 | LT |

## Áreas de Soporte TI (se asignan solo donde de verdad aplican)

- **Arquitectura**: aprueba el diseño en los momentos que fijan una decisión difícil de revertir después — kickoff, elección del patrón de concurrencia anti-sobreventa, elección del patrón Saga, validación final pre-lanzamiento.
- **Base de Datos**: interviene cuando se modela o cambia un motor de persistencia — Postgres de cada servicio nuevo, y el índice de OpenSearch/Elasticsearch.
- **Seguridad**: define políticas de roles/IAM, valida el cumplimiento de datos de pago (PCI-DSS), y ejecuta la auditoría final antes de Production.
- **Plataforma**: Terraform, CI/CD, aprovisionamiento de infraestructura (Nube o On-Premise) y ventanas de despliegue.

## Épicas del roadmap (las 13 son obligatorias, no solo "Eventos")

1. Fundamentos & Arquitectura
2. MVP Core (Eventos)
3. Identidad y Seguridad (Keycloak)
4. Observabilidad
5. Búsqueda Avanzada
6. Ticketing & Reservas (alta concurrencia)
7. Pagos
8. Emisión de Tickets & Check-in
9. Notificaciones Multicanal
10. Seguridad & Compliance
11. Infraestructura Cloud/Híbrida (Terraform)
12. QA & Performance
13. Lanzamiento

## Backlog detallado

| Épica | Sprint (1 al 12) | Tarea principal del Backlog | Roles Asignados | Interacción Soporte TI | Entorno |
|---|---|---|---|---|---|
| Fundamentos & Arquitectura | 1 | Kickoff: aprobar el diseño de referencia completo a 6 meses (microservicios, contratos de eventos, motores de persistencia por servicio) | LT, PO, SM | **Arquitectura** (aprobación inicial del diseño completo) | DEV |
| Fundamentos & Arquitectura | 1 | Modelar `eventdb`/`notificationdb` y fijar la convención de contratos de mensajería (`EventCreated`) | LT, Backend (x2) | **Base de Datos** (modelado inicial) | DEV |
| MVP Core (Eventos) | 1–2 | Implementar `EventService` + `NotificationService`: creación/consulta de eventos, Outbox transaccional, consumo idempotente, reintentos + DLQ | Backend (x2), FullStack | — | DEV → QA |
| MVP Core (Eventos) | 2 | Construir el formulario "Registrar Evento" (Frontend) con validaciones inline y estados de carga/error | Frontend (x2), UI, UX | — | DEV → QA |
| MVP Core (Eventos) | 2 | Suite de pruebas automatizadas (dominio, aplicación, integración, Frontend) y checklist de aceptación del MVP | QA (x2), LT | — | QA |
| Infraestructura Cloud/Híbrida | 3 | Definir pipelines CI/CD y aprovisionar los 4 entornos con Terraform, incluyendo la decisión Nube vs. On-Premise por servicio | LT, FullStack | **Plataforma** (Terraform, CI/CD, aprovisionamiento) | DEV → Staging |
| Identidad y Seguridad | 3 | Desplegar Keycloak (realm, clients) y definir los 4 roles del sistema completo (Cliente, Promotor, Admin, Staff) | Backend (x2), LT | **Seguridad** (políticas de roles y auditoría del IdP) | DEV |
| Identidad y Seguridad | 4 | Migrar la validación JWT de `EventService`/`NotificationService` de local a Keycloak, sin modificar las policies de autorización existentes | Backend, LT | — | DEV → QA |
| Observabilidad | 4 | Instrumentar OpenTelemetry (trazas + métricas) en `EventService` y `NotificationService` | Backend (x2), LT | — | DEV |
| Observabilidad | 5 | Desplegar Jaeger/Loki/Grafana y construir dashboards + alertas básicas de SLO | FullStack, LT | **Plataforma** (aprovisionamiento del stack de observabilidad) | QA → Staging |
| Búsqueda Avanzada | 6 | Diseñar el modelo del índice de OpenSearch/Elasticsearch y el proyector asíncrono desde RabbitMQ (`EventCreated` → índice) | LT, Backend | **Base de Datos** (modelado del índice de búsqueda) | DEV |
| Búsqueda Avanzada | 6 | Implementar `SearchService` (proyección + endpoint de búsqueda avanzada) y su UI de filtros/resultados en el Frontend | Backend, Frontend, FullStack | — | DEV → QA |
| Ticketing & Reservas | 7 | Diseñar el patrón de concurrencia anti-sobreventa: hold con TTL en Redis + concurrencia optimista en Postgres | LT | **Arquitectura** (aprobación del patrón de concurrencia) | DEV |
| Ticketing & Reservas | 7 | Implementar `TicketingService`: endpoint de reserva, hold en Redis, `ticketingdb` | Backend (x2), FullStack | **Base de Datos** (modelado de `ticketingdb`) | DEV → QA |
| Ticketing & Reservas | 8 | Frontend de reserva/checkout, incluyendo manejo de expiración del hold en la UI | Frontend (x2), UX | — | DEV → QA |
| Pagos | 8 | Integrar `PaymentService` con Stripe (tokenización en el cliente vía Stripe.js/Elements, sin tocar datos de tarjeta en el backend) | Backend, LT | **Seguridad** (cumplimiento de datos de pago, alcance PCI-DSS) | DEV |
| Pagos | 9 | Implementar la Saga coreografiada entre `TicketingService` y `PaymentService` (`ReservationHeld` → `PaymentApproved`/`PaymentFailed`) | Backend (x2), LT | **Arquitectura** (aprobación del patrón Saga) | DEV → QA |
| Seguridad & Compliance | 9 | Revisión transversal de prevención de IDOR en todo endpoint con recurso "por dueño" (tickets/reservas de Cliente, eventos de Promotor) | QA (x2), LT | **Seguridad** (revisión de políticas de acceso por rol) | QA |
| Emisión de Tickets & Check-in | 10 | Diseñar el ticket con QR firmado (evento `TicketIssued`) y el modelo de validación online/offline | LT, Backend | — | DEV |
| Emisión de Tickets & Check-in | 10 | Implementar `CheckInService` (escaneo online + cache local firmada para modo offline) y la App de Staff | Backend, Frontend, UX | — | DEV → QA |
| Notificaciones Multicanal | 10 | Extender `NotificationService` para consumir `TicketIssued` y notificar por email/SMS/push según preferencia del Cliente | Backend, FullStack | — | DEV → QA |
| QA & Performance | 11 | Pruebas de carga del flujo reserva → pago bajo un escenario de alta concurrencia ("flash sale") | QA (x2), Backend | **Plataforma** (aprovisionamiento del entorno de carga) | Staging |
| Infraestructura Cloud/Híbrida | 11 | Diseñar alta disponibilidad (réplicas de Postgres, cluster de RabbitMQ) y autoscaling para los servicios nuevos | LT, FullStack | **Plataforma** (aprovisionamiento de alta disponibilidad) | Staging |
| Lanzamiento | 12 | Auditoría de seguridad final (compliance de pagos, gestión de secretos, dependencias) previa a Production | LT, QA | **Seguridad** (auditoría final pre-producción) | Staging |
| Lanzamiento | 12 | Desplegar a Production (blue-green) el sistema completo y ejecutar la retrospectiva final del roadmap | LT, FullStack, PO, SM | **Arquitectura** (validación final: implementado vs. diseñado) y **Plataforma** (ventana de despliegue productivo) | Production |

## Fuera de este roadmap (decisiones de producto, no de infraestructura)

Funcionalidades de negocio que no se derivan directamente de la arquitectura técnica descrita en `architecture.md` (por ejemplo: programas de fidelización, reventa de tickets entre usuarios, precios dinámicos) quedan deliberadamente fuera de este backlog de 6 meses — son decisiones de alcance de producto que el Product Owner debe priorizar por separado, no continuaciones naturales de la evolución arquitectónica que este documento cubre.
