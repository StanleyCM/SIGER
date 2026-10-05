import { formatVisitDate, formatVisitTime } from '../utils/format'

export default function ReservationSummary({ reservation }) {
  return (
    <div className="reservation-summary">
      <h4 className="summary-number">Reserva #{reservation.id}</h4>
      <div className="summary-visit">
        <time dateTime={reservation.reservationDateTime}>
          <span className="summary-date">{formatVisitDate(reservation.reservationDateTime)}</span>
          <span className="summary-time">{formatVisitTime(reservation.reservationDateTime)}</span>
        </time>
        <p className="summary-people">{reservation.numberOfPeople} {reservation.numberOfPeople === 1 ? 'persona' : 'personas'}</p>
      </div>
      <div className="summary-status"><span>Estado:</span><span className="status-badge">Pendiente</span></div>
      <p className="summary-zone">Hora de República Dominicana</p>
      {reservation.notes && <p className="summary-notes">{reservation.notes}</p>}
    </div>
  )
}
