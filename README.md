# SIGER

Sistema de gestión de restaurante. Backend .NET 10 / ASP.NET Core, EF Core/Npgsql sobre
PostgreSQL existente en Supabase y autenticación Supabase Auth. Desktop y Web no están
implementados en esta etapa.

Estado al 24 de septiembre de 2026: **AUTH REAL READY; BACKEND READY** dentro del alcance acordado.
421/421 tests aprobados, build Release con 0 errores y 0 advertencias. Los permisos de la tabla
residual `public.__EFMigrationsHistory` están corregidos y la auditoría automática transaccional
está implementada. Los límites de operación/despliegue siguen documentados; no ejecutar cambios
de esquema ni pruebas remotas de escritura sin autorización.

## Arquitectura

- Domain: modelo independiente.
- Application → Domain: casos de uso y contratos.
- Infrastructure → Application/Domain: PostgreSQL y adaptador Supabase Auth.
- API → Application/Infrastructure: único backend HTTP/JSON `/api/v1`.
- Desktop/Web: clientes futuros por HTTP, sin acceso directo a PostgreSQL.

Se conservan las carpetas de solución Core, Application, Infrastructure, API, Desktop, Web y Tests.
SIGER.Tests es el único proyecto de pruebas, organizado en Domain/, Application/, Infrastructure/
y API/. Los 421 casos existentes se conservaron; no se deben recrear proyectos de tests por capa.
La base de datos ya existe: no usar migrations, EnsureCreated ni dotnet ef para inicializarla.

## Desarrollo y verificación

Requiere SDK .NET 10; la compilación de toda la solución incluye el proyecto Windows Desktop.
Configuración local mediante User Secrets, nunca secretos en archivos versionados.
Consulta [configuración, ejecución, endpoints y compensación](SIGER.API/README.md).

```powershell
dotnet restore SIGER.slnx -p:Configuration=Release -p:ShouldRunNpmInstall=false -p:ShouldRunBuildScript=false
dotnet build SIGER.slnx --configuration Release --no-restore -p:ShouldRunNpmInstall=false -p:ShouldRunBuildScript=false
dotnet test SIGER.slnx --configuration Release --no-build --no-restore -p:ShouldRunNpmInstall=false -p:ShouldRunBuildScript=false
```

Para ejecutar únicamente las pruebas: `dotnet test SIGER.Tests --configuration Release`.
Cobertura opcional y ubicación temporal de resultados: consultar el README de API.

Estos tests no crean datos en Supabase. La prueba remota controlada de órdenes/pagos, rollback
y concurrencia se ejecutó por separado y sus datos temporales fueron eliminados; el usuario
dedicado Mesero se conserva. No ejecutar pruebas remotas de escritura sin autorización explícita.
