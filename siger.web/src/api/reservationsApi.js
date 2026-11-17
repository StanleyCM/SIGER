import { ApiError, request, unconfirmedWrite } from './http'

function requireResult(value, valid, writePath) {
  if (valid) return value
  if (writePath) throw unconfirmedWrite(writePath)
  throw new ApiError(0, 'No pudimos leer la respuesta del restaurante. Inténtalo de nuevo.')
}

function isReservation(value, id = value?.id) {
  return Number.isSafeInteger(id) && id > 0 && value?.id === id && Number.isFinite(Date.parse(value.reservationDateTime))
    && ['Pending', 'Confirmed', 'Cancelled', 'Completed'].includes(value.status) && Number.isInteger(value.numberOfPeople)
}

function isPreOrder(value, id) {
  return value?.reservationId === id && Array.isArray(value.items) && Number.isFinite(value.total)
}

export async function createReservation(body) {
  const result = await request('/reservations', { method: 'POST', body })
  return requireResult(result, isReservation(result?.reservation) && typeof result.accessToken === 'string'
    && result.accessToken.length > 0 && Number.isFinite(Date.parse(result.accessTokenExpiresAt)), '/reservations')
}

export async function updateReservation(id, token, body) {
  const path = `/reservations/${id}`
  const result = await request(path, { method: 'PATCH', token, body })
  return requireResult(result, isReservation(result, id), path)
}

export async function getReservation(id, token, signal) {
  const result = await request(`/reservations/${id}`, { token, signal })
  return requireResult(result, isReservation(result, id))
}

export async function getPreOrder(id, token, signal) {
  const result = await request(`/reservations/${id}/preorder`, { token, signal })
  return requireResult(result, isPreOrder(result, id))
}

export function getReservationAvailability(date, numberOfPeople, { reservationId, token, signal } = {}) {
  const query = new URLSearchParams({ date, numberOfPeople })
  if (reservationId) query.set('reservationId', reservationId)
  return request(`/reservations/availability?${query}`, { token, signal })
}

export async function createPreOrder(id, token, items) {
  const path = `/reservations/${id}/preorder`
  const result = await request(path, {
    method: 'POST', token,
    body: { items: items.map(({ productId, quantity, notes }) => ({ productId, quantity, notes: notes?.trim() || null })) },
  })
  return requireResult(result, isPreOrder(result, id), path)
}
