import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import ReservationForm from '../components/ReservationForm'
import { reservationDateRange, reservationDateTime, validateReservationSchedule } from '../config/reservationSlots'
import { formatDate } from '../utils/format'
import { availableSlots } from './availabilityFixture'

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(new Date('2026-10-03T23:30:00Z'))
  vi.stubEnv('VITE_API_BASE_URL', 'https://api.example.test')
  vi.stubGlobal('fetch', vi.fn(async () => Response.json(availableSlots())))
})
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); vi.unstubAllEnvs() })

describe('Fecha y horarios del restaurante', () => {
  it.each([
    ['2026-10-02', /viernes, 2 de octubre de 2026/i, 'No puedes reservar en una fecha pasada.'],
    ['2026-11-03', /martes, 3 de noviembre de 2026/i, 'Selecciona una fecha dentro de los próximos 30 días.'],
  ])('rechaza %s antes de llamar a la API', async (date, label, message) => {
    render(<ReservationForm onCreated={vi.fn()} />)
    expect(validateReservationSchedule(date, '12:00')).toBe(message)
    await userEvent.click(screen.getByLabelText('Fecha'))
    if (date.startsWith('2026-11')) await userEvent.click(screen.getByRole('button', { name: 'Ir al mes siguiente' }))
    const day = screen.getByRole('button', { name: label })
    expect(day).toBeDisabled()
    await userEvent.click(day)
    await userEvent.click(screen.getByRole('button', { name: 'Cerrar calendario' }))
    await userEvent.click(screen.getByRole('button', { name: 'Solicitar reserva' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Selecciona una fecha.')
    expect(fetch.mock.calls.filter(([, options]) => options.method === 'POST')).toHaveLength(0)
  })

  it('permite un único slot y deshabilita horarios pasados en Santo Domingo', async () => {
    render(<ReservationForm onCreated={vi.fn()} />)
    expect(within(screen.getByRole('group', { name: 'Horario' })).getAllByRole('radio')).toHaveLength(13)
    await userEvent.click(screen.getByLabelText('Fecha'))
    await userEvent.click(screen.getByRole('button', { name: /domingo, 4 de octubre de 2026/i }))
    const lunch = screen.getByRole('radio', { name: '12:00 PM', exact: true })
    const dinner = screen.getByRole('radio', { name: '7:30 PM', exact: true })
    await userEvent.click(lunch)
    await userEvent.click(dinner)
    expect(dinner).toBeChecked()
    expect(lunch).not.toBeChecked()
    await userEvent.click(screen.getByLabelText('Fecha'))
    await userEvent.click(screen.getByRole('button', { name: /sábado, 3 de octubre de 2026/i }))
    expect(dinner).not.toBeChecked()
    expect(dinner).toBeDisabled()
    expect(screen.getByRole('radio', { name: '8:00 PM', exact: true })).toBeEnabled()
  })

  it('usa el día dominicano incluso cuando UTC ya cambió de día y año', () => {
    const now = new Date('2027-01-01T02:00:00Z')
    expect(reservationDateRange(now)).toEqual({ min: '2026-12-31', max: '2027-01-30' })
    expect(reservationDateTime('2027-01-01', '19:30')).toBe('2027-01-01T19:30:00-04:00')
    expect(new Date(reservationDateTime('2027-01-01', '19:30')).toISOString()).toBe('2027-01-01T23:30:00.000Z')
    expect(formatDate('2027-01-01T02:00:00Z')).toContain('31 de diciembre de 2026')
  })

  it('acepta el día 30 y rechaza horas fuera de la configuración', () => {
    expect(validateReservationSchedule('2026-11-02', '21:30')).toBe('')
    expect(validateReservationSchedule('2026-10-04', '19:13')).toBe('Selecciona uno de los horarios disponibles.')
    expect(validateReservationSchedule('2026-10-03', '19:00')).toBe('Ese horario ya pasó. Selecciona uno futuro.')
  })

})
