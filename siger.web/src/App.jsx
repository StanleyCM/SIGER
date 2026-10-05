import { useEffect, useRef, useState } from 'react'
import SiteHeader from './components/SiteHeader'
import Hero from './components/Hero'
import HomeGallery from './components/HomeGallery'
import MenuSection from './components/MenuSection'
import ReservationFlow from './components/ReservationFlow'
import SiteFooter from './components/SiteFooter'
import './App.css'

export default function App() {
  const [view, setView] = useState('home')
  const [reservationSession, setReservationSession] = useState(0)
  const [visited, setVisited] = useState({ menu: false, reservation: false })
  const main = useRef(null)

  function navigate(next) {
    if (next === 'reservation') setReservationSession((session) => session + 1)
    setVisited((previous) => ({ ...previous, [next]: true }))
    setView(next)
  }

  useEffect(() => {
    // Moving focus to the new view also brings its beginning into view.
    main.current?.focus()
  }, [view])

  return (
    <>
      <a className="skip-link" href="#contenido">Saltar al contenido</a>
      <div id="inicio">
        <main id="contenido" ref={main} tabIndex={-1}>
          <div hidden={view !== 'home'}><Hero /><HomeGallery /></div>
          <div id="menu-view" className="page-shell internal-view" hidden={view !== 'menu'}>
            {visited.menu && <><button className="back-home text-button" onClick={() => navigate('home')}>← Volver al inicio</button><MenuSection /></>}
          </div>
          <div id="reservation-view" className="page-shell internal-view" hidden={view !== 'reservation'}>
            {visited.reservation && <><button className="back-home text-button" onClick={() => navigate('home')}>← Volver al inicio</button><ReservationFlow key={reservationSession} onHome={() => navigate('home')} /></>}
          </div>
        </main>
        <SiteFooter onHome={() => navigate('home')} />
        <SiteHeader view={view} onNavigate={navigate} />
      </div>
    </>
  )
}
