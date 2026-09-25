# SIGER API

API HTTP/JSON versionada bajo `/api/v1`. El esquema PostgreSQL existe previamente:
el arranque no crea, migra ni elimina tablas. Los servicios de Application y
Infrastructure se registran con sus contratos existentes.

Estado al 24 de septiembre de 2026: **AUTH REAL READY; BACKEND READY** dentro del alcance acordado.
La tabla residual `public.__EFMigrationsHistory` está protegida con RLS y sin privilegios de cliente;
la auditoría automática está implementada y probada. Build Release limpio y 421 tests aprobados.

## Configuración local

El proyecto tiene UserSecretsId. Desde la raíz del repositorio:

```powershell
dotnet user-secrets set "ConnectionStrings:SIGERDatabase" "TU_CONNECTION_STRING" --project SIGER.API
dotnet user-secrets set "Supabase:Url" "TU_SUPABASE_URL" --project SIGER.API
dotnet user-secrets set "Supabase:ServiceRoleKey" "TU_SERVICE_ROLE_KEY" --project SIGER.API
dotnet run --project SIGER.API --launch-profile https
```

Los placeholders deben sustituirse manualmente. No versionar valores reales.
En despliegue se utilizan `ConnectionStrings__SIGERDatabase`, `Supabase__Url`
y `Supabase__ServiceRoleKey`. La API informa las claves faltantes al arrancar
sin imprimir sus valores. La conexión se utiliza bajo demanda por el ORM.

## Autenticación y permisos

Login delega a IAuthService y al adapter SupabaseAuthService existente.
La API valida firma, emisor, audiencia y expiración con JwtBearer y las claves
de `{Supabase:Url}/auth/v1/.well-known/jwks.json`, mediante los componentes
Microsoft IdentityModel. Las claves se almacenan en caché y se actualizan
mediante ConfigurationManager.

Se admiten tokens asimétricos RS256/ES256. El 24 de septiembre de 2026 se
verificaron login real, JWT ES256 y firma usando el JWKS público real.
El mismo JWT de Mesero obtuvo 200 en lectura de mesas y 403 en usuarios;
sin token, usuarios respondió 401. Los tokens
HS256 no funcionan con esta validación JWKS. No se utiliza ServiceRoleKey
como clave de firma ni se emiten JWT propios.

El emisor por defecto es `{Supabase:Url}/auth/v1`; la audiencia es
`authenticated`. Se pueden configurar `Supabase:JwtIssuer` y
`Supabase:JwtAudience` cuando el proyecto lo requiera.

Después de validar el token se consulta User mediante su AuthUserId, incluyendo
Role, una vez por autenticación de la solicitud. Usuario y rol deben estar
activos. Los claims funcionales se construyen exclusivamente con el usuario
local; los roles del token no otorgan permisos. Los nombres de Role esperados
son Administrador, Mesero, Cocinero, Cajero y Cliente, exactamente como están
persistidos en public.rol. Se centralizan en las constantes existentes de
`SIGER.API/Authorization/Policies/SigerClaims.cs`; sus identificadores técnicos
siguen en inglés. No se traducen claims ni se crean roles alternativos.

| Policy técnica (sin renombrar) | Valores reales de Role.Name permitidos |
|---|---|
| AdministratorOnly | Administrador |
| StaffOnly | Administrador, Mesero, Cocinero, Cajero |
| WaiterOrAdministrator | Administrador, Mesero |
| KitchenOrAdministrator | Administrador, Cocinero |
| CashierOrAdministrator | Administrador, Cajero |
| Reservations | Administrador, Mesero, Cliente |

| Ruta base | Acceso y operaciones |
|---|---|
| /api/v1/auth/login | POST anónimo, login |
| /api/v1/users | Administración, listado/paginación, creación, edición, status y roles |
| /api/v1/categories | Administración, listado/paginación, creación, edición y status |
| /api/v1/products | Staff lee; Administrador administra; GET /available es público |
| /api/v1/tables | Staff lee; Administrador crea/edita; Mesero/Administrador cambia estado |
| /api/v1/orders | Staff lee; Mesero/Administrador crea, edita items y solicita cuenta |
| /api/v1/kitchen/orders | Cocinero/Administrador consulta y cambia a in-preparation o ready |
| /api/v1/payments | Cajero/Administrador procesa pagos y consulta /order/{orderId} |
| /api/v1/reservations | Mesero/Administrador administra; Cliente consulta y modifica las propias |
| /api/v1/promotions | Administrador administra; /active y /{id}/products/{productId} |
| /api/v1/reports | Administrador: /sales y /top-products, startDate/endDate requeridos |
| /api/v1/audits | Administrador: solo lectura por id o paginada |

Las órdenes requieren un creador interno según el contrato actual; su creación
no se expone al rol Cliente. El usuario responsable de órdenes y pagos se toma
de la identidad autenticada. Para Cliente, el filtro y propietario de reservas
se fijan al usuario local, y solo se permite cancelar mediante /status.
Las transiciones de cocina y pago usan sus endpoints dedicados; el PATCH de
estado general de órdenes permite Served/Cancelled.

Cocina solo avanza Pending → InPreparation → Ready; Mesero sirve desde Ready.
Cancelar libera la mesa en transacción si no quedan otras órdenes activas.
Cambiar cantidad conserva el UnitPrice capturado, no el precio actual del catálogo.
Reservas usan intervalos de dos horas [inicio, fin), con bloqueo para Pending/Confirmed;
Cancelled/Completed no bloquean. Se validan usuario activo, capacidad y mesa utilizable.

## Usuarios y compensación Auth/PostgreSQL

Email local y Auth representan el mismo correo de login; no son dos contactos independientes.
Las operaciones existentes verifican la identidad y su estado remoto antes de modificarla;
AuthUserId no cambia. Una discrepancia previa se rechaza para reconciliación, no se oculta.

El endpoint administrativo de Supabase actualiza email directamente, sin el flujo de confirmación
self-service. El adaptador no envía email_confirm al editar ni cambia opciones globales.
Se comprobó mailer_autoconfirm=false en el proyecto real. La creación administrativa conserva
su comportamiento anterior de usuario confirmado. Véase el [contrato oficial de Supabase](https://supabase.com/docs/reference/javascript/auth-admin-updateuserbyid).

Creación, email y activación/desactivación tienen compensación automática si Auth cambia y falla
el guardado local: eliminación de la identidad recién creada o restauración del estado anterior.
No existe transacción distribuida. La recuperación tiene un timeout independiente de 30 segundos,
no se cancela con la petición y verifica snapshots antes de revertir. Si el commit SQL es incierto,
primero intenta determinar si el estado nuevo quedó persistido.

Una compensación fallida, un resultado incierto o un cambio posterior detectado emite un error
crítico estructurado (EventId 6201) y HTTP 500 seguro con OperationId. Los logs no incluyen correo,
password, tokens, claves, conexión ni mensajes originales del proveedor. Correlacionar OperationId
y los IDs técnicos, consultar ambos sistemas y autorizar una reconciliación antes de actuar.
No reintentar a ciegas. La comparación previa no es CAS atómico frente a cambios externos desde
Dashboard; una caída abrupta del proceso tampoco tiene recuperación durable mediante outbox.
Estos límites y el último recurso operacional están documentados en la auditoría.

Las escrituras administrativas remotas se prueban con HTTP simulado. No se cambió email/estado/rol
del usuario real para demostrar compensación. No usar la cuenta de prueba para pruebas destructivas.

## Auditoría automática

Los cambios persistidos de usuarios, categorías, productos, mesas, órdenes/detalles, pagos,
reservas y promociones/asociaciones generan auditoría desde SIGERDbContext. Incluye email/estado,
precio/cantidad, cocina, servido, cancelación y pago. No se agregaron triggers ni lógica a controllers.

Negocio y audit confirman juntos: un fallo de auditoría provoca rollback; con UoW externo se usa
savepoint sin confirmar anticipadamente. En administración Auth/local se conserva la compensación:
Auth → local + audit → commit, o rollback SQL y compensación Auth. No hay éxito auditado falso.

Actor: ID local del principal autenticado. IP: RemoteIpAddress, sin confiar en headers arbitrarios.
Operaciones internas sin petición pueden usar NULL, como permite el esquema. JSONB usa allowlists
centrales de IDs, estados, importes y fechas; registra nombres de campos cambiados sin serializar
navegaciones ni texto libre. No guarda email, nombres, teléfonos, AuthUserId, passwords, tokens,
headers, claves, conexiones, notas/descripciones/URLs ni referencias libres. Para cambios de email
queda constancia del campo cambiado, no de las direcciones personales.

Solo Administrador consulta GET /api/v1/audits y /{id}; no existe endpoint de escritura arbitraria.
Listado paginado (máximo 200), más reciente primero. GET/reportes/Swagger/smoke/login no generan audit.
No se auditan cambios externos por SQL/Dashboard. No hay purga automática de historial.

Prueba real: categoría temporal id 2 creada y eliminada; auditorías 1/Create y 2/Delete conservadas.
Operación interna con actor/IP NULL, sin claims artificiales. Actor HTTP y rollback se cubren en tests.

## HTTP y operación

Los DTO exitosos se devuelven directamente: 200, 201 con Location para creación
y 204 para operaciones sin contenido. Errores: ProblemDetails o
ValidationProblemDetails (400/401/403/404/409/429/500) con traceId.
Result no tiene códigos de error tipados: ResultHttpExtensions centraliza
la compatibilidad con los mensajes actuales de Application.

Paginación: pageNumber=1 y pageSize=20, con máximo de 200; un offset mayor que int.MaxValue
se rechaza con 400 antes del repositorio. En cuerpos JSON,
los enums utilizan sus nombres ingleses como strings. Las fechas con offset se
normalizan a UTC antes de enviarse a PostgreSQL.

CORS lee `Cors:AllowedOrigins`, inicialmente vacío. No habilita credenciales
de cookies ni orígenes comodín. Por IP, login admite 5 solicitudes por minuto
y el catálogo público 60, configurables en `RateLimiting`.

El log de solicitudes registra método, path sin query, estado y duración;
no cuerpos ni cabeceras. En Production los errores registran tipo y traceId.
En Development se conserva la excepción para diagnóstico local; esos logs
pueden contener detalles internos del proveedor y deben mantenerse privados.
Los fallos administrativos de usuario se sanitizan antes de llegar a este middleware.
Los detalles no se devuelven en ProblemDetails. Se deshabilitan categorías de
log del proveedor que podrían incluir consultas/autenticación.

En Development: `/swagger` y `/openapi/v1.json`. Authorize acepta un access
token de Supabase. La documentación se omite fuera de Development.
El perfil https utiliza el certificado de desarrollo local y los puertos
7203 (HTTPS) y 5179 (HTTP). Ejecutar solo HTTP puede producir el warning
`Failed to determine the https port for redirect`: falta un listener/puerto
HTTPS en ese arranque, no indica un fallo PostgreSQL. No se deshabilitó la
ejecución HTTP local. Si falta el certificado, configurarlo manualmente en
el equipo antes de usar el perfil https.

## Verificación

Desde la raíz, sin instalar dependencias ni ejecutar el build React:

SIGER.Tests es el único proyecto xUnit, con carpetas Domain, Application, Infrastructure y API.
Mantiene los fixtures, WebApplicationFactory y comportamiento de paralelización por defecto;
no hay proyectos de pruebas separados por capa ni referencias productivas hacia el proyecto de tests.

```powershell
dotnet restore SIGER.slnx -p:Configuration=Release -p:ShouldRunNpmInstall=false -p:ShouldRunBuildScript=false
dotnet build SIGER.slnx --configuration Release --no-restore -p:ShouldRunNpmInstall=false -p:ShouldRunBuildScript=false
dotnet test SIGER.slnx --configuration Release --no-build --no-restore -p:ShouldRunNpmInstall=false -p:ShouldRunBuildScript=false
```

Cobertura opcional y explícita; los nuevos resultados pueden guardarse fuera del repositorio:

```powershell
$sigerCoveragePath = Join-Path ([IO.Path]::GetTempPath()) 'SIGER.Tests-coverage'
dotnet test SIGER.Tests --configuration Release --no-build --no-restore --collect:"XPlat Code Coverage" --logger trx --results-directory $sigerCoveragePath
./scripts/Get-BackendCoverage.ps1 -ResultsPath $sigerCoveragePath
```

El informe único contiene los cuatro ensamblados productivos; el script mantiene su cobertura
separada, incluida API propia sin obj. Los tests normales no activan TRX/coverage automáticamente.

Resultado final: 421 pruebas aprobadas, 0 fallidas, 0 omitidas;
build Release con 0 errores y 0 advertencias. Los tests usan
WebApplicationFactory, claves JWT efímeras y dependencias simuladas; nunca
conectan automáticamente con Supabase. Un flujo HTTP utiliza OrderService,
KitchenService y PaymentService reales con repositorios/UoW simulados. Las pruebas de auditoría
añaden SQLite en memoria, transacciones reales locales y Auth simulado; nunca escriben en Supabase.

Por separado se probó PostgreSQL real: orden, cocina, servido, pago, orden pagada y mesa disponible;
rollback físico de orden/pago y versiones concurrentes Product/Table/Order. Las operaciones
restringidas al Mesero se comprobaron mediante servicios reales, sin falsear claims ni políticas.
Los datos temporales se eliminaron por sus IDs/marca, preservando roles y usuario de prueba.
El ajuste final de bloqueo de mesa en pago se verificó con tests, sin repetir escrituras tras limpiar.
La comparación de metadata cubrió 12 tablas y siete enums; no se modificó esquema en esta etapa.

El smoke final después del build usó un login real con TestAuth de User Secrets:
200/401/403, Swagger Bearer y GET /api/v1/products/available → 200 (`[]`).
No imprimir ni copiar las credenciales TestAuth:Email/TestAuth:Password a archivos o reportes.
El contrato de login devuelve access token, no refresh token. Una UI futura deberá respetar ese contrato.

Scripts conservados: Get-BackendCoverage.ps1 (unión de cobertura), Invoke-ReadOnlySmoke.ps1
(GET sin login, Release por defecto) y ReadOnlyDiagnostics.cs (diagnóstico sin DDL/escrituras).
No son autorización para crear identidades o datos de negocio. Los resultados generados permanecen
ignorados; su eliminación local fue bloqueada por la herramienta durante el cierre.
La carpeta Migrations vacía sí fue eliminada; no se generaron ni ejecutaron migrations.
