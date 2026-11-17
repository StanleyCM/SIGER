export default function SiteHeader({ view, onNavigate, onResume, disabled }) {
  return (
    <nav className="floating-nav" aria-label="Navegación principal">
      <button type="button" disabled={disabled} aria-pressed={view === 'menu'} aria-controls="menu-view" onClick={() => onNavigate('menu')}>MENU</button>
      <button type="button" disabled={disabled} aria-pressed={view === 'reservation'} aria-controls="reservation-view" onClick={() => onNavigate('reservation')}>RESERVA</button>
      {onResume && view !== 'reservation' && <button type="button" disabled={disabled}
        aria-label="Volver a mi reserva" aria-controls="reservation-view" onClick={onResume}>MI RESERVA</button>}
    </nav>
  )
}
