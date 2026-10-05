# SIGER — Entregable 3: Avance funcional

**Sistema Integral de Gestión de Restaurante**  
**Restaurante:** Superior Kitchen Essentials  
**Estudiante:** Stanley Camacho Abreu  
**Matrícula:** 2025-2271

SIGER es un sistema para apoyar la gestión de un restaurante mediante una aplicación Web para clientes, una aplicación Desktop para el personal y una API central conectada a PostgreSQL en Supabase.

Este repositorio corresponde al **Entregable 3 — Avance funcional**. En esta etapa se implementó y validó una base funcional conectada a la base de datos real, con énfasis en los módulos de **Reservaciones** y **Preórdenes**.

---

## Estado del Entregable 3

- Repositorio Git con commits organizados.
- Arquitectura en capas implementada.
- SIGER.API operativa y conectada a Supabase/PostgreSQL.
- SIGER.Web funcional en React.
- Catálogo público de categorías y productos.
- Reservaciones de visitantes sin necesidad de cuenta.
- Edición de reservas pendientes mediante credencial segura.
- Preórdenes asociadas a una reservación.
- Validaciones de disponibilidad, capacidad y solapamiento de mesas.
- Pruebas automatizadas de backend y frontend.
- Build Release del backend sin errores ni advertencias en la validación del entregable.

---

## Módulos funcionales de la entrega

### 1. Reservaciones

El cliente puede realizar una reservación desde SIGER.Web sin iniciar sesión.

El flujo permite:

- Registrar nombre y teléfono.
- Registrar correo opcional.
- Seleccionar fecha y horario.
- Seleccionar cantidad de personas.
- Agregar observaciones.
- Asignar automáticamente una mesa compatible.
- Registrar la reserva inicialmente como `Pending` / `Pendiente`.
- Editar una reserva pendiente usando la credencial temporal de la propia reserva.
- Revalidar disponibilidad cuando cambian fecha, horario o cantidad de personas.

Las reservas utilizan intervalos de dos horas y el backend vuelve a validar la disponibilidad al momento de guardar para evitar conflictos por concurrencia.

### 2. Preórdenes

Después de crear una reservación, el visitante puede crear opcionalmente una preorden.

La preorden:

- Reutiliza `Order` y `OrderDetail`; no existe una tabla separada de PreOrder.
- Se relaciona con una reservación.
- Permite seleccionar productos y cantidades.
- Permite agregar notas por producto.
- Calcula precios, subtotales y total en el backend.
- Se registra con origen Web y estado `PreOrdered` / `Preordenada`.
- No ocupa la mesa.
- No se envía a cocina en esta etapa.
- No genera pagos ni solicita cuenta.

---

## Arquitectura

SIGER utiliza **Layered Architecture** con un backend monolítico modular.

```text
SIGER.Web        SIGER.Desktop
     \              /
          SIGER.API
              |
      SIGER.Application
              |
         SIGER.Domain
              |
     SIGER.Infrastructure
              |
     Supabase / PostgreSQL
