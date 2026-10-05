export default function SiteHeader({ view, onNavigate }) {
  return (
    <nav className="floating-nav" aria-label="Navegación principal">
      <button type="button" aria-pressed={view === 'menu'} aria-controls="menu-view" onClick={() => onNavigate('menu')}>MENU</button>
      <button type="button" aria-pressed={view === 'reservation'} aria-controls="reservation-view" onClick={() => onNavigate('reservation')}>RESERVA</button>
    </nav>
  )
}
