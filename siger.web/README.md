# Flujo público de reservas

El botón global RESERVA siempre inicia un formulario nuevo, incluso al pulsarlo desde la propia vista de reserva. HOME y MENU permiten salir del flujo, pero pulsar RESERVA después descarta la solicitud anterior de la interfaz. Los registros ya enviados permanecen en el servidor.

«Nueva reserva» limpia la reserva, la preorden, la credencial y los formularios de la sesión React, y vuelve al paso 01 con dos personas y sin fecha ni horario. No envía solicitudes para modificar o eliminar datos del servidor. Durante el guardado de una preorden se deshabilita ese botón local. El botón global RESERVA siempre inicia otro flujo; una solicitud ya enviada puede terminar en el servidor, pero su respuesta no se incorpora al nuevo formulario.

La credencial pública se mantiene únicamente en memoria; no se guarda en localStorage, sessionStorage ni en la URL. Se conserva tras finalizar o guardar la preorden para permitir editar la reserva actual. Se descarta al iniciar una nueva reserva. Recargar la página pierde el acceso desde esta interfaz a la solicitud anterior, aunque los registros permanecen en el servidor. Esta base funcional no ofrece historial ni recuperación de acceso.

«Editar reserva» reutiliza el formulario con contacto, fecha/hora de República Dominicana, personas y observaciones precargados. Cancelar edición no envía cambios. Guardar usa PATCH /api/v1/public/reservations/{id} con X-Reservation-Token. Solo se pueden editar reservas Pending futuras; la API vuelve a comprobar disponibilidad al cambiar fecha/hora/personas y sincroniza la mesa de una preorden existente dentro de la misma transacción. La credencial no rota y su expiración se ajusta al nuevo intervalo. Los errores conservan el borrador.

La API pública todavía no ofrece edición de productos de una preorden guardada ni cancelación pública. «Nueva reserva» no equivale a editar o cancelar.
