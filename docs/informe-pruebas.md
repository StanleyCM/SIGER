# SIGER · Evidencia del Entregable 4
Fecha: 2 de noviembre de 2026.

| Suite | Inicial | Final | Aprobadas | Fallidas | Omitidas |
|---|---:|---:|---:|---:|---:|
| Backend | 578 | 128 | 119 | 0 | 9 |
| Frontend | 36 | 23 | 23 | 0 | 0 |

La línea base backend se ejecutó antes de editar: 564 aprobadas y 14 omitidas. El conteo inicial frontend corresponde al inventario original (incluye dos filas de fecha). No se midió cobertura de líneas.

**Clasificación backend:** 76 unitarias (69 casos de Application con dependencias sustituidas, cinco de UnitOfWork con transacción simulada, uno de credenciales y uno de actor HTTP). Las otras 52 integran componentes: 28 HTTP con host de prueba y 24 de persistencia/modelo/proveedor, incluidas nueve PostgreSQL. Pasaron las 76 unitarias y 43 de integración. Los 23 casos web se cuentan aparte: prueban componentes o funciones de presentación; las llamadas a la API se simulan.

**Cinco unitarias para demostrar:** creación Pending con credencial; rechazo por falta de mesa; edición parcial sin perder identidad; preorden calculada con precios del servidor; rechazo de preorden duplicada sin sobrescribir el historial. El [inventario](auditoria-pruebas.md) identifica los métodos concretos.

**Procesos de integración:** disponibilidad pública, creación de reserva y preorden sin JWT, edición con token propio, rechazo de acceso ajeno, rol permitido/prohibido y orden → cocina → pago. En EF se conservan relaciones, precisión monetaria, unicidad, auditoría y consultas de solapamiento. En PostgreSQL se conservan esquema e idempotencia, rollback de esquema incompatible, última mesa en disputa, preorden concurrente con recarga, cancelación y conservación del historial, restricciones, rollback de edición, dos ediciones concurrentes y disponibilidad con reservas consecutivas. Se retuvieron nueve porque las carreras de edición y los dos tipos de rollback tienen causas distintas.

**Decisiones:** se reemplazó la matriz de roles/rutas por casos 401, 403 y acceso permitido, y las comprobaciones monetarias por un contrato conjunto. El caso de intervalos consecutivos pasó al flujo de disponibilidad y escritura. Se retiraron reflexión sobre propiedades, constructores y enums, matrices de paginación y columnas, y variantes repetidas del formulario. Domain no implementa transiciones en sus entidades; las reglas ejecutables se prueban en los servicios. También se retiró cobertura secundaria específica de reportes, promociones, configuración/JWKS y adaptador remoto de identidad: no se afirma que todos esos escenarios sean equivalentes. Se conservan compensaciones críticas en Application.

**Verificación:** dotnet build SIGER.slnx -c Release: correcto, cero errores y nueve advertencias de dependencias JavaScript. dotnet test SIGER.Tests/SIGER.Tests.csproj -c Release: 119/119 ejecutadas aprobadas; evidencia local en SIGER.Tests/TestResults/suite-reducida.trx. npm.cmd run build, npm.cmd run lint y npm.cmd run test: correctos. Se corrigieron dos imports de pruebas detectados por lint durante la limpieza. SIGER_TEST_POSTGRES se dejó vacío en el proceso de verificación; no se ejecutaron los nueve casos contra base de datos externa ni se modificó la protección local. No se cambió código productivo ni se hizo commit o push.

## Estado final del árbol de trabajo

M: modificado; D: eliminado; ??: nuevo.

```text
 M SIGER.Tests/API/AuditSecurityTests.cs
 D SIGER.Tests/API/ConfigurationAndLoggingTests.cs
 M SIGER.Tests/API/EndpointTests.cs
 M SIGER.Tests/API/PublicReservationSecurityTests.cs
 D SIGER.Tests/API/RoleAuthorizationTests.cs
 M SIGER.Tests/API/RouteContractTests.cs
 M SIGER.Tests/Application/ClosureRegressionTests.cs
 M SIGER.Tests/Application/GuestReservationEditTests.cs
 M SIGER.Tests/Application/GuestReservationTests.cs
 M SIGER.Tests/Application/OrderPaymentTests.cs
 M SIGER.Tests/Application/PreOrderTests.cs
 M SIGER.Tests/Application/PublicCatalogTests.cs
 D SIGER.Tests/Application/QueryContractTests.cs
 M SIGER.Tests/Application/ReservationAvailabilityTests.cs
 D SIGER.Tests/Application/SupportingServiceTests.cs
 D SIGER.Tests/Application/UserCatalogTests.cs
 M SIGER.Tests/Application/UserCompensationTests.cs
 D SIGER.Tests/Domain/ModelContractTests.cs
 M SIGER.Tests/Infrastructure/AuthAndUnitOfWorkTests.cs
 D SIGER.Tests/Infrastructure/AuthCompensationTests.cs
 M SIGER.Tests/Infrastructure/BusinessAuditTests.cs
 M SIGER.Tests/Infrastructure/ClosureSqlTests.cs
 M SIGER.Tests/Infrastructure/ExistingSchemaContractTests.cs
 M SIGER.Tests/Infrastructure/GuestPreOrderPostgresTests.cs
 M SIGER.Tests/Infrastructure/GuestReservationTokenTests.cs
 M SIGER.Tests/Infrastructure/ModelTests.cs
 D SIGER.Tests/Infrastructure/ReportSqlTests.cs
 M siger.web/src/test/ProductCatalog.test.jsx
 M siger.web/src/test/ReservationAvailability.test.jsx
 M siger.web/src/test/ReservationContact.test.jsx
 M siger.web/src/test/ReservationFlow.test.jsx
 M siger.web/src/test/ReservationSchedule.test.jsx
?? docs/auditoria-pruebas.md
?? docs/informe-pruebas.md
```
