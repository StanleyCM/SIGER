import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import ReservationForm from '../components/ReservationForm'
import { availableSlots } from './availabilityFixture'

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(new Date('2026-10-04T14:00:00Z'))
  vi.stubEnv('VITE_API_BASE_URL', 'https://api.example.test')
  vi.stubGlobal('fetch', vi.fn(async (url) => url.includes('/availability?')
    ? Response.json(availableSlots()) : Response.json({ reservation: { id: 1 } }, { status: 201 })))
})
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); vi.unstubAllEnvs() })

async function completeSchedule(user) {
  await user.type(screen.getByLabelText('Nombre'), 'Ana Pérez')
  await user.click(screen.getByLabelText('Fecha'))
  await user.click(screen.getByRole('button', { name: /lunes, 5 de octubre de 2026/i }))
  await user.click(screen.getByRole('radio', { name: '12:00 PM', exact: true }))
}

describe('Contacto y cantidad de personas', () => {

  it('exige 10 dígitos, limita a 15 y bloquea el envío de un teléfono incompleto', async () => {
    const user = userEvent.setup()
    render(<ReservationForm onCreated={vi.fn()} />)
    await completeSchedule(user)
    const phone = screen.getByLabelText('Teléfono')
    await user.click(phone)
    await user.tab()
    expect(phone).toHaveAttribute('aria-invalid', 'true')
    await user.click(screen.getByRole('button', { name: 'Solicitar reserva' }))
    expect(fetch.mock.calls.filter(([, options]) => options.method === 'POST')).toHaveLength(0)
    await user.type(phone, '123456789')
    await user.click(screen.getByRole('button', { name: 'Solicitar reserva' }))
    expect(fetch.mock.calls.filter(([, options]) => options.method === 'POST')).toHaveLength(0)
    await user.type(phone, '0123456')
    expect(phone).toHaveValue('123456789012345')
    expect(phone).toHaveAttribute('aria-invalid', 'false')
  })

  it('permite correo vacío y actualiza el error en vivo para dominios generales', async () => {
    const user = userEvent.setup()
    render(<ReservationForm onCreated={vi.fn()} />)
    const email = screen.getByLabelText('Correo (opcional)')
    expect(email).not.toBeRequired()
    expect(screen.queryByText('Ingresa un correo electrónico válido.')).not.toBeInTheDocument()
    for (const value of ['usuario@dominio']) {
      await user.clear(email)
      await user.type(email, value)
      expect(email).toHaveAccessibleDescription('Ingresa un correo electrónico válido.')
      expect(email).toHaveAttribute('aria-invalid', 'true')
    }
    for (const value of ['persona@empresa.com', '']) {
      await user.clear(email)
      if (value) await user.type(email, value)
      expect(email).toHaveAttribute('aria-invalid', 'false')
      expect(screen.queryByText('Ingresa un correo electrónico válido.')).not.toBeInTheDocument()
    }
  })

  it('impide enviar un correo inválido aunque el resto del formulario esté completo', async () => {
    const user = userEvent.setup()
    render(<ReservationForm onCreated={vi.fn()} />)
    await completeSchedule(user)
    await user.type(screen.getByLabelText('Teléfono'), '8095551234')
    await user.type(screen.getByLabelText('Correo (opcional)'), 'usuario@dominio')
    await user.click(screen.getByRole('button', { name: 'Solicitar reserva' }))
    expect(fetch.mock.calls.filter(([, options]) => options.method === 'POST')).toHaveLength(0)
    expect(screen.getByLabelText('Correo (opcional)')).toHaveFocus()
  })

})
