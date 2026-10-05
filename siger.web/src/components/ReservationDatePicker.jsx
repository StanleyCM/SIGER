import { useId, useRef, useState } from 'react'
import { DayPicker, TZDate } from '@daypicker/react'
import { es } from '@daypicker/react/locale'
import '@daypicker/react/style.css'
import { reservationSchedule } from '../config/reservationSlots'

// Calendar-only dates stay in the restaurant's zone, independent of the device.
function calendarDate(value) {
  const [year, month, day] = value.split('-').map(Number)
  return new TZDate(year, month - 1, day, 12, 0, 0, reservationSchedule.timeZone)
}

const dateLabel = new Intl.DateTimeFormat('es-DO', {
  day: 'numeric', month: 'long', year: 'numeric', timeZone: reservationSchedule.timeZone,
})

export default function ReservationDatePicker({ value, min, max, onChange }) {
  const id = useId()
  const dialog = useRef(null)
  const trigger = useRef(null)
  const [open, setOpen] = useState(false)
  const selected = value ? calendarDate(value) : undefined
  const first = calendarDate(min)
  const last = calendarDate(max)

  function select(day) {
    if (!day) return
    const next = `${day.getFullYear()}-${String(day.getMonth() + 1).padStart(2, '0')}-${String(day.getDate()).padStart(2, '0')}`
    if (next < min || next > max) return
    onChange(next)
    dialog.current.close()
  }

  return (
    <div className="reservation-date">
      <label htmlFor={id}>Fecha</label>
      <button ref={trigger} id={id} type="button" className="date-trigger" aria-haspopup="dialog"
        aria-expanded={open} aria-controls={`${id}-calendar`} aria-describedby={`${id}-value time-hint`}
        onClick={() => { dialog.current.showModal(); setOpen(true) }}>
        <span id={`${id}-value`}>{selected ? dateLabel.format(selected) : 'Elige tu fecha'}</span>
        <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="5" width="18" height="16" rx="3" /><path d="M7 3v4m10-4v4M3 11h18m-14 4h3m4 0h3" /></svg>
      </button>
      <dialog ref={dialog} id={`${id}-calendar`} className="date-dialog" aria-labelledby={`${id}-title`}
        onClose={() => { setOpen(false); trigger.current?.focus() }}>
        <div className="date-dialog-header">
          <h4 id={`${id}-title`}>Elige tu fecha</h4>
          <button type="button" className="calendar-close" aria-label="Cerrar calendario" onClick={() => dialog.current.close()}>×</button>
        </div>
        {open && <DayPicker mode="single" required autoFocus locale={es} timeZone={reservationSchedule.timeZone}
          selected={selected} defaultMonth={selected && value >= min && value <= max ? selected : first}
          today={first} startMonth={first} endMonth={last} disabled={[{ before: first }, { after: last }]}
          onSelect={select} navLayout="around" />}
        <p className="field-hint">Del {dateLabel.format(first)} al {dateLabel.format(last)}.</p>
      </dialog>
    </div>
  )
}
