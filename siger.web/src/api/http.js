export class ApiError extends Error {
  constructor(status, message, { uncertain = false } = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.uncertain = uncertain
  }
}

export function unconfirmedWrite(path, status = 0) {
  const message = path === '/reservations'
    ? 'No pudimos confirmar si se guardó tu reserva. Contacta al restaurante antes de enviarla de nuevo.'
    : 'No pudimos confirmar si se guardaron los cambios. Consulta el estado antes de volver a enviar.'
  return new ApiError(status, message, { uncertain: true })
}

export async function request(path, { method = 'GET', body, token, signal } = {}) {
  const base = import.meta.env.VITE_API_BASE_URL?.trim().replace(/\/+$/, '')
  if (!base) throw new ApiError(0, 'La conexión con el restaurante aún no está configurada.')

  const writing = method === 'POST' || method === 'PATCH'
  const timeout = AbortSignal.timeout(15_000)
  const requestSignal = signal ? AbortSignal.any([signal, timeout]) : timeout
  let response
  try {
    response = await fetch(`${base}/api/v1/public${path}`, {
      method,
      signal: requestSignal,
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
    if (signal?.aborted) throw error
    if (writing) throw unconfirmedWrite(path)
    throw new ApiError(0, 'No pudimos conectar con el restaurante. Inténtalo de nuevo.')
  }
  if (!response.ok) {
    if (writing && (response.status === 408 || response.status >= 500))
      throw unconfirmedWrite(path, response.status)
    const problem = response.status === 409 && method === 'PATCH' ? await response.json().catch(() => null) : null
    const editBlocked = ['Only future pending reservations can be edited.', 'A reservation with an operational order cannot be rescheduled.'].includes(problem?.detail)
    const messages = {
      400: 'Revisa los datos: la fecha debe ser futura, las cantidades válidas y los productos disponibles.',
      401: 'El acceso a esta reserva venció o ya no es válido. Contacta al restaurante para continuar.',
      404: path.endsWith('/preorder') ? 'No encontramos una preorden para esta reserva.' : 'No encontramos lo que solicitaste.',
      409: editBlocked ? 'Esta reserva ya no puede editarse. Contacta al restaurante.' : path.endsWith('/preorder')
        ? 'No se pudo guardar. Consulta el estado de la reserva antes de volver a enviar.'
        : 'No hay una mesa disponible para esa fecha y cantidad de personas. Prueba otro horario.',
      429: 'Has realizado varias solicitudes en poco tiempo. Espera unos minutos antes de volver a intentar.',
    }
    throw new ApiError(response.status, messages[response.status] || 'El servicio no está disponible por el momento. Inténtalo más tarde.')
  }
  try {
    return await response.json()
  } catch (error) {
    if (signal?.aborted) throw error
    if (writing) throw unconfirmedWrite(path)
    throw new ApiError(0, 'No pudimos leer la respuesta del restaurante. Inténtalo de nuevo.')
  }
}
