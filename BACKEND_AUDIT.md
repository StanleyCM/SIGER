# Auditoría técnica y pruebas del backend SIGER

Fecha: 23 de septiembre de 2026. Alcance: Domain, Application, Infrastructure y API.

## Resultado ejecutivo

La arquitectura se conserva. Se crearon cuatro proyectos xUnit separados bajo la carpeta lógica Tests de SIGER.slnx, sin mover los proyectos existentes. Se corrigieron siete grupos de problemas reproducidos y se conservaron sus regresiones.

- Restore y build completos de .NET: correctos, **0 errores y 0 advertencias**.
- Pruebas finales: **275 exitosas, 0 fallidas, 0 omitidas**.
- Se ejecutó otra vez la suite con XPlat Code Coverage, también correctamente.
- **La integración real con PostgreSQL NO está validada todavía**: la conexión guardada en User Secrets es una URI PostgreSQL, formato que Npgsql no acepta directamente. La API inicia, pero la consulta del catálogo responde 500 antes de abrir una conexión.
- No se modificaron secretos, datos ni esquema de Supabase. No hubo DDL, migraciones, seed, inserciones, actualizaciones ni eliminaciones remotas.

## 1. Arquitectura y documentos de referencia

Referencias consideradas: Documento Arquitectura SIGER.pdf y SRS_SIGER_v1.0.pdf proporcionados previamente, más las instrucciones posteriores del modelo relacional y del backend.

La arquitectura extendida incluye Web, Cook, Client, reservas y promociones; el SRS inicial limita pedidos por Internet y enumera menos roles. Se conserva la evolución explícitamente solicitada por el usuario; no se habilitaron nuevos casos de uso para resolver esa diferencia documental.

Referencias productivas verificadas:

| Proyecto | Project References |
|---|---|
| Domain | Ninguna |
| Application | Domain |
| Infrastructure | Application, Domain |
| API | Application, Infrastructure |

No se detectaron ciclos ni referencias invertidas. Domain/Application no contienen SQL, EF, Supabase SDK, HTTP ni UI. Los servicios intercambian DTOs; las interfaces de repositorios utilizan entidades dentro del backend. El único IQueryable encontrado es un helper privado de OrderRepository, no un contrato público.

No se modificaron Desktop, Web, Program.cs, los proyectos productivos, sus paquetes ni sus configuraciones. Desktop únicamente participó en la compilación global de .NET. Los comandos desactivaron npm y el build de React.

## 2. Domain

Sin defectos estructurales detectados. Verificados:

- BaseEntity abstracta con Id long, sin fechas comunes artificiales.
- Las 12 entidades y los siete enums requeridos, sin duplicados.
- Colecciones inicializadas y no compartidas entre instancias.
- PromotionProduct con PromotionId/ProductId, sin Id artificial ni BaseEntity.
- Navegaciones opcionales y obligatorias, distinción Order.User/Client.
- Version long en Product/Table/Order.
- User sin Password/PasswordHash; AuthUserId Guid.
- Audit conserva payloads anterior/nuevo e IP como strings opcionales.
- Excepciones preservan mensaje y causa.

No se cambió código de Domain.

## 3. Bugs reproducidos y corregidos

| Problema | Causa y corrección mínima | Regresión |
|---|---|---|
| Ediciones obsoletas aceptadas | Application copiaba request.Version al valor actual; EF incrementaba desde la versión original recién cargada. Se compara la versión recibida con la cargada antes de mutar Product/Table/Order. El conflicto genera BusinessRuleException, que API mapea a 409. EF conserva su comprobación para carreras posteriores a la lectura. | Seis escenarios de versión obsoleta, además de metadata/incremento de Version |
| Reapertura de órdenes cerradas | UpdateStatusAsync no aplicaba la protección de Paid/Cancelled. Ahora devuelve fallo sin mutar ni guardar. | Paid y Cancelled no pueden cambiar estado ni detalles/cuenta |
| Marcar Paid sin pago | El servicio permitía asignar Paid directamente. Se exige usar PaymentService. | Paid_status_requires_payment_workflow |
| Login con rol inactivo | AuthService revisaba al usuario, pero no al rol; API rechazaba después el mismo perfil. Se alinea el login con el control de usuario y rol activos. | Escenario inactive-role |
| Password null aceptado en creación | El validador opcional omitía null. La creación normaliza a vacío para rechazarlo antes del proveedor. No se almacena ni retorna. | Escenario null-password |
| Respuestas inválidas de Supabase Auth | JSON incompleto podía devolver Guid.Empty/token vacío, provocar NullReferenceException o filtrar fragmentos mediante errores del parser. Se validan identidad/token/expiración y se sanitizan errores JSON sin conservar su inner exception. | Login y creación con {}, null, JSON inválido, usuario nulo o sin identidad |
| Modelos/proveedores EF recreados por ámbito | MapSIGEREnums creaba traductores diferentes en cada configuración scoped. Npgsql no reutilizaba su caché; tras acumular proveedores EF lanzaba ManyServiceProvidersCreatedWarning como excepción. Se reutilizan traductores estáticos inmutables con los mismos labels. | Repeated_scopes_reuse_the_same_EF_model y la suite completa de metadata |

La primera ejecución de Application tuvo 12 fallos: 11 correspondían a regresiones productivas y uno a un mock de promoción que no reproducía el estado de una lectura posterior. Se corrigió ese fixture, no el servicio de promociones. También se corrigieron supuestos de pruebas sobre identidad temporal, serialización JWKS y orden alfabético de labels en metadata. No se relajaron los tests que reproducían bugs.

### Archivos productivos modificados

1. SIGER.Application/Services/AuthService.cs: comprobación de rol.
2. SIGER.Application/Services/UserService.cs: password requerido en creación.
3. SIGER.Application/Services/ProductService.cs: conflicto de versión previo a cambios.
4. SIGER.Application/Services/TableService.cs: conflicto de versión previo a cambios.
5. SIGER.Application/Services/OrderService.cs: protección de estados y versiones; cierre pagado solo por pago.
6. SIGER.Infrastructure/Authentication/SupabaseAuthService.cs: respuestas remotas válidas y errores sanitizados.
7. SIGER.Infrastructure/Persistence/PostgreSqlEnumMappings.cs: reutilización de traductores.

Cambios de soporte: SIGER.slnx incorpora los tests; .gitignore ignora TestResults; cuatro proyectos de tests, tres scripts de diagnóstico/cobertura y este informe. El archivo dotnet-tools.json ya estaba sin seguimiento al comenzar y se preservó.

## 4. Application: comprobaciones y límites

Se probaron los 12 servicios con repositorios, IUnitOfWork e IAuthProvider simulados, sin PostgreSQL:

- Usuarios: creación, rol ausente/inactivo, duplicado, fallo remoto, edición, habilitar/deshabilitar y salida sin contraseña.
- Auth: credenciales rechazadas, perfil inexistente/inactivo, rol inactivo, token/expiración sin alteraciones.
- Categorías/productos/mesas: operaciones, validaciones, disponibilidad/estado, mapping, filtros y paginación.
- Órdenes: creador/cliente/mesa/productos, cantidades, disponibilidad, precio del catálogo, subtotales y total; agregar/editar/retirar items; cuenta y estado; protección de órdenes cerradas.
- CreateOrder ocupa mesa y guarda dentro de IUnitOfWork.
- Kitchen: listado, Pending -> InPreparation -> Ready, inexistentes y estados terminales.
- Payment: pago válido, duplicado, monto/usuario/orden inválidos; pago + Paid + mesa Available dentro del delegado transaccional; fallo intermedio propagado. También TakeAway.
- Reservas: creación, validación de fecha/personas/relaciones, edición y cancelación.
- Promociones: rangos/porcentaje/productos, deduplicación, relaciones y activación.
- Reportes: delegación de filtros/límite/token y resultados.
- Auditoría: actor opcional, payloads, filtros y mapping.
- Result y PaginatedResult: invariantes y errores.

Los mocks demuestran coordinación e invocaciones, **no prueban rollback físico ni aislamiento SQL**.

## 5. Infrastructure y acceso a datos

### Modelo EF sin conexión

Se usaron opciones reales Npgsql, sin EF InMemory. Las pruebas inspeccionan las 12 tablas:

rol, usuario, categoria, producto, mesa, orden, detalle_orden, pago, reserva, promocion, promocion_producto, auditoria.

Comprobaciones: claves e identidad BIGINT, nombres físicos de columnas, FKs, navegaciones, nullability, precisión monetaria, longitudes representativas, índices y cinco índices únicos, delete behaviors, PK compuesta, JSONB, conversor INET y TIMESTAMPTZ.

Enums físicos verificados: estado_mesa, estado_orden, metodo_pago, estado_pago, origen_orden, tipo_orden y estado_reserva, con los labels españoles del documento de arquitectura. Metadata de Npgsql enumera labels alfabéticamente; se compara su conjunto, no el orden numérico del enum C#.

Product/Table/Order tienen concurrency token BIGINT, default 1 e incremento desde OriginalValue. El guardado fue interceptado para evitar SQL. **No se provocó una DbUpdateConcurrencyException contra PostgreSQL real**; sí se verifica su traducción HTTP y la protección de versiones en Application.

La prueba de ChangeTracker confirma que Update de una orden ya rastreada deja catálogo, usuario y mesa sin modificaciones artificiales, detecta cambios de cantidad, altas de detalles y eliminación de huérfanos. No había motivo para reemplazar los repositorios.

### Revisión estática de consultas

- Los listados paginados aplican Count/Skip/Take antes de materializar; usan AsNoTracking.
- OrderRepository limita primero IDs y luego carga relaciones de esa página mediante split queries.
- Las lecturas destinadas a actualización son tracked; las de catálogo/listados/auth son mayormente no-tracking.
- Includes resuelven las relaciones utilizadas por los DTO; no se usa lazy loading.
- ReportRepository ejecuta CountAsync, SumAsync y GroupBy/Select/Take sobre IQueryable antes de ToListAsync. La forma LINQ es de agregación en servidor; ejecución y planes reales quedan pendientes.
- CancellationToken se transmite a las operaciones EF/HTTP relevantes.
- IUnitOfWork realiza begin/commit/rollback, reutiliza una transacción existente y propaga fallos normales. Los tests usan DatabaseFacade/IDbContextTransaction simulados, no una base sustituta.

No se encontraron Migrate, EnsureCreated ni EnsureDeleted en código productivo. No hay migrations, ModelSnapshot, DbContextFactory de migrations, WeatherForecast ni Class1 en el backend. JwtService.cs sigue siendo un placeholder vacío no registrado; **no emite tokens**. Se preservó para no borrar estructura existente innecesariamente.

## 6. API y seguridad

Los 12 controllers conservan rutas /api/v1, delegan a servicios y no usan EF/SQL/Supabase directamente.

Pruebas WebApplicationFactory con servicios/repositorios desconectados y claves efímeras:

- 200, 201/Location, 204, 400, 401, 403, 404, 409, 429 y 500.
- JWT: issuer, audience, expiración, firma, subject y tokens malformados.
- RS256 y ES256; usuario/rol local inactivo o ausente rechazado.
- Claims de rol/usuario enviados en el token no otorgan privilegios.
- Administrator, Waiter, Cook, Cashier, Client y roles desconocidos.
- UserId de órdenes/pagos tomado de identidad local; item ID tomado de la ruta.
- Client no accede a reservas ajenas ni reasigna su propietario; solo cancela las propias.
- JSON inválido, enums numéricos y filtros fuera de rango producen 400.
- Login y catálogo público producen 429 sin sleeps.
- CORS acepta solo origen configurado y no habilita credentials.
- Swagger/OpenAPI disponibles en Development, no expuestos en Production.
- Excepciones sanitizadas sin mensajes privados ni stack traces.
- Logging propio omite Authorization, cookies, query y cuerpo de solicitud.

JWT utiliza ConfigurationManager/JWKS, actualización ante clave desconocida y algoritmos RS256/ES256. No usa ServiceRoleKey para firmar ni crea un segundo emisor JWT. Se probó lectura de JWKS con dos claves simuladas; no se realizó rotación real.

La documentación oficial de [Supabase Signing Keys](https://supabase.com/docs/guides/auth/signing-keys) respalda la verificación asimétrica mediante JWKS. Una clave pública ES256 real fue observada, pero eso **no demuestra un login completo ni el algoritmo de todos los tokens actualmente emitidos**. Un proyecto que siga emitiendo HS256 requeriría revisar su configuración, no desactivar validaciones.

### Secretos

Se escanearon 222 archivos versionables del árbol de trabajo con patrones de claves Supabase, URI PostgreSQL, Password/ServiceRoleKey, JWT literales y claves privadas. Hubo dos coincidencias de asignaciones de variables/null en UserService y sus pruebas, revisadas como falsos positivos.

No se encontraron secretos reales en los archivos inspeccionados. Los valores sintéticos de tests no son credenciales. User Secrets externos quedaron fuera del escaneo y sus valores no se imprimieron. Este análisis de patrones no equivale a una auditoría forense de todo el historial Git ni prueba la configuración remota de RLS.

## 7. Proyectos, ejecución y cobertura

| Proyecto de tests | Exitosos | Fallidos | Omitidos |
|---|---:|---:|---:|
| SIGER.Domain.Tests | 23 | 0 | 0 |
| SIGER.Application.Tests | 104 | 0 | 0 |
| SIGER.Infrastructure.Tests | 48 | 0 | 0 |
| SIGER.API.Tests | 100 | 0 | 0 |
| Total | 275 | 0 | 0 |

Paquetes de pruebas: xunit 2.9.3, runner.visualstudio 3.1.5, Microsoft.NET.Test.Sdk 18.7.0, coverlet.collector 6.0.4, Moq 4.20.72 donde se usa y Microsoft.AspNetCore.Mvc.Testing 10.0.12 en API. No se agregaron paquetes productivos.

Cobertura de líneas, unión de las cuatro mediciones por ruta absoluta y número de línea:

| Ensamblado | Líneas cubiertas / total | Cobertura |
|---|---:|---:|
| Domain | 125 / 125 | 100 % |
| Application | 1092 / 1104 | 98,91 % |
| Infrastructure | 536 / 834 | 64,27 % |
| API, incluyendo código generado | 556 / 816 | 68,14 % |
| API, solo archivos propios fuera de obj | 420 / 431 | 97,45 % |

Para transparencia: el XML individual de Application.Tests reporta 97,55 % de líneas y 95,23 % de ramas para Application. El XML de API.Tests reporta 68,13 % de líneas y 56,93 % de ramas para API; incluye código generado de OpenAPI. La unión de líneas evita promediar porcentajes entre suites y deduplica ubicaciones; por eso sus cifras pueden diferir ligeramente de las de Coverlet. No se alteraron los XML, no se excluyeron archivos para ejecutar el collector y no se estableció un threshold.

Los porcentajes altos de modelos/DTO no sustituyen pruebas de negocio o de base real. La cobertura de Infrastructure es menor porque no se ejecutaron sus consultas contra PostgreSQL.

Comandos finales ejecutados desde la raíz:

```powershell
dotnet restore SIGER.slnx -p:ShouldRunNpmInstall=false -p:ShouldRunBuildScript=false
dotnet build SIGER.slnx --no-restore -p:ShouldRunNpmInstall=false -p:ShouldRunBuildScript=false
dotnet test SIGER.slnx --no-restore -p:ShouldRunNpmInstall=false -p:ShouldRunBuildScript=false --logger trx --results-directory TestResults/final
dotnet test SIGER.slnx --no-restore -p:ShouldRunNpmInstall=false -p:ShouldRunBuildScript=false --collect:"XPlat Code Coverage" --results-directory TestResults/final-coverage
./scripts/Get-BackendCoverage.ps1
```

Resultados TRX: TestResults/final. XML Cobertura: TestResults/final-coverage/*/coverage.cobertura.xml. Están ignorados por Git, no son fuentes productivas. Build final: 0 errores, 0 advertencias.

## 8. Smoke test real, separado de los tests

Se usaron los User Secrets existentes sin mostrar ni modificar valores. Se inició un proceso temporal de API en loopback y se detuvo al terminar.

| Comprobación real | Resultado |
|---|---|
| Arranque de API | Correcto |
| GET /swagger/index.html | 200 |
| GET /openapi/v1.json | 200 |
| GET /api/v1/products/available | 500 |
| GET del JWKS público de Supabase | 200; una clave ES256 |
| Login real | Pendiente de usuario de prueba |

Diagnóstico de la consulta fallida: ConnectionStrings:SIGERDatabase tiene formato URI postgresql://…; NpgsqlConnectionStringBuilder no puede interpretarlo. Es un ArgumentException al analizar la configuración, **antes de conectar o ejecutar SQL**, no evidencia de un mapping incorrecto del esquema real.

Pendiente local: guardar la misma conexión en formato Npgsql/ADO.NET, por ejemplo:

```text
Host=<host>;Port=<puerto>;Database=<base>;Username=<usuario>;Password=<contraseña>;SSL Mode=Require
```

Los marcadores son ilustrativos. Deben usarse los valores exactos del proyecto y escapar correctamente componentes con caracteres especiales. No se inventó ni cambió host, usuario, contraseña o modalidad de conexión. Luego repetir:

```powershell
./scripts/Invoke-ReadOnlySmoke.ps1
dotnet run --file scripts/ReadOnlyDiagnostics.cs
```

Los scripts son opt-in y no forman parte de dotnet test. El diagnóstico devuelve código 1 cuando la configuración no es válida; no es un error de compilación. No imprime secretos ni mensajes internos del proveedor.

**Login real pendiente de usuario de prueba.** No se solicitó ni probó ninguna contraseña real.

## 9. Pendientes y recomendaciones antes de Desktop

Prioridad inmediata:

1. Corregir el formato del User Secret y repetir GET del catálogo hasta obtener 200, aunque la lista esté vacía.
2. Con un usuario de prueba autorizado, verificar login real, issuer/audience, AuthUserId, nombres de roles y consultas autenticadas. La autenticación sintética ya está probada; el recorrido remoto completo no.
3. En un entorno de pruebas autorizado, no sobre la BD actual, probar commit/rollback físico, carreras sobre mesa/orden/producto y pagos simultáneos. Metadata y mocks no prueban esos efectos.
4. Contrastar las 12 tablas, constraints, índices y labels con el esquema remoto real. Solo se contrastó el modelo local/documentado.

Hallazgos funcionales/técnicos adicionales conservados para una decisión explícita, sin refactor amplio:

- UserService.Update cambia el correo local, pero IAuthProvider no tiene actualización de correo remoto. Debe definirse cómo sincronizarlo antes de ofrecer esa edición en Desktop.
- Crear/habilitar/deshabilitar usuarios combina Supabase Auth y guardado local, sin transacción distribuida ni compensación si el segundo falla. Hace falta un procedimiento de recuperación.
- Kitchen no valida toda la máquina de estados: fuera de Paid/Cancelled acepta transiciones como Served -> Ready o Ready -> InPreparation. Definir transiciones permitidas y pruebas antes de exponer todos los botones.
- Cancelar una orden no libera automáticamente la mesa; el pago sí lo hace. Acordar la regla de cancelación sin asumir que cualquier mesa puede liberarse.
- Editar cantidades recalcula UnitPrice con el precio actual del catálogo. Debe acordarse si se conserva el precio capturado originalmente.
- ReservationService no comprueba capacidad máxima, solapamiento temporal ni usuario activo. Falta definir/implementar esas reglas antes de reservas reales.
- Crear órdenes/promociones consulta productos individualmente: coste N consultas para N productos. Una consulta por lote sería una mejora delimitada posterior.
- Catálogo disponible, promociones activas y cola de cocina no están paginados. Medir tamaño y establecer límites si crecen.
- La paginación usa desplazamiento int y algunos ordenamientos no tienen desempate por Id. Páginas extremas pueden desbordar el cálculo; conviene limitarlo y estabilizar el orden.
- Result -> HTTP depende de mensajes exactos; cambios futuros de texto pueden alterar el status. Conviene evolucionar a códigos tipados como tarea separada.
- No se verificaron planes SQL, rendimiento bajo carga, RLS remoto, recuperación ante caída de red durante rollback ni rotación real de claves.
- La auditoría actual permite consultar registros, pero no hay una política completa de generación automática de eventos de negocio.
- La configuración de rate limiting usa IP directa; revisar proxy/ForwardedHeaders y orígenes CORS al desplegar, sin habilitar confianza indiscriminada.

## 10. Límites respetados

No hubo modificaciones de Desktop/Web, cambios de UI, nuevos casos de uso, Docker/Testcontainers, EF InMemory, cambio de TargetFramework, migraciones, modificación de esquema, seed ni escrituras de prueba remotas. No se eliminaron archivos existentes ni se reorganizaron proyectos. No se hizo git add, commit ni push.

La auditoría termina aquí. No se continúa con Desktop ni Web.

