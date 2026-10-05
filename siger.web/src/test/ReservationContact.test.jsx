import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
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
  it('elimina letras y limpia texto pegado conservando ceros iniciales', async () => {
    const user = userEvent.setup()
    render(<ReservationForm onCreated={vi.fn()} />)
    const phone = screen.getByLabelText('Teléfono')
    expect(phone).toHaveAttribute('type', 'tel')
    expect(phone).toHaveAttribute('inputmode', 'numeric')
    await user.type(phone, 'abc')
    expect(phone).toHaveValue('')
    expect(phone).toHaveAccessibleDescription('Ingresa un número de teléfono válido.')
    await user.paste('809-555-1234')
    expect(phone).toHaveValue('8095551234')
    expect(screen.queryByText('Ingresa un número de teléfono válido.')).not.toBeInTheDocument()
    await user.clear(phone)
    await user.paste('00 (809) 555-1234 ext abc')
    expect(phone).toHaveValue('008095551234')
    expect(phone).toHaveAttribute('aria-invalid', 'false')
  })

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
    for (const value of ['asdadsadsa', 'usuario@', '@gmail.com', 'usuario@dominio', 'usuario dominio@gmail.com']) {
      await user.clear(email)
      await user.type(email, value)
      expect(email).toHaveAccessibleDescription('Ingresa un correo electrónico válido.')
      expect(email).toHaveAttribute('aria-invalid', 'true')
    }
    for (const value of ['stanley@gmail.com', 'usuario@hotmail.com', 'persona@yahoo.com', 'empleado@empresa.com', 'estudiante@itla.edu.do', '']) {
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

  it('inicia en 2, aumenta con teclado y disminuye sin salir del rango 1–12', async () => {
    const user = userEvent.setup()
    render(<ReservationForm onCreated={vi.fn()} />)
    const group = within(screen.getByRole('group', { name: 'Cantidad de personas' }))
    const count = group.getByRole('status')
    const decrease = group.getByRole('button', { name: 'Disminuir cantidad de personas' })
    const increase = group.getByRole('button', { name: 'Aumentar cantidad de personas' })
    expect(count).toHaveTextContent(/^2 personas$/)
    expect(group.queryByRole('radio')).not.toBeInTheDocument()
    expect(group.queryByRole('spinbutton')).not.toBeInTheDocument()
    expect(group.queryByRole('textbox')).not.toBeInTheDocument()
    await user.click(increase)
    expect(count).toHaveTextContent(/^3 personas$/)
    await user.keyboard('{Enter}')
    expect(count).toHaveTextContent(/^4 personas$/)
    await user.click(decrease)
    expect(count).toHaveTextContent(/^3 personas$/)
    await user.keyboard(' ')
    expect(count).toHaveTextContent(/^2 personas$/)
    await user.click(decrease)
    expect(count).toHaveTextContent(/^1 persona$/)
    expect(decrease).toBeDisabled()
    await user.click(decrease)
    expect(count).toHaveTextContent(/^1 persona$/)
    for (let i = 0; i < 11; i++) await user.click(increase)
    expect(count).toHaveTextContent(/^12 personas$/)
    expect(increase).toBeDisabled()
    await user.click(increase)
    expect(count).toHaveTextContent(/^12 personas$/)
    await user.click(decrease)
    expect(count).toHaveTextContent(/^11 personas$/)
    expect(increase).toBeEnabled()
    expect(fetch.mock.calls.filter(([, options]) => options.method === 'POST')).toHaveLength(0)
  })

  it('envía la cantidad del stepper como numberOfPeople y mantiene el offset correcto', async () => {
    const user = userEvent.setup()
    const onCreated = vi.fn()
    render(<ReservationForm onCreated={onCreated} />)
    const group = within(screen.getByRole('group', { name: 'Cantidad de personas' }))
    for (let i = 0; i < 3; i++) await user.click(group.getByRole('button', { name: 'Aumentar cantidad de personas' }))
    expect(group.getByRole('status')).toHaveTextContent(/^5 personas$/)
    await completeSchedule(user)
    await user.type(screen.getByLabelText('Teléfono'), '08095551234')
    await user.click(screen.getByRole('button', { name: 'Solicitar reserva' }))
    await waitFor(() => expect(onCreated).toHaveBeenCalledOnce())
    const [url, options] = fetch.mock.calls.find(([, options]) => options.method === 'POST')
    expect(url).toBe('https://api.example.test/api/v1/public/reservations')
    expect(JSON.parse(options.body)).toEqual({
      name: 'Ana Pérez', phone: '08095551234', email: null,
      reservationDateTime: '2026-10-05T12:00:00-04:00', numberOfPeople: 5, notes: null,
    })
  })
})
