import { useEffect, useRef, useState } from 'react'
import ReservationForm from './ReservationForm'
import ReservationSummary from './ReservationSummary'
import PreOrderBuilder from './PreOrderBuilder'
import PreOrderConfirmation from './PreOrderConfirmation'
import { restaurantImages } from '../restaurantImages'
import { getPreOrder, getReservation, updateReservation } from '../api/reservationsApi'

export default function ReservationFlow({ onHome, runWrite, writing, onActiveChange }) {
  const [step, setStep] = useState('reservation')
  const [reservation, setReservation] = useState(null)
  const [credential, setCredential] = useState(null)
  const [preorder, setPreorder] = useState(null)
  const [preorderBusy, setPreorderBusy] = useState(false)
  const [editing, setEditing] = useState(null)
  const [updated, setUpdated] = useState(false)
  const [checking, setChecking] = useState(false)
  const [refreshMessage, setRefreshMessage] = useState('')
  const [refreshError, setRefreshError] = useState('')
  const [preorderUncertain, setPreorderUncertain] = useState(false)
  const [now, setNow] = useState(() => Date.now())
  const refreshRequest = useRef(null)
  const preorderSummary = useRef(null)
  const panel = useRef(null)
  const resetFocus = useRef(false)
  const heading = useRef(null)
  const reservationId = reservation?.id
  const isFuture = reservation && Date.parse(reservation.reservationDateTime) > now
  const canEdit = isFuture && reservation.status === 'Pending'
  const canPreOrder = isFuture && ['Pending', 'Confirmed'].includes(reservation.status)
  useEffect(() => () => refreshRequest.current?.abort(), [])
  useEffect(() => {
    if (!reservationId) return
    const timer = setInterval(() => setNow(Date.now()), 60_000)
    return () => clearInterval(timer)
  }, [reservationId])
  useEffect(() => {
    if (editing) panel.current?.querySelector('input[name="name"]')?.focus()
    else if (step !== 'reservation') heading.current?.focus()
    else if (resetFocus.current) {
      panel.current?.querySelector('input[name="name"]')?.focus()
      resetFocus.current = false
    }
  }, [step, editing])

  function created(result) {
    setNow(Date.now())
    setReservation(result.reservation)
    setCredential({ token: result.accessToken, expiresAt: result.accessTokenExpiresAt })
    onActiveChange(true)
    setStep('choice')
  }
  function finish() { setStep('done') }
  function newReservation() {
    if (preorderBusy) return
    setCredential(null)
    setReservation(null)
    setPreorder(null)
    setPreorderBusy(false)
    setEditing(null)
    setUpdated(false)
    setPreorderUncertain(false)
    setRefreshError('')
    setRefreshMessage('')
    onActiveChange(false)
    resetFocus.current = true
    setStep('reservation')
  }

  function applyReservation(current) {
    setNow(Date.now())
    setReservation(current)
    setCredential((previous) => ({ ...previous, expiresAt: current.accessExpiresAt ?? previous.expiresAt }))
  }

  async function checkEdit() {
    const current = await getReservation(reservation.id, credential.token)
    applyReservation(current)
    return current
  }

  async function refresh() {
    if (refreshRequest.current || writing) return
    const controller = new AbortController()
    refreshRequest.current = controller
    setChecking(true)
    setRefreshMessage('')
    setRefreshError('')
    const [current, order] = await Promise.allSettled([
      getReservation(reservation.id, credential.token, controller.signal),
      getPreOrder(reservation.id, credential.token, controller.signal),
    ])
    refreshRequest.current = null
    if (controller.signal.aborted) return
    const errors = new Set()
    if (current.status === 'fulfilled') applyReservation(current.value)
    else errors.add(current.reason.message)
    if (order.status === 'fulfilled') {
      setPreorder(order.value)
      setPreorderUncertain(false)
      setStep('done')
    } else if (order.reason.status === 404 && !preorder && current.status === 'fulfilled') {
      if (preorderUncertain) setRefreshMessage('No hay una preorden registrada en este momento. Conservamos tu selección.')
      setPreorderUncertain(false)
    } else errors.add(order.reason.message)
    if (errors.size) setRefreshError([...errors].join(' '))
    else if (!preorderUncertain || order.status === 'fulfilled') setRefreshMessage('Estado consultado.')
    setChecking(false)
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
        {editing && <ReservationForm initialReservation={editing} availabilityToken={credential.token} onCancel={() => setEditing(null)}
          runWrite={runWrite} blocked={!canEdit} onCheckWrite={checkEdit}
          onSubmitReservation={(body) => updateReservation(reservation.id, credential.token, body)}
          onCreated={(result) => {
            applyReservation(result)
            setUpdated(true)
            setEditing(null)
          }} />}
        <div hidden={editing}>
        {step === 'reservation' ? <ReservationForm onCreated={created} runWrite={runWrite} /> : (
          <div className="reservation-confirmation">
            <header className="confirmation-header">
              <p className="eyebrow">Tu reserva actual</p>
              <h3 className="confirmation-title" tabIndex="-1" ref={heading}>{step === 'preorder' ? 'Elige tu preorden' : updated ? 'Reserva actualizada' : 'Reserva recibida'}</h3>
            </header>
            <ReservationSummary reservation={reservation} />
            <div className="confirmation-edit">
              {credential && <button className="text-button" type="button" disabled={writing || checking}
                onClick={refresh}>{checking ? 'Consultando estado…' : 'Consultar estado'}</button>}
              {canEdit && credential && <button className="button button-small button-outline" type="button" disabled={writing || checking}
                onClick={() => setEditing(reservation)}>Editar reserva</button>}
              {preorder && <button className="text-button" type="button" onClick={() => preorderSummary.current?.focus()}>Ver preorden</button>}
            </div>
            {refreshError && <p className="error-message" role="alert">{refreshError}</p>}
            {refreshMessage && <p className="field-hint" role="status">{refreshMessage}</p>}
            {!preorder && step !== 'preorder' && credential && canPreOrder && <div className="choice-panel"><h4>¿Deseas realizar una preorden?</h4>
              <p>Elige tus platos con anticipación para tu próxima visita.</p>
              <button className="button confirmation-primary" type="button" disabled={checking} onClick={() => setStep('preorder')}>Hacer preorden</button>
            </div>}
            {step === 'preorder' && <PreOrderBuilder reservationId={reservation.id} token={credential.token} expiresAt={credential.expiresAt}
              busy={preorderBusy} setBusy={setPreorderBusy}
              runWrite={runWrite} blocked={checking || preorderUncertain || !canPreOrder}
              onUncertain={(message) => { setPreorderUncertain(true); setRefreshMessage(''); setRefreshError(message) }}
              onCreated={(result) => { setPreorder(result); setRefreshMessage(''); setRefreshError(''); setStep('done') }} onFinish={finish} />}
            {step === 'done' && preorder && <div className="completion" role="status" tabIndex="-1" ref={preorderSummary}><PreOrderConfirmation preorder={preorder} reservationStatus={reservation.status} />
              </div>}
            <p className="field-hint">Si recargas o cierras la página, perderás el acceso desde esta sesión. Guarda el número para identificar tu solicitud con el restaurante.</p>
            <div className="confirmation-footer">
              <button className="button button-small button-outline confirmation-new" type="button" disabled={writing || checking}
                onClick={newReservation}>Nueva reserva</button>
              <button className="text-button" type="button" disabled={writing} onClick={onHome}>Volver al inicio ↑</button>
            </div>
          </div>
        )}
        </div>
      </div>
      </div>
    </section>
  )
}
