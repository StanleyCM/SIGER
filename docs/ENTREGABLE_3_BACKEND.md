# Entregable 3 — backend de invitados y preórdenes

Estado: implementación local; **DDL de Supabase pendiente de autorización explícita**.
No desplegar este backend contra el esquema anterior: las consultas EF de reservas y órdenes
ya requieren las columnas nuevas. No se ejecutan migraciones ni DDL al iniciar la API.

## Comportamiento implementado

- Reserva gratuita sin cuenta, sin usuarios ficticios y sin llamadas a Supabase Auth.
- Nombre y teléfono obligatorios; correo opcional; fecha futura, personas y observaciones.
- Asignación automática de mesa: capacidad suficiente y estado distinto de `OutOfService`.
  El modelo actual no tiene un indicador adicional de mesa activa. `Occupied`/`Reserved`
  describen su situación presente y no impiden por sí solos una reserva futura.
- Reservas `Pending`/`Confirmed` bloquean intervalos semiabiertos de dos horas.
  Dos reservas contiguas pueden compartir mesa. Se bloquean las candidatas en orden de ID
  y se vuelve a consultar el solapamiento después de adquirir el bloqueo, en la transacción.
- Las reservas nuevas quedan `Pending`. Respuesta: **«Reserva recibida: pendiente»**.
- Preorden mediante `Order`/`OrderDetail`; una por reserva, sin tabla nueva.
  `Origin=Web`, `Type=Table`, mesa de la reserva, `UserId=null`, `ClientId=null`,
  `Status=PreOrdered`. El valor C# es 6; los anteriores conservan 0–5.
- La preorden no ocupa/libera mesas, no entra en cocina, no se puede pagar ni solicitar cuenta,
  ni editar/cancelar mediante el flujo genérico de órdenes operativas.
  La cancelación interna de la reserva cancela también su preorden pendiente, sin borrarla.
- La reprogramación interna de una reserva invitada mantiene su preorden pendiente en la
  misma mesa de la reserva y actualiza el vencimiento de su credencial. No convierte al invitado
  en usuario. Los contactos se muestran solamente en el DTO de reservas internas protegido.
- Precios y total se calculan en el servidor. Se exigen productos disponibles con categoría activa.
  Las promociones son informativas y no modifican precios ni agregan impuestos.

## Credencial y límites

Token de 32 bytes aleatorios, expresado en 64 caracteres hexadecimales. Se guarda únicamente
SHA-256 (32 bytes) y vencimiento. La comparación usa tiempo constante. Se entrega una sola vez
en el cuerpo de creación y vence al terminar el intervalo de la reserva (fecha/hora + 2 horas).
No es un JWT y no concede roles. No hay recuperación/reemisión pública en esta etapa.

Enviar `X-Reservation-Token` en las consultas de la reserva y en creación/consulta de su preorden.
No se acepta por query string ni por URL. Ausencia, valor incorrecto, ID ajeno y vencimiento
devuelven el mismo 401. La preorden revalida la credencial y el estado después de bloquear
la reserva. Reservas pasadas, canceladas o completadas no aceptan preórdenes.
Las respuestas de reservas usan `Cache-Control: no-store`; el token no se incluye en `Location`.

Límites del contrato público:

| Campo/operación | Límite |
|---|---|
| Nombre / teléfono / correo | 150 / 30 / 150 caracteres |
| Personas | 1–100, además de capacidad real de la mesa |
| Observaciones de reserva | 500 caracteres |
| Productos distintos por preorden | 1–50; combinar productos repetidos |
| Cantidad por producto | 1–100 |
| Notas por producto | 300 caracteres |
| Página de productos | 1–100 elementos |
| Cuerpo de reserva / preorden | 16 KiB / 32 KiB |
| Lectura pública | 60 solicitudes/minuto/IP, configurable |
| Escritura pública | 5 solicitudes/15 minutos/IP, compartidas entre reservas y preórdenes; configurable |

No se registran cuerpos ni cabeceras de credenciales. La auditoría usa listas explícitas de
valores permitidos y excluye contactos, observaciones, token y hash. No se habilita logging
de parámetros EF. JWT, Supabase Auth y RBAC internos conservan su configuración.

## Contrato HTTP

| Método | Ruta | Acceso |
|---|---|---|
| GET | `/api/v1/public/categories` | Anónimo; solo activas |
| GET | `/api/v1/public/products?pageNumber=1&pageSize=20&categoryId=1` | Anónimo; disponibles y categoría activa; filtro opcional |
| GET | `/api/v1/public/products/{id}` | Anónimo; 404 si no publicable |
| GET | `/api/v1/public/promotions` | Anónimo; activas y vigentes en la hora del servidor; IDs de productos publicables |
| POST | `/api/v1/public/reservations` | Anónimo |
| GET | `/api/v1/public/reservations/{id}` | Credencial de esa reserva |
| POST | `/api/v1/public/reservations/{id}/preorder` | Credencial de esa reserva |
| GET | `/api/v1/public/reservations/{id}/preorder` | Credencial de esa reserva |

Las rutas administrativas existentes mantienen sus políticas. El antiguo
`/api/v1/products/available` conserva su contrato; la nueva Web debe consumir el catálogo
bajo `/api/v1/public`, que además filtra categorías inactivas.

Ejemplo de cuerpo de reserva (usar una fecha futura con zona horaria):

```json
{
  "name": "Nombre del visitante",
  "phone": "+58 412 1234567",
  "email": null,
  "reservationDateTime": "2030-01-15T19:00:00-04:00",
  "numberOfPeople": 2,
  "notes": null
}
```

201 devuelve `reservation`, `message`, `accessToken`, `accessTokenExpiresAt`.
La consulta posterior devuelve únicamente `id`, `reservationDateTime`, `numberOfPeople`,
`status`, `notes`. Fechas de salida UTC; estados JSON en inglés.

Ejemplo de preorden:

```json
{"items":[{"productId":1,"quantity":2,"notes":"Sin sal"}]}
```

201 devuelve `id`, `reservationId`, `status`, `total` e `items` con precio/subtotal calculados.
Un duplicado devuelve 409; una preorden aún inexistente devuelve 404 tras validar credencial.
No enviar precio ni total: esos campos no forman parte del DTO de entrada.

CORS Development permite `http://localhost:49338` (Vite). Producción conserva la lista
explícita `Cors:AllowedOrigins`; no se utiliza `AllowAnyOrigin`. Configurar el origen HTTPS
real al desplegar. `RateLimiting:PublicWritePermitLimit` configura las escrituras.

## Cambio exacto de base de datos propuesto

Archivo: [003_guest_reservations_preorders.sql](../scripts/database/003_guest_reservations_preorders.sql).
**Preparado y probado localmente; no ejecutado en Supabase.**

| Objeto | Cambio |
|---|---|
| `public.reserva.id_usuario` | Permitir NULL; mantener FK existente |
| `public.reserva.id_mesa` | Mantener NOT NULL y FK existente |
| `reserva.nombre_contacto` | Agregar `varchar(150)` nullable para registros históricos |
| `reserva.telefono_contacto` | Agregar `varchar(30)` nullable para registros históricos |
| `reserva.email_contacto` | Agregar `varchar(150)` nullable |
| `reserva.token_acceso_hash` | Agregar `bytea` nullable |
| `reserva.token_acceso_expira` | Agregar `timestamptz` nullable |
| `ck_reserva_contacto_invitado` | Invitado sin usuario debe tener nombre y teléfono no vacíos |
| `ck_reserva_credencial` | Hash y vencimiento juntos; hash de 32 bytes; invitados deben tener credencial |
| `public.orden.id_usuario` | Permitir NULL, sujeto a la restricción de responsable |
| `orden.id_reserva` | Agregar `bigint` nullable |
| `fk_orden_reserva` | FK a `reserva.id_reserva`, `ON DELETE RESTRICT` |
| `public.estado_orden` | Agregar al final `Preordenada` |
| `ck_orden_responsable` | NULL solo para Web/Mesa asociada a reserva, con mesa, sin cliente, sin cuenta solicitada y estado Preordenada/Cancelada |
| `ck_orden_preorden` | Preordenada exige Web/Mesa, reserva y mesa, sin responsable ni cliente ni cuenta solicitada |
| `ux_orden_id_reserva` | Único parcial `(id_reserva) WHERE id_reserva IS NOT NULL` |
| `ux_reserva_token_acceso_hash` | Único parcial `(token_acceso_hash) WHERE token_acceso_hash IS NOT NULL` |
| `ix_reserva_mesa_fecha_disponibilidad` | Btree `(id_mesa, fecha_hora)` |

Los índices se crean solo si no hay uno válido equivalente, incluso con otro nombre.
No se eliminan/reconstruyen índices existentes ni se actualizan registros históricos.
No se cambian RLS, permisos, políticas, funciones de negocio ni tablas de Auth.
En una repetición, las restricciones existentes se comparan con definiciones normalizadas
por PostgreSQL mediante restricciones temporales de comparación que se retiran antes del commit;
las restricciones originales permanecen.

El archivo usa una transacción, `lock_timeout=5s`, `statement_timeout=60s` y bloqueos de
`reserva`/`orden`. Una incompatibilidad aborta el cambio completo. Los CHECK comparan el
estado como texto para no usar el nuevo valor enum antes del COMMIT, de acuerdo con la
[documentación PostgreSQL](https://www.postgresql.org/docs/current/sql-altertype.html).
Debe ejecutarse en una ventana de mantenimiento; reiniciar la API después para renovar
el modelo y los metadatos del proveedor. Ningún código de la API lo aplica automáticamente.

Consulta remota de solo lectura realizada el 03/10/2026: 0 reservas, 0 órdenes, ninguna de
las seis columnas nuevas, enum aún sin `Preordenada`; `reserva.id_mesa` continúa NOT NULL.
Esta comprobación es una observación de ese momento, no una garantía ante cambios posteriores.

## Pruebas y reproducción

Se conservaron los 421 casos originales, ajustando cinco expectativas de contrato por los
cambios aprobados de enum, nulabilidad e índices. Las pruebas nuevas cubren Application,
API, credenciales, modelo EF y PostgreSQL real local.

Las pruebas PostgreSQL requieren una instancia **local desechable**. Nunca leen User Secrets.
Rechazan hosts remotos; crean bases aleatorias `siger_test_*`, cargan una fixture sintética
del esquema anterior, ejecutan el script 003 y eliminan únicamente sus bases al terminar.
La fixture no es un script de instalación del sistema ni debe ejecutarse en Supabase.
Sin `SIGER_TEST_POSTGRES`, esos casos aparecen omitidos de forma explícita.

```powershell
$env:SIGER_TEST_POSTGRES = 'Host=127.0.0.1;Port=55439;Username=siger_test;Database=postgres'
dotnet restore SIGER.slnx -p:Configuration=Release -p:ShouldRunNpmInstall=false -p:ShouldRunBuildScript=false
dotnet build SIGER.slnx -c Release --no-restore -p:ShouldRunNpmInstall=false -p:ShouldRunBuildScript=false
dotnet test SIGER.slnx -c Release --no-build --no-restore -p:ShouldRunNpmInstall=false -p:ShouldRunBuildScript=false
```

Las propiedades de MSBuild evitan disparar instalación/build de Web desde la solución.
La instancia local utilizada fue PostgreSQL 17.11 temporal, escuchando solo en 127.0.0.1.
Se verifican: ocho reservas simultáneas para una mesa, ocho preórdenes simultáneas para una
reserva, intervalos contiguos, persistencia en scopes distintos, unicidad y FK reales,
cancelación/historial, auditoría, filtros SQL antes de paginar, reejecución del script,
índices equivalentes y rollback ante una columna incompatible.

Resultado final del 03/10/2026: restore correcto; build Release con **0 errores y 0 warnings**;
**515 tests, 515 aprobados, 0 fallidos y 0 omitidos** (421 originales + 94 nuevos).
Incluye los nueve casos con PostgreSQL real local habilitado. `git diff --check` sin errores.
Resultado TRX local generado en `SIGER.Tests/TestResults/entregable3-backend.trx` (ignorado por Git).
No se hicieron commits ni push; HEAD permanece en `3d56bb1584370bb1c5dd3a412c26d318e3c47839`.
Web y Desktop no presentan cambios. La instancia temporal de PostgreSQL se detuvo al finalizar. La revisión automática bloqueó por política la eliminación de sus archivos; permanecen en `C:\Users\Stanley\AppData\Local\Temp\siger-pg-validation-20261003` (binarios portables y datos sintéticos). No se instaló un servicio de Windows.

## Pendientes y límites de esta etapa

- Revisar y autorizar por separado el DDL de Supabase; después aplicarlo y verificar el entorno real.
  Las pruebas locales no afirman equivalencia completa de la fixture con todo Supabase.
- No se desarrollaron Web ni Desktop, ni activación operativa de preórdenes. Esa activación futura
  deberá asignar responsable interno y validar/ocupar la mesa antes de cocina o pagos.
- No hay reservas de stock, pagos, notificaciones, recuperación del token ni promociones automáticas.
- Duración de dos horas vigente; horarios comerciales, horizonte de fechas y antelación mínima
  adicional no están definidos. Los límites 100 personas/unidades son límites técnicos, no capacidad garantizada.
- Rate limiting en memoria por instancia/IP: antes de desplegar detrás de un proxy o escalar a
  varias instancias, configurar IPs de proxy confiables y decidir límites distribuidos. No se cambió esa arquitectura.
- La selección puede esperar bloqueos en horas concurridas; todos los escritores deben seguir
  el protocolo transaccional de mesa. El SQL no agrega una exclusión temporal para escrituras externas directas.
- Divergencias históricas conservadas fuera de alcance: nombres `pk_/ix_` de EF frente a
  `*_pkey/idx_*` remotos, FK de cliente con SET NULL remoto frente a Restrict en EF,
  columna remota `fecha_solicitud_cuenta` sin mapear y comportamientos históricos de otras FK.
- La documentación Arquitectura v1.0 y el SRS no se modificaron. La nueva aprobación de invitados
  y preórdenes define el alcance de esta etapa pese a la exclusión de pedidos web del SRS original.

## Archivos cambiados

El inventario exacto se registra al terminar la validación, en la sección siguiente.
