import { useState } from 'react'
import { createPreOrder } from '../api/reservationsApi'
import { formatAmount } from '../utils/format'
import ProductCatalog from './ProductCatalog'
import { MAX_PREORDER_PRODUCTS, MAX_PRODUCT_QUANTITY } from '../config/preorder'

export default function PreOrderBuilder({ reservationId, token, expiresAt, onCreated, onFinish, busy, setBusy }) {
  const [items, setItems] = useState([])
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  function add(product) {
    const existing = items.find((item) => item.productId === product.id)
    if ((!existing && items.length >= MAX_PREORDER_PRODUCTS) || existing?.quantity >= MAX_PRODUCT_QUANTITY) {
      setError('Puedes elegir hasta 50 productos distintos y 100 unidades de cada uno.'); return
    }
    setItems(existing ? items.map((item) => item.productId === product.id ? { ...item, quantity: item.quantity + 1 } : item)
      : [...items, { productId: product.id, name: product.name, price: product.price, quantity: 1, notes: '' }])
    setError('')
    setNotice(`${product.name} agregado a tu selección.`)
  }

  function change(id, patch) { setItems(items.map((item) => item.productId === id ? { ...item, ...patch } : item)) }

  function changeQuantity(id, delta) {
    setItems((current) => current.map((item) => item.productId === id
      ? { ...item, quantity: Math.max(0, Math.min(MAX_PRODUCT_QUANTITY, item.quantity + delta)) }
      : item).filter((item) => item.quantity > 0))
  }

  async function submit(event) {
    event.preventDefault()
    if (busy || items.length === 0) return
    if (new Date(expiresAt) <= new Date()) { setError('El acceso a esta reserva venció. Contacta al restaurante para continuar.'); return }
    setBusy(true)
    setError('')
    try { onCreated(await createPreOrder(reservationId, token, items)) }
    catch (failure) { setError(failure.message); setBusy(false) }
  }

  return (
    <div className="preorder-builder">
      <p>Elige tus productos. El importe definitivo se calcula al guardar tu preorden.</p>
      <ProductCatalog onAdd={add} onDecrease={(id) => changeQuantity(id, -1)} items={items} disabled={busy} />
      <p className="field-hint" role="status">{notice}</p>
      <form onSubmit={submit} className="selection" aria-label="Tu preorden" aria-busy={busy}>
        <h4>Tu selección</h4>
        {items.length === 0 ? <p className="field-hint">Aún no has agregado productos.</p> : (
          <ul className="selected-items">{items.map((item) => (
            <li key={item.productId}>
              <div className="selected-top"><strong>{item.name}</strong><span>{formatAmount(item.price)} por unidad</span></div>
              <div className="quantity-row">
                <div className="quantity-control">
                  <button type="button" disabled={busy} aria-label={`Disminuir ${item.name}`} onClick={() => changeQuantity(item.productId, -1)}>−</button>
                  <output aria-label={`Cantidad de ${item.name}`}>{item.quantity}</output>
                  <button type="button" disabled={busy || item.quantity >= MAX_PRODUCT_QUANTITY} aria-label={`Aumentar ${item.name}`} onClick={() => changeQuantity(item.productId, 1)}>+</button>
                </div>
                <button className="text-button" type="button" disabled={busy} aria-label={`Eliminar ${item.name}`} onClick={() => setItems(items.filter((i) => i.productId !== item.productId))}>Eliminar</button>
              </div>
              <label>Nota para {item.name} (opcional)<textarea value={item.notes} disabled={busy} rows="2" maxLength={300} placeholder="Máximo 300 caracteres" onChange={(e) => change(item.productId, { notes: e.target.value })} /></label>
            </li>
          ))}</ul>
        )}
        {items.length > 0 && <p className="estimated-total">Total estimado <strong>{formatAmount(items.reduce((sum, item) => sum + item.price * item.quantity, 0))}</strong></p>}
        <p className="field-hint">No se realizará ningún pago. La reservación permanece pendiente de confirmación.</p>
        {error && <p className="error-message" role="alert">{error}</p>}
        <div className="actions">
          <button className="button" type="submit" disabled={busy || items.length === 0}>{busy ? 'Guardando preorden…' : 'Guardar preorden'}</button>
          <button className="text-button" type="button" disabled={busy} onClick={onFinish}>Finalizar sin preorden</button>
        </div>
      </form>
    </div>
  )
}
