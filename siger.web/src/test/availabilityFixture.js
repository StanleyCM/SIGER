import { reservationSlots } from '../config/reservationSlots'

export const availableSlots = () => reservationSlots.flatMap((group) => group.slots.map((slot) => ({ time: slot.value, available: true })))
