import { useState } from 'react'

export default function ProductImage({ src, name }) {
  const [failedSource, setFailedSource] = useState(null)
  if (src && src !== failedSource) {
    return <img className="product-photo" src={src} alt={name} loading="lazy" onError={() => setFailedSource(src)} />
  }
  return (
    <div className="product-photo product-placeholder" role="img" aria-label={`Imagen no disponible de ${name}`}>
      <svg viewBox="0 0 80 80" aria-hidden="true" focusable="false">
        <circle cx="40" cy="40" r="28" /><circle cx="40" cy="40" r="21" />
        <path d="M4 12v17c0 7 8 7 8 0V12M8 12v56M75 12v56M75 12c-9 10-9 22 0 25" />
      </svg>
      <span>Imagen no disponible</span>
    </div>
  )
}
