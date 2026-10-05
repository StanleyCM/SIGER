import { formatAmount } from '../utils/format'

export default function PreOrderConfirmation({ preorder }) {
  return (
    <div className="preorder-confirmation">
      <h4>Preorden registrada</h4>
      <ul className="confirmation-items">{preorder.items.map((item) => (
        <li key={item.productId}><div><strong>{item.productName}</strong><span>{item.quantity} × {formatAmount(item.unitPrice)}</span>
          {item.notes && <p>{item.notes}</p>}</div>{item.subtotal != null && <strong>{formatAmount(item.subtotal)}</strong>}</li>
      ))}</ul>
      <p className="estimated-total">Total de tu preorden <strong>{formatAmount(preorder.total)}</strong></p>
      <p>La reservación permanece pendiente de confirmación.</p>
    </div>
  )
}
