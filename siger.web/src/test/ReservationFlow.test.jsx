import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from '../App'
import { availableSlots } from './availabilityFixture'

const token = 'test-only-credential-not-for-a-real-reservation'
let requests
let savedReservations
let savedPreorders
const products = [{ id: 1, categoryId: 1, name: 'Ravioles', description: 'Pasta fresca', price: 10 }]

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(new Date('2026-10-03T23:30:00Z'))
  requests = []
  savedReservations = []
  savedPreorders = []
  vi.stubEnv('VITE_API_BASE_URL', 'https://api.example.test')
  vi.stubGlobal('fetch', vi.fn(async (url, options) => {
    requests.push({ url, ...options })
    if (url.includes('/reservations/availability?')) return Response.json(availableSlots())
    if (url.endsWith('/categories')) return Response.json([{ id: 1, name: 'Pastas' }])
    if (url.includes('/products?')) return Response.json({ items: products, totalCount: 1, totalPages: 1, pageNumber: 1, pageSize: 12 })
    if (url.endsWith('/reservations')) {
      const body = JSON.parse(options.body)
      const savedReservation = { ...body, id: 10 + savedReservations.length, status: 'Pending', accessExpiresAt: new Date(new Date(body.reservationDateTime).getTime() + 7_200_000).toISOString() }
      savedReservations.push(savedReservation)
      return Response.json({ reservation: savedReservation, message: 'Reserva recibida: pendiente', accessToken: savedReservation.id === 10 ? token : `${token}-${savedReservation.id}`,
        accessTokenExpiresAt: new Date(new Date(body.reservationDateTime).getTime() + 7_200_000).toISOString() }, { status: 201 })
    }
    if (options.method === 'PATCH') {
      const id = Number(url.split('/').at(-1))
      const expectedToken = id === 10 ? token : `${token}-${id}`
      if (options.headers['X-Reservation-Token'] !== expectedToken) return Response.json({}, { status: 401 })
      const saved = savedReservations.find(r => r.id === id)
      Object.assign(saved, JSON.parse(options.body))
      saved.accessExpiresAt = new Date(new Date(saved.reservationDateTime).getTime() + 7_200_000).toISOString()
      return Response.json(saved)
    }
    const preorderMatch = url.match(/\/reservations\/(\d+)\/preorder$/)
    if (preorderMatch) {
      const reservationId = Number(preorderMatch[1])
      const expectedToken = reservationId === 10 ? token : `${token}-${reservationId}`
      if (options.headers['X-Reservation-Token'] !== expectedToken) return Response.json({}, { status: 401 })
      // Server price intentionally differs from the catalog: the confirmation must use this response.
      const preorder = { id: reservationId + 10, reservationId, status: 'PreOrdered', total: 23,
        items: [{ productId: 1, productName: 'Ravioles', quantity: 2, unitPrice: 11.5, subtotal: 23, notes: 'Sin sal' }] }
      savedPreorders.push(preorder)
      return Response.json(preorder, { status: 201 })
    }
    throw new Error('Unexpected test endpoint')
  }))
})

afterEach(() => { vi.unstubAllGlobals(); vi.unstubAllEnvs(); vi.useRealTimers() })

async function reserve(user) {
  await user.click(screen.getByRole('button', { name: 'RESERVA', exact: true }))
  await user.type(screen.getByLabelText('Nombre'), 'Ana Pérez')
  await user.type(screen.getByLabelText('Teléfono'), '+58 412 1234567')
  await user.click(screen.getByLabelText('Fecha'))
  await user.click(screen.getByRole('button', { name: /domingo, 4 de octubre de 2026/i }))
  await user.click(screen.getByRole('radio', { name: '7:00 PM', exact: true }))
  await user.click(screen.getByRole('button', { name: 'Solicitar reserva' }))
  await screen.findByRole('heading', { name: 'Reserva recibida' })
}

describe('Base funcional de reservaciones y preórdenes', () => {

  it('crea la reserva pendiente, admite correo omitido y oculta la credencial', async () => {
    const user = userEvent.setup()
    render(<App />)
    await reserve(user)
    expect(screen.getByRole('heading', { name: 'Reserva recibida' })).toBeInTheDocument()
    const post = requests.find((r) => r.method === 'POST')
    expect(JSON.parse(post.body)).toEqual({ name: 'Ana Pérez', phone: '584121234567', email: null,
      reservationDateTime: '2026-10-04T19:00:00-04:00', numberOfPeople: 2, notes: null })
    expect(screen.getByText('Reserva #10')).toBeInTheDocument()
    expect(document.body.textContent).not.toContain(token)
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
    expect(screen.queryByRole('button', { name: 'Finalizar', exact: true })).not.toBeInTheDocument()
    expect(screen.getByText('Pendiente')).toHaveClass('status-badge')
    expect(screen.getByText('7:00 PM')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Volver al inicio ↑' }))
    expect(requests.filter((r) => r.method === 'POST')).toHaveLength(1)
  })

  it('edita la selección y crea la preorden sin precios, mostrando el total del servidor', async () => {
    const user = userEvent.setup()
    render(<App />)
    await reserve(user)
    await user.click(screen.getByRole('button', { name: 'Hacer preorden' }))
    await user.click(await screen.findByRole('button', { name: 'Agregar Ravioles' }))
    await user.click(screen.getByRole('button', { name: 'Eliminar Ravioles' }))
    expect(screen.getByRole('button', { name: 'Guardar preorden' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Agregar Ravioles' }))
    await user.click(screen.getByRole('button', { name: 'Aumentar Ravioles' }))
    await user.click(screen.getByRole('button', { name: 'Disminuir Ravioles' }))
    await user.click(screen.getByRole('button', { name: 'Aumentar Ravioles' }))
    await user.type(screen.getByLabelText('Nota para Ravioles (opcional)'), 'Sin sal')
    await user.click(screen.getByRole('button', { name: 'Guardar preorden' }))
    await screen.findByRole('heading', { name: 'Preorden registrada' })
    const post = requests.find((r) => r.url.endsWith('/preorder'))
    expect(post.headers['X-Reservation-Token']).toBe(token)
    expect(post.url).not.toContain(token)
    expect(JSON.parse(post.body)).toEqual({ items: [{ productId: 1, quantity: 2, notes: 'Sin sal' }] })
    await waitFor(() => expect(screen.getByText('Total de tu preorden')).toHaveTextContent('23,00'))
    expect(screen.getByText('La reservación permanece pendiente de confirmación.')).toBeInTheDocument()
    expect(document.body.textContent).not.toContain(token)
  })

  it('descarta el borrador de preorden y la credencial anterior al crear otra reserva', async () => {
    const user = userEvent.setup()
    render(<App />)
    await reserve(user)
    await user.click(screen.getByRole('button', { name: 'Hacer preorden' }))
    await user.click(await screen.findByRole('button', { name: 'Agregar Ravioles' }))
    await user.type(screen.getByLabelText('Nota para Ravioles (opcional)'), 'Borrador anterior')
    await user.click(screen.getByRole('button', { name: 'MENU', exact: true }))
    await user.click(screen.getByRole('button', { name: 'RESERVA', exact: true }))
    expect(screen.queryByLabelText('Nota para Ravioles (opcional)')).not.toBeInTheDocument()
    await reserve(user)
    const secondPost = requests.filter(r => r.url.endsWith('/reservations') && r.method === 'POST')[1]
    expect(secondPost.headers['X-Reservation-Token']).toBeUndefined()
    await user.click(screen.getByRole('button', { name: 'Hacer preorden' }))
    expect(screen.getByText('Aún no has agregado productos.')).toBeInTheDocument()
    expect(screen.queryByLabelText('Nota para Ravioles (opcional)')).not.toBeInTheDocument()
    await user.click(await screen.findByRole('button', { name: 'Agregar Ravioles' }))
    expect(screen.getByLabelText('Nota para Ravioles (opcional)')).toHaveValue('')
    await user.click(screen.getByRole('button', { name: 'Guardar preorden' }))
    await screen.findByRole('heading', { name: 'Preorden registrada' })
    const post = requests.find(r => r.url.endsWith('/reservations/11/preorder'))
    expect(post.headers['X-Reservation-Token']).toBe(`${token}-11`)
    expect(post.headers['X-Reservation-Token']).not.toBe(token)
    expect(savedPreorders.map(p => p.reservationId)).toEqual([11])
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
    expect(document.body.textContent).not.toContain(token)
  })

  it('precarga el formulario de edición y cancelar no toca el servidor', async () => {
    const user = userEvent.setup()
    render(<App />)
    await reserve(user)
    const before = JSON.stringify(savedReservations)
    await user.click(screen.getByRole('button', { name: 'Editar reserva' }))
    expect(screen.getByRole('heading', { name: 'Editar tu reserva' })).toBeInTheDocument()
    expect(screen.getByLabelText('Nombre')).toHaveValue('Ana Pérez')
    expect(screen.getByLabelText('Teléfono')).toHaveValue('584121234567')
    expect(screen.getByLabelText('Correo (opcional)')).toHaveValue('')
    expect(screen.getByLabelText('Fecha')).toHaveTextContent('4 de octubre de 2026')
    expect(screen.getByRole('radio', { name: '7:00 PM', exact: true })).toBeChecked()
    expect(screen.getByRole('group', { name: 'Cantidad de personas' })).toHaveTextContent('2 personas')
    await user.clear(screen.getByLabelText('Nombre'))
    await user.type(screen.getByLabelText('Nombre'), 'Descartar')
    await user.click(screen.getByRole('button', { name: 'Cancelar edición' }))
    expect(screen.getByRole('heading', { name: 'Reserva recibida' })).toBeInTheDocument()
    expect(JSON.stringify(savedReservations)).toBe(before)
    expect(requests.some(r => r.method === 'PATCH')).toBe(false)
  })

  it('guarda edición con el mismo token y permite editar después de crear preorden', async () => {
    const user = userEvent.setup()
    render(<App />)
    await reserve(user)
    await user.click(screen.getByRole('button', { name: 'Editar reserva' }))
    await user.clear(screen.getByLabelText('Nombre'))
    await user.type(screen.getByLabelText('Nombre'), 'Ana editada')
    await user.type(screen.getByLabelText('Correo (opcional)'), 'ana@empresa.com')
    await user.type(screen.getByLabelText('Observaciones (opcional)'), 'Ventana')
    await user.click(screen.getByRole('button', { name: 'Aumentar cantidad de personas' }))
    await user.click(screen.getByRole('radio', { name: '8:00 PM', exact: true }))
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    await screen.findByRole('heading', { name: 'Reserva actualizada' })
    const patch = requests.find(r => r.method === 'PATCH')
    expect(patch.headers['X-Reservation-Token']).toBe(token)
    expect(JSON.parse(patch.body)).toEqual({ name: 'Ana editada', phone: '584121234567', email: 'ana@empresa.com',
      reservationDateTime: '2026-10-04T20:00:00-04:00', numberOfPeople: 3, notes: 'Ventana' })
    expect(screen.getByText('Pendiente')).toBeInTheDocument()
    expect(screen.getByText('Ventana')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Hacer preorden' }))
    await user.click(await screen.findByRole('button', { name: 'Agregar Ravioles' }))
    await user.click(screen.getByRole('button', { name: 'Guardar preorden' }))
    await screen.findByRole('heading', { name: 'Preorden registrada' })
    await user.click(screen.getByRole('button', { name: 'Editar reserva' }))
    expect(screen.getByLabelText('Nombre')).toHaveValue('Ana editada')
    expect(screen.getByLabelText('Correo (opcional)')).toHaveValue('ana@empresa.com')
    await user.click(screen.getByRole('button', { name: 'Aumentar cantidad de personas' }))
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    await screen.findByRole('heading', { name: 'Reserva actualizada' })
    expect(requests.filter(r => r.method === 'PATCH').every(r => r.headers['X-Reservation-Token'] === token)).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Ver preorden' }))
    expect(screen.getByRole('heading', { name: 'Preorden registrada' })).toBeVisible()
    expect(savedPreorders).toHaveLength(1)
    expect(savedReservations[0].numberOfPeople).toBe(4)
  })

  it('mantiene lo escrito ante falta de disponibilidad y no altera la reserva guardada', async () => {
    const user = userEvent.setup()
    const originalFetch = fetch.getMockImplementation()
    fetch.mockImplementation((url, options) => options.method === 'PATCH'
      ? Response.json({ detail: 'No table is available for this reservation.' }, { status: 409 }) : originalFetch(url, options))
    render(<App />)
    await reserve(user)
    const before = JSON.stringify(savedReservations)
    await user.click(screen.getByRole('button', { name: 'Editar reserva' }))
    await user.type(screen.getByLabelText('Observaciones (opcional)'), 'Conservar borrador')
    await user.click(screen.getByRole('button', { name: 'Aumentar cantidad de personas' }))
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('No hay una mesa disponible para esa fecha y cantidad de personas. Prueba otro horario.')
    expect(screen.getByLabelText('Observaciones (opcional)')).toHaveValue('Conservar borrador')
    expect(screen.getByText('3 personas')).toBeInTheDocument()
    expect(JSON.stringify(savedReservations)).toBe(before)
  })

})
