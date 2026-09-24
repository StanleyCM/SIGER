# SIGER API

API HTTP/JSON versionada bajo `/api/v1`. El esquema PostgreSQL existe previamente:
el arranque no crea, migra ni elimina tablas. Los servicios de Application y
Infrastructure se registran con sus contratos existentes.

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

Se admiten tokens asimétricos RS256/ES256. La configuración remota de firma
debe verificarse posteriormente: los proyectos que aún emiten HS256 no
funcionan con esta validación JWKS. No se utiliza ServiceRoleKey como clave
de firma ni se emiten JWT propios.

El emisor por defecto es `{Supabase:Url}/auth/v1`; la audiencia es
`authenticated`. Se pueden configurar `Supabase:JwtIssuer` y
`Supabase:JwtAudience` cuando el proyecto lo requiera.

Después de validar el token se consulta User mediante su AuthUserId, incluyendo
Role, una vez por autenticación de la solicitud. Usuario y rol deben estar
activos. Los claims funcionales se construyen exclusivamente con el usuario
local; los roles del token no otorgan permisos. Los nombres de Role esperados
son Administrator, Waiter, Cook, Cashier y Client.

| Ruta base | Acceso y operaciones |
|---|---|
| /api/v1/auth/login | POST anónimo, login |
| /api/v1/users | Administración, listado/paginación, creación, edición, status y roles |
| /api/v1/categories | Administración, listado/paginación, creación, edición y status |
| /api/v1/products | Staff lee; Administrator administra; GET /available es público |
| /api/v1/tables | Staff lee; Administrator crea/edita; Waiter/Administrator cambia estado |
| /api/v1/orders | Staff lee; Waiter/Administrator crea, edita items y solicita cuenta |
| /api/v1/kitchen/orders | Cook/Administrator consulta y cambia a in-preparation o ready |
| /api/v1/payments | Cashier/Administrator procesa pagos y consulta /order/{orderId} |
| /api/v1/reservations | Waiter/Administrator administra; Client consulta y modifica las propias |
| /api/v1/promotions | Administrator administra; /active y /{id}/products/{productId} |
| /api/v1/reports | Administrator: /sales y /top-products, startDate/endDate requeridos |
| /api/v1/audits | Administrator: solo lectura por id o paginada |

Las órdenes requieren un creador interno según el contrato actual; su creación
no se expone al rol Client. El usuario responsable de órdenes y pagos se toma
de la identidad autenticada. Para Client, el filtro y propietario de reservas
se fijan al usuario local, y solo se permite cancelar mediante /status.
Las transiciones de cocina y pago usan sus endpoints dedicados; el PATCH de
estado general de órdenes permite Served/Cancelled.

## HTTP y operación

Los DTO exitosos se devuelven directamente: 200, 201 con Location para creación
y 204 para operaciones sin contenido. Errores: ProblemDetails o
ValidationProblemDetails (400/401/403/404/409/429/500) con traceId.
Result no tiene códigos de error tipados: ResultHttpExtensions centraliza
la compatibilidad con los mensajes actuales de Application.

Paginación: pageNumber=1 y pageSize=20, con máximo de 200. En cuerpos JSON,
los enums utilizan sus nombres ingleses como strings. Las fechas con offset se
normalizan a UTC antes de enviarse a PostgreSQL.

CORS lee `Cors:AllowedOrigins`, inicialmente vacío. No habilita credenciales
de cookies ni orígenes comodín. Por IP, login admite 5 solicitudes por minuto
y el catálogo público 60, configurables en `RateLimiting`.

El log propio registra método, path sin query, estado y duración; no cuerpos,
cabeceras ni secretos. Los errores registran tipo y traceId, no SQL ni detalles
del proveedor. Se deshabilitan categorías de log del proveedor que podrían
incluir información interna de las consultas/autenticación.

En Development: `/swagger` y `/openapi/v1.json`. Authorize acepta un access
token de Supabase. La documentación se omite fuera de Development.
El perfil https utiliza el certificado de desarrollo local.

## Verificación

La implementación se comprobó con un servidor HTTP en memoria, JWT firmados
con una clave efímera y servicios/repositorios simulados: autorización, tokens
inválidos, aislamiento de reservas, errores sanitizados, CORS, rate limiting,
Swagger y resolución de los registros DI reales. No se usaron secretos reales
ni se ejecutó SQL contra la base existente. La prueba con Supabase real queda
para después de configurar User Secrets y confirmar las claves de firma.
