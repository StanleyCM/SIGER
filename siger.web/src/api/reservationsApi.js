import { request } from './http'

export const createReservation = (body) => request('/reservations', { method: 'POST', body })
export const updateReservation = (id, token, body) => request(`/reservations/${id}`, { method: 'PATCH', token, body })

export function getReservationAvailability(date, numberOfPeople, { reservationId, token, signal } = {}) {
  const query = new URLSearchParams({ date, numberOfPeople })
  if (reservationId) query.set('reservationId', reservationId)
  return request(`/reservations/availability?${query}`, { token, signal })
}

export function createPreOrder(id, token, items) {
  return request(`/reservations/${id}/preorder`, {
    method: 'POST', token,
    body: { items: items.map(({ productId, quantity, notes }) => ({ productId, quantity, notes: notes?.trim() || null })) },
  })
}
