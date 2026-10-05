// Frontend scheduling policy for this stage; the API remains the final authority.
export const reservationSchedule = {
  timeZone: 'America/Santo_Domingo',
  utcOffset: '-04:00',
  maxAdvanceDays: 30,
  slotIntervalMinutes: 30,
  services: [
    { label: 'Almuerzo', start: '12:00', end: '14:00' },
    { label: 'Cena', start: '18:00', end: '21:30' },
  ],
}

const minutes = (time) => Number(time.slice(0, 2)) * 60 + Number(time.slice(3))
export const reservationSlots = reservationSchedule.services.map(({ label, start, end }) => ({
  label,
  slots: Array.from({ length: (minutes(end) - minutes(start)) / reservationSchedule.slotIntervalMinutes + 1 }, (_, index) => {
    const total = minutes(start) + index * reservationSchedule.slotIntervalMinutes
    const hour = Math.floor(total / 60)
    const minute = String(total % 60).padStart(2, '0')
    return { value: `${String(hour).padStart(2, '0')}:${minute}`, label: `${hour % 12 || 12}:${minute} ${hour < 12 ? 'AM' : 'PM'}` }
  }),
}))

export function reservationDateRange(now = new Date()) {
  const parts = Object.fromEntries(new Intl.DateTimeFormat('en', {
    timeZone: reservationSchedule.timeZone, year: 'numeric', month: '2-digit', day: '2-digit',
  }).formatToParts(now).map(({ type, value }) => [type, value]))
  const min = `${parts.year}-${parts.month}-${parts.day}`
  const last = new Date(`${min}T00:00:00Z`)
  last.setUTCDate(last.getUTCDate() + reservationSchedule.maxAdvanceDays)
  return { min, max: last.toISOString().slice(0, 10) }
}

// Explicit restaurant offset: never parse the selected time in the browser's zone.
export const reservationDateTime = (date, slot) => `${date}T${slot}:00${reservationSchedule.utcOffset}`

export function reservationFormSchedule(value) {
  if (!value) return { date: '', slot: '' }
  const parts = Object.fromEntries(new Intl.DateTimeFormat('en', {
    timeZone: reservationSchedule.timeZone, year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
  }).formatToParts(new Date(value)).map(({ type, value: part }) => [type, part]))
  return { date: `${parts.year}-${parts.month}-${parts.day}`, slot: `${parts.hour}:${parts.minute}` }
}

export function validateReservationSchedule(date, slot, now = new Date()) {
  if (!date) return 'Selecciona una fecha.'
  const parsed = new Date(`${date}T00:00:00Z`)
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date) || !Number.isFinite(parsed.getTime()) || parsed.toISOString().slice(0, 10) !== date) return 'Selecciona una fecha válida.'
  const { min, max } = reservationDateRange(now)
  if (date < min) return 'No puedes reservar en una fecha pasada.'
  if (date > max) return `Selecciona una fecha dentro de los próximos ${reservationSchedule.maxAdvanceDays} días.`
  if (!slot) return 'Selecciona un horario.'
  if (!reservationSlots.some((group) => group.slots.some((item) => item.value === slot))) return 'Selecciona uno de los horarios disponibles.'
  if (new Date(reservationDateTime(date, slot)) <= now) return 'Ese horario ya pasó. Selecciona uno futuro.'
  return ''
}
