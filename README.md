# SIGER - Entregable 3: Avance funcional

## Sistema Integral de Gestión de Restaurante

**Restaurante:** Superior Kitchen Essentials  
**Autor:** Stanley Camacho Abreu  

---

## 1. Repositorio

Repositorio del proyecto:

https://github.com/StanleyCM/SIGER

El repositorio contiene los commits correspondientes al desarrollo del backend, reservaciones, preórdenes y la aplicación Web.

---

## 2. Estructura del proyecto

SIGER está organizado utilizando una arquitectura en capas.

```text
SIGER/
│
├── SIGER.Domain
├── SIGER.Application
├── SIGER.Infrastructure
├── SIGER.API
├── SIGER.Desktop
├── siger.web
├── SIGER.Tests
├── docs
└── scripts
```

### Capas principales

- **SIGER.Domain:** contiene las entidades, enumeraciones y reglas principales del dominio.
- **SIGER.Application:** contiene servicios, DTOs, interfaces y casos de uso.
- **SIGER.Infrastructure:** contiene la persistencia de datos, repositorios y conexión con PostgreSQL/Supabase.
- **SIGER.API:** API central encargada de comunicar las aplicaciones con la lógica del sistema.
- **SIGER.Web:** aplicación Web desarrollada para los clientes del restaurante.
- **SIGER.Desktop:** aplicación de escritorio destinada al personal del restaurante.
- **SIGER.Tests:** contiene las pruebas automatizadas del proyecto.

La comunicación principal del sistema sigue la siguiente estructura:

```text
SIGER.Web / SIGER.Desktop
           |
           v
       SIGER.API
           |
           v
 SIGER.Application
           |
           v
    SIGER.Domain
           |
           v
SIGER.Infrastructure
           |
           v
Supabase / PostgreSQL
```

---

## 3. Funcionalidades implementadas

Para este Entregable 3 se implementaron principalmente dos módulos funcionales:

### Módulo de Reservaciones

El módulo de reservaciones permite que un cliente realice una solicitud de reserva desde SIGER.Web.

Actualmente permite:

- Registrar el nombre del cliente.
- Registrar número de teléfono.
- Registrar correo electrónico opcional.
- Seleccionar la cantidad de personas.
- Seleccionar una fecha.
- Seleccionar un horario.
- Agregar observaciones.
- Validar la disponibilidad de mesas.
- Asignar automáticamente una mesa compatible.
- Registrar la reserva con estado Pendiente.
- Editar una reserva pendiente.
- Revalidar la disponibilidad al cambiar la fecha, hora o cantidad de personas.

Las reservaciones realizadas desde la Web son almacenadas mediante SIGER.API en PostgreSQL/Supabase.

### Módulo de Preórdenes

Después de realizar una reservación, el cliente puede realizar opcionalmente una preorden.

Actualmente permite:

- Consultar los productos disponibles del menú.
- Visualizar los productos organizados por categoría.
- Filtrar productos por categoría.
- Agregar productos a la preorden.
- Seleccionar cantidades.
- Aumentar o disminuir cantidades.
- Agregar observaciones a los productos.
- Calcular el subtotal y total.
- Asociar la preorden con una reservación existente.

La preorden queda registrada en el sistema sin ser enviada directamente a cocina ni generar pagos en esta etapa.

---

## 4. Conexión con la base de datos

SIGER utiliza **PostgreSQL administrado mediante Supabase**.

La conexión con la base de datos se realiza a través de SIGER.API y la capa de infraestructura.

```text
SIGER.Web
     |
     v
SIGER.API
     |
     v
SIGER.Application
     |
     v
SIGER.Infrastructure
     |
     v
Supabase / PostgreSQL
```

La conexión se encuentra operativa y permite registrar y consultar información real del sistema.

Entre los datos utilizados actualmente se encuentran:

- Mesas.
- Categorías.
- Productos.
- Reservaciones.
- Órdenes.
- Detalles de órdenes.

---

## 5. Instalación y ejecución

### Requisitos

Para ejecutar el proyecto se necesita:

- .NET 10 SDK.
- Node.js.
- npm.
- Acceso a PostgreSQL/Supabase.
- Credenciales de configuración del backend.

### Configuración del Backend

Las credenciales del backend deben configurarse mediante User Secrets.

Desde la raíz del proyecto:

```powershell
dotnet user-secrets set "ConnectionStrings:SIGERDatabase" "TU_CONNECTION_STRING" --project SIGER.API
dotnet user-secrets set "Supabase:Url" "TU_SUPABASE_URL" --project SIGER.API
dotnet user-secrets set "Supabase:ServiceRoleKey" "TU_SERVICE_ROLE_KEY" --project SIGER.API
```

No se deben guardar credenciales reales dentro del repositorio.

### Ejecutar el Backend

Desde la raíz del proyecto:

```powershell
dotnet restore
dotnet run --project SIGER.API --launch-profile http
```

La API estará disponible en:

```text
http://localhost:5179
```

Swagger estará disponible en:

```text
http://localhost:5179/swagger/index.html
```

### Configuración de SIGER.Web

Entrar a la carpeta del proyecto Web:

```powershell
cd siger.web
```

Instalar las dependencias:

```powershell
npm install
```

Crear un archivo llamado:

```text
.env.local
```

Agregar la siguiente configuración:

```env
VITE_API_BASE_URL=http://localhost:5179
```

### Ejecutar SIGER.Web

Dentro de la carpeta `siger.web` ejecutar:

```powershell
npm run dev
```

Vite mostrará en la consola la dirección local donde se encuentra ejecutándose la aplicación Web.

---

## Pruebas

### Backend

Para ejecutar las pruebas del backend:

```powershell
dotnet test -c Release
```

El proyecto cuenta con pruebas automatizadas para validar las funcionalidades principales y las diferentes capas del sistema.

### Frontend

Dentro de `siger.web`:

```powershell
npm run test
```

También pueden ejecutarse las siguientes verificaciones:

```powershell
npm run build
npm run lint
```

---

## Estado actual del proyecto

Para este Entregable 3 se encuentran funcionando:

- Repositorio GitHub con commits.
- Proyecto organizado por capas y módulos.
- SIGER.API.
- Conexión con Supabase/PostgreSQL.
- Catálogo de productos.
- Categorías.
- SIGER.Web.
- Módulo de Reservaciones.
- Edición de reservaciones.
- Módulo de Preórdenes.
- Pruebas automatizadas.

El proyecto continúa en desarrollo, por lo que en futuras etapas se completarán las demás funcionalidades e interfaces del sistema.

---

## Cumplimiento del Entregable 3

| Requisito | Estado |
|---|---|
| Repositorio en GitHub/GitLab con commits | Cumplido |
| Estructura del proyecto organizada | Cumplido |
| Mínimo 2 módulos funcionando | Cumplido: Reservaciones y Preórdenes |
| Conexión con base de datos operativa | Cumplido: PostgreSQL/Supabase |
| README con instrucciones de instalación y ejecución | Cumplido |

---

## Repositorio

https://github.com/StanleyCM/SIGER
