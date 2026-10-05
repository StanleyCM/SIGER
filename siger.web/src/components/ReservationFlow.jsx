import { useEffect, useRef, useState } from 'react'
import ReservationForm from './ReservationForm'
import ReservationSummary from './ReservationSummary'
import PreOrderBuilder from './PreOrderBuilder'
import PreOrderConfirmation from './PreOrderConfirmation'
import { restaurantImages } from '../restaurantImages'
import { updateReservation } from '../api/reservationsApi'

export default function ReservationFlow({ onHome }) {
  const [step, setStep] = useState('reservation')
  const [reservation, setReservation] = useState(null)
  const [credential, setCredential] = useState(null)
  const [preorder, setPreorder] = useState(null)
  const [preorderBusy, setPreorderBusy] = useState(false)
  const [editing, setEditing] = useState(false)
  const [updated, setUpdated] = useState(false)
  const preorderSummary = useRef(null)
  const panel = useRef(null)
  const resetFocus = useRef(false)
  const heading = useRef(null)
  useEffect(() => {
    if (editing) panel.current?.querySelector('input[name="name"]')?.focus()
    else if (step !== 'reservation') heading.current?.focus()
    else if (resetFocus.current) {
      panel.current?.querySelector('input[name="name"]')?.focus()
      resetFocus.current = false
    }
  }, [step, editing])

  function created(result) {
    setReservation(result.reservation)
    setCredential({ token: result.accessToken, expiresAt: result.accessTokenExpiresAt })
    setStep('choice')
  }
  function finish() { setStep('done') }
  function newReservation() {
    if (preorderBusy) return
    setCredential(null)
    setReservation(null)
    setPreorder(null)
    setPreorderBusy(false)
    setEditing(false)
    setUpdated(false)
    resetFocus.current = true
    setStep('reservation')
  }

  return (
    <section id="reservacion" className={`section reservation-section${!editing && ['choice', 'done'].includes(step) ? ' reservation-confirmed' : ''}`} aria-labelledby="reservation-title">
      <div className="reservation-editorial"><p className="eyebrow">02 / Nos vemos a la mesa</p><h2 id="reservation-title">Un momento <span className="title-line">para <em>disfrutar.</em></span></h2>
        <p>Nosotros ponemos la mesa.<br />Tú eliges el momento.</p>
        <figure className="reservation-photo"><img src={restaurantImages.atmosphere.src} alt={restaurantImages.atmosphere.alt} width="1400" height="1000" loading="lazy" /></figure>
        <p className="editorial-detail">Sin cuenta ni pago anticipado.<br />Añade una preorden después de reservar.</p></div>
      <div className="reservation-content">
      <ol className="steps" aria-label="Pasos de tu visita"><li className={step === 'reservation' ? 'current' : ''}>01 · Reservación</li>
        <li className={step === 'preorder' ? 'current' : ''}>02 · Preorden opcional</li></ol>
      <div className="flow-panel" ref={panel}>
        {editing && <ReservationForm initialReservation={reservation} availabilityToken={credential.token} onCancel={() => setEditing(false)}
          onSubmitReservation={(body) => updateReservation(reservation.id, credential.token, body)}
          onCreated={(result) => {
            setReservation(result)
            setCredential((current) => ({ ...current, expiresAt: result.accessExpiresAt }))
            setUpdated(true)
            setEditing(false)
          }} />}
        <div hidden={editing}>
        {step === 'reservation' ? <ReservationForm onCreated={created} /> : (
          <div className="reservation-confirmation">
            <header className="confirmation-header">
              <p className="eyebrow">Tu reserva actual</p>
              <h3 className="confirmation-title" tabIndex="-1" ref={heading}>{step === 'preorder' ? 'Elige tu preorden' : updated ? 'Reserva actualizada' : 'Reserva recibida'}</h3>
            </header>
            <ReservationSummary reservation={reservation} />
            <div className="confirmation-edit">
              {reservation.status === 'Pending' && credential && <button className="button button-small button-outline" type="button" disabled={preorderBusy} onClick={() => setEditing(true)}>Editar reserva</button>}
              {preorder && <button className="text-button" type="button" onClick={() => preorderSummary.current?.focus()}>Ver preorden</button>}
            </div>
            {!preorder && step !== 'preorder' && credential && <div className="choice-panel"><h4>¿Deseas realizar una preorden?</h4>
              <p>Elige tus platos con anticipación para tu próxima visita.</p>
              <button className="button confirmation-primary" type="button" onClick={() => setStep('preorder')}>Hacer preorden</button>
            </div>}
            {step === 'preorder' && <PreOrderBuilder reservationId={reservation.id} token={credential.token} expiresAt={credential.expiresAt}
              busy={preorderBusy} setBusy={setPreorderBusy}
              onCreated={(result) => { setPreorder(result); setPreorderBusy(false); setStep('done') }} onFinish={finish} />}
            {step === 'done' && preorder && <div className="completion" role="status" tabIndex="-1" ref={preorderSummary}><PreOrderConfirmation preorder={preorder} />
              <p className="field-hint">Guarda el número de tu reserva para identificar tu solicitud.</p>
              </div>}
            <div className="confirmation-footer">
              <button className="button button-small button-outline confirmation-new" type="button" disabled={preorderBusy}
                onClick={newReservation}>Nueva reserva</button>
              <button className="text-button" type="button" onClick={onHome}>Volver al inicio ↑</button>
            </div>
          </div>
        )}
        </div>
      </div>
      </div>
    </section>
  )
}
