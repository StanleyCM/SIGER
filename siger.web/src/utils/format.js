import { reservationSchedule } from '../config/reservationSlots'

// The API does not define a currency; preserve its amounts without inventing a currency.
export const formatAmount = (amount) => new Intl.NumberFormat('es', {
  minimumFractionDigits: 2, maximumFractionDigits: 2,
}).format(amount)

export const formatDate = (value) => new Intl.DateTimeFormat('es', {
  dateStyle: 'long', timeStyle: 'short', timeZone: reservationSchedule.timeZone,
}).format(new Date(value))

export const formatVisitDate = (value) => new Intl.DateTimeFormat('es', {
  dateStyle: 'long', timeZone: reservationSchedule.timeZone,
}).format(new Date(value))

export const formatVisitTime = (value) => new Intl.DateTimeFormat('en-US', {
  hour: 'numeric', minute: '2-digit', hour12: true, timeZone: reservationSchedule.timeZone,
}).format(new Date(value))


