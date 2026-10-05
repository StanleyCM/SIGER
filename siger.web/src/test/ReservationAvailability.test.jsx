import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import ReservationForm from '../components/ReservationForm'
import { availableSlots } from './availabilityFixture'

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(new Date('2026-10-05T17:00:00Z'))
  vi.stubEnv('VITE_API_BASE_URL', 'https://api.example.test')
  vi.stubGlobal('fetch', vi.fn(async () => Response.json(availableSlots())))
})
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); vi.unstubAllEnvs() })

async function chooseDate(user, day = 6) {
  await user.click(screen.getByLabelText('Fecha'))
  await user.click(screen.getByRole('button', { name: new RegExp(`, ${day} de octubre de 2026`, 'i') }))
}
const lunch = () => screen.getByRole('radio', { name: '12:00 PM', exact: true })

describe('Disponibilidad real de horarios', () => {
  it('habilita solo los libres y deshabilita llenos y pasados sin permitir seleccionarlos', async () => {
    fetch.mockImplementation(async () => Response.json(availableSlots().map(s => ({ ...s, available: s.time !== '18:00' }))))
    const user = userEvent.setup(); render(<ReservationForm onCreated={vi.fn()} />)
    await chooseDate(user, 5)
    await waitFor(() => expect(screen.getByRole('radio', { name: '2:00 PM', exact: true })).toBeEnabled())
    expect(lunch()).toBeDisabled()
    const full = screen.getByRole('radio', { name: '6:00 PM', exact: true })
    expect(full).toBeDisabled()
    expect(full.closest('label')).toHaveClass('slot-choice')
    await user.click(full); expect(full).not.toBeChecked()
    expect(screen.getByText('Elige un horario. Gris = no disponible o pasado.')).toBeInTheDocument()
  })

  it('refresca por personas y fecha y limpia la selección cuando deja de estar libre', async () => {
    fetch.mockImplementation(async (url) => {
      const query = new URL(url).searchParams
      return Response.json(availableSlots().map(s => ({ ...s, available: query.get('numberOfPeople') === '2' || query.get('date') === '2026-10-07' })))
    })
    const user = userEvent.setup(); render(<ReservationForm onCreated={vi.fn()} />)
    await chooseDate(user)
    await waitFor(() => expect(lunch()).toBeEnabled()); await user.click(lunch())
    expect(lunch()).toBeChecked()
    await user.click(screen.getByRole('button', { name: 'Aumentar cantidad de personas' }))
    await waitFor(() => expect(lunch()).not.toBeChecked())
    expect(lunch()).toBeDisabled()
    expect(fetch.mock.calls.some(([url]) => url.includes('date=2026-10-06&numberOfPeople=3'))).toBe(true)
    await chooseDate(user, 7)
    await waitFor(() => expect(lunch()).toBeEnabled())
    expect(lunch()).not.toBeChecked()
    expect(fetch.mock.calls.some(([url]) => url.includes('date=2026-10-07&numberOfPeople=3'))).toBe(true)
  })

  it('conserva una selección si continúa disponible tras cambiar personas', async () => {
    const user = userEvent.setup(); render(<ReservationForm onCreated={vi.fn()} />)
    await chooseDate(user); await waitFor(() => expect(lunch()).toBeEnabled()); await user.click(lunch())
    await user.click(screen.getByRole('button', { name: 'Aumentar cantidad de personas' }))
    await waitFor(() => expect(lunch()).toBeEnabled()); expect(lunch()).toBeChecked()
  })

  it('bloquea durante la carga, muestra fallo discreto y permite reintentar sin asumir disponibilidad', async () => {
    let respond
    fetch.mockImplementationOnce(() => new Promise(resolve => { respond = resolve }))
    const user = userEvent.setup(); render(<ReservationForm onCreated={vi.fn()} />)
    await chooseDate(user)
    expect(screen.getByText('Consultando disponibilidad…')).toBeInTheDocument()
    expect(lunch()).toBeDisabled(); expect(screen.getByRole('button', { name: 'Solicitar reserva' })).toBeDisabled()
    respond(Response.json({}, { status: 500 }))
    const retry = await screen.findByRole('button', { name: 'Reintentar disponibilidad' })
    expect(lunch()).toBeDisabled(); expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    await user.click(retry); await waitFor(() => expect(lunch()).toBeEnabled())
    expect(screen.queryByRole('button', { name: 'Reintentar disponibilidad' })).not.toBeInTheDocument()
  })

  it('ignora una respuesta antigua después de cambiar personas', async () => {
    let respond
    fetch.mockImplementationOnce(() => new Promise(resolve => { respond = resolve }))
      .mockImplementationOnce(async () => Response.json(availableSlots().map(s => ({ ...s, available: false }))))
    const user = userEvent.setup(); render(<ReservationForm onCreated={vi.fn()} />)
    await chooseDate(user)
    await user.click(screen.getByRole('button', { name: 'Aumentar cantidad de personas' }))
    await screen.findByText('Elige un horario. Gris = no disponible o pasado.')
    expect(fetch.mock.calls[0][1].signal.aborted).toBe(true)
    respond(Response.json(availableSlots()))
    await waitFor(() => expect(lunch()).toBeDisabled())
  })

  it('envía la credencial solo al consultar disponibilidad de su propia reserva en edición', async () => {
    render(<ReservationForm onCreated={vi.fn()} availabilityToken="own-test-token" initialReservation={{ id: 9, name: 'Ana', phone: '8095550100', numberOfPeople: 2, reservationDateTime: '2026-10-06T16:00:00Z' }} />)
    await waitFor(() => expect(lunch()).toBeEnabled())
    expect(lunch()).toBeChecked()
    expect(fetch.mock.calls[0][0]).toContain('reservationId=9')
    expect(fetch.mock.calls[0][1].headers['X-Reservation-Token']).toBe('own-test-token')
  })
})
