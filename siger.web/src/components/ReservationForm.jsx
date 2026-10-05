import { useEffect, useState } from 'react'
import { createReservation, getReservationAvailability } from '../api/reservationsApi'
import { reservationDateRange, reservationDateTime, reservationFormSchedule, reservationSchedule, reservationSlots, validateReservationSchedule } from '../config/reservationSlots'
import { emailError, partySizeLimits, phoneError, sanitizePhone } from '../utils/reservationValidation'
import ReservationDatePicker from './ReservationDatePicker'

export default function ReservationForm({ onCreated, initialReservation, availabilityToken, onCancel, onSubmitReservation = createReservation }) {
  const editing = Boolean(initialReservation)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [date, setDate] = useState(() => reservationFormSchedule(initialReservation?.reservationDateTime).date)
  const [slot, setSlot] = useState(() => reservationFormSchedule(initialReservation?.reservationDateTime).slot)
  const [phone, setPhone] = useState(initialReservation?.phone ?? '')
  const [phoneTouched, setPhoneTouched] = useState(false)
  const [email, setEmail] = useState(initialReservation?.email ?? '')
  const [people, setPeople] = useState(initialReservation?.numberOfPeople ?? 2)
  const phoneMessage = phoneError(phone)
  const emailMessage = emailError(email)
  const [now, setNow] = useState(() => new Date())
  const range = reservationDateRange(now)
  const [availability, setAvailability] = useState(null)
  const [attempt, setAttempt] = useState(0)
  const reservationId = initialReservation?.id
  const availabilityKey = `${date}:${people}:${reservationId ?? ''}:${attempt}`
  const loadingAvailability = Boolean(date) && availability?.key !== availabilityKey
  const availabilityError = availability?.key === availabilityKey ? availability.error : ''
  const isAvailable = (time) => !loadingAvailability && !availabilityError
    && availability?.slots?.some((item) => item.time === time && item.available === true)

  useEffect(() => {
    if (!date) return
    const controller = new AbortController()
    getReservationAvailability(date, people, { reservationId, token: availabilityToken, signal: controller.signal })
      .then((slots) => {
        if (controller.signal.aborted) return
        if (!Array.isArray(slots) || slots.some((item) => !item || typeof item.time !== 'string' || typeof item.available !== 'boolean')) {
          throw new Error('No pudimos consultar los horarios. Inténtalo de nuevo.')
        }
        setAvailability({ key: availabilityKey, slots })
        setSlot((current) => slots.some((item) => item.time === current && item.available) ? current : '')
      })
      .catch((failure) => {
        if (controller.signal.aborted) return
        setAvailability({ key: availabilityKey, error: failure.message })
        setSlot('')
      })
    return () => controller.abort()
  }, [date, people, reservationId, availabilityToken, availabilityKey])
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 60_000)
    return () => clearInterval(timer)
  }, [])

  async function submit(event) {
    event.preventDefault()
    if (busy) return
    const fields = new FormData(event.currentTarget)
    const name = fields.get('name').trim()
    const scheduleError = validateReservationSchedule(date, slot)
    if (scheduleError) { setError(scheduleError); return }
    if (!isAvailable(slot)) { setError('Consulta la disponibilidad y elige un horario disponible.'); return }
    if (!name) { setError('Escribe tu nombre.'); return }
    if (phoneMessage || emailMessage) {
      setPhoneTouched(true)
      setError('')
      event.currentTarget.elements.namedItem(phoneMessage ? 'phone' : 'email').focus()
      return
    }
    if (!Number.isInteger(people) || people < partySizeLimits.min || people > partySizeLimits.max) {
      setError('Selecciona la cantidad de personas.'); return
    }
    const invalid = Array.from(event.currentTarget.elements).find((field) => field.willValidate && !field.validity.valid)
    if (invalid) { setError(`Revisa el campo «${invalid.labels?.[0]?.textContent || invalid.name}».`); invalid.focus(); return }
    setBusy(true)
    setError('')
    try {
      const created = await onSubmitReservation({
        name, phone, email: email.trim() || null,
        reservationDateTime: reservationDateTime(date, slot), numberOfPeople: people,
        notes: fields.get('notes').trim() || null,
      })
      onCreated(created)
    } catch (failure) {
      setError(failure.message)
      setBusy(false)
      if (failure.status === 409) setAttempt((current) => current + 1)
    }
  }

  return (
    <form onSubmit={submit} noValidate className="reservation-form" aria-label={editing ? 'Editar reservación' : 'Solicitar reservación'} aria-busy={busy}>
      <h3 className="form-title">{editing ? 'Editar tu reserva' : 'Tu próxima visita'}</h3>
      <p className="form-intro">Elige tu momento y déjanos tus datos.</p>
      <p className="field-hint">Todos los campos son obligatorios excepto los marcados como opcionales.</p>
      <fieldset disabled={busy}>
        <legend className="sr-only">Datos de tu reserva</legend>
        <div className="form-grid">
          <label>Nombre<input name="name" defaultValue={initialReservation?.name ?? ''} autoComplete="name" required maxLength={150} placeholder="Tu nombre y apellido" /></label>
          <div>
            <label>Teléfono<input name="phone" type="tel" inputMode="numeric" autoComplete="tel" required
              value={phone} onChange={(event) => { setPhone(sanitizePhone(event.target.value)); setPhoneTouched(true) }}
              onBlur={() => setPhoneTouched(true)} aria-invalid={phoneTouched && Boolean(phoneMessage)}
              aria-describedby={phoneTouched && phoneMessage ? 'phone-error' : undefined} placeholder="Tu número de contacto" /></label>
            {phoneTouched && phoneMessage && <p className="field-error" id="phone-error" aria-live="polite">{phoneMessage}</p>}
          </div>
          <div>
            <label>Correo (opcional)<input name="email" type="email" autoComplete="email" maxLength={150}
              value={email} onChange={(event) => setEmail(event.target.value)} aria-invalid={Boolean(emailMessage)}
              aria-describedby={emailMessage ? 'email-error' : undefined} placeholder="nombre@correo.com" /></label>
            {emailMessage && <p className="field-error" id="email-error" aria-live="polite">{emailMessage}</p>}
          </div>
          <ReservationDatePicker value={date} min={range.min} max={range.max}
            onChange={(value) => { setDate(value); setSlot(''); setError(''); setNow(new Date()) }} />
          <fieldset className="slot-picker full-width">
            <legend>Cantidad de personas</legend>
            <div className="party-stepper">
              <button type="button" aria-label="Disminuir cantidad de personas" aria-controls="party-count"
                disabled={people <= partySizeLimits.min}
                onClick={() => setPeople((current) => Math.max(partySizeLimits.min, current - 1))}>−</button>
              <output id="party-count" aria-live="polite" aria-atomic="true">{people} {people === 1 ? 'persona' : 'personas'}</output>
              <button type="button" aria-label="Aumentar cantidad de personas" aria-controls="party-count"
                disabled={people >= partySizeLimits.max}
                onClick={() => setPeople((current) => Math.min(partySizeLimits.max, current + 1))}>+</button>
            </div>
          </fieldset>
          <p className="field-hint full-width" id="time-hint">Hora de República Dominicana (UTC−04:00). Puedes reservar desde hoy hasta {reservationSchedule.maxAdvanceDays} días después.</p>
          <fieldset className="slot-picker full-width" aria-describedby="slot-hint" aria-busy={loadingAvailability}>
            <legend>Horario</legend>
            <p className="field-hint" id="slot-hint" role="status">{!date ? 'Selecciona una fecha para ver los horarios disponibles.'
              : loadingAvailability ? 'Consultando disponibilidad…'
                : availabilityError ? availabilityError : 'Elige un horario. Gris = no disponible o pasado.'}</p>
            {availabilityError && <button className="text-button availability-retry" type="button"
              onClick={() => setAttempt((current) => current + 1)}>Reintentar disponibilidad</button>}
            {reservationSlots.map((group) => <div className="slot-group" key={group.label}>
              <p className="slot-group-title">{group.label}</p><div className="slot-options">
                {group.slots.map((item) => <label className="slot-choice" key={item.value}>
                  <input type="radio" name="time" value={item.value} checked={slot === item.value} required
                    disabled={Boolean(validateReservationSchedule(date, item.value, now)) || !isAvailable(item.value)}
                    onChange={() => { setSlot(item.value); setError('') }} />
                  <span>{item.label}</span>
                </label>)}
              </div>
            </div>)}
          </fieldset>
          <label className="full-width">Observaciones (opcional)<textarea name="notes" defaultValue={initialReservation?.notes ?? ''} rows="2" maxLength={500} placeholder="Algo que debamos tener en cuenta (máximo 500 caracteres)" /></label>
        </div>
      </fieldset>
      {error && <p className="error-message" role="alert">{error}</p>}
      <div className="form-submit"><button className="button" type="submit" disabled={busy || loadingAvailability || Boolean(availabilityError)}>{busy ? 'Enviando solicitud…' : editing ? 'Guardar cambios' : 'Solicitar reserva'}</button>
        {editing && <button className="text-button" type="button" disabled={busy} onClick={onCancel}>Cancelar edición</button>}
        <span className="field-hint">Sujeta a disponibilidad y confirmación.</span></div>
    </form>
  )
}
