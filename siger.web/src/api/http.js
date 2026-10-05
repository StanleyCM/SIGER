export class ApiError extends Error {
  constructor(status, message) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

export async function request(path, { method = 'GET', body, token, signal } = {}) {
  const base = import.meta.env.VITE_API_BASE_URL?.trim().replace(/\/+$/, '')
  if (!base) throw new ApiError(0, 'La conexión con el restaurante aún no está configurada.')

  let response
  try {
    response = await fetch(`${base}/api/v1/public${path}`, {
      method,
      signal,
      credentials: 'omit',
      cache: 'no-store',
      headers: {
        Accept: 'application/json',
        ...(body ? { 'Content-Type': 'application/json' } : {}),
        ...(token ? { 'X-Reservation-Token': token } : {}),
      },
      ...(body ? { body: JSON.stringify(body) } : {}),
    })
  } catch (error) {
    if (error.name === 'AbortError') throw error
    throw new ApiError(0, ['POST', 'PATCH'].includes(method)
      ? 'Se perdió la conexión. No pudimos comprobar si se guardó la solicitud. Evita enviarla de nuevo hasta verificarlo con el restaurante.'
      : 'No pudimos conectar con el restaurante. Revisa tu conexión e inténtalo de nuevo.')
  }
  if (!response.ok) {
    const problem = response.status === 409 && method === 'PATCH' ? await response.json().catch(() => null) : null
    const editBlocked = ['Only future pending reservations can be edited.', 'A reservation with an operational order cannot be rescheduled.'].includes(problem?.detail)
    const messages = {
      400: 'Revisa los datos: la fecha debe ser futura, las cantidades válidas y los productos disponibles.',
      401: 'El acceso a esta reserva venció o ya no es válido. Contacta al restaurante para continuar.',
      404: 'No encontramos la reserva o el producto solicitado.',
      409: editBlocked ? 'Esta reserva ya no puede editarse. Contacta al restaurante.' : path.endsWith('/preorder')
        ? 'No se pudo guardar: la reserva ya tiene una preorden o su disponibilidad cambió.'
        : 'No hay una mesa disponible para esa fecha y cantidad de personas. Prueba otro horario.',
      429: 'Has realizado varias solicitudes en poco tiempo. Espera unos minutos antes de volver a intentar.',
    }
    throw new ApiError(response.status, messages[response.status] || 'El servicio no está disponible por el momento. Inténtalo más tarde.')
  }
  try {
    return await response.json()
  } catch {
    throw new ApiError(0, ['POST', 'PATCH'].includes(method)
      ? 'No pudimos comprobar la respuesta de tu solicitud. Contacta al restaurante antes de enviarla de nuevo.'
      : 'No pudimos leer la respuesta del restaurante. Inténtalo más tarde.')
  }
}
