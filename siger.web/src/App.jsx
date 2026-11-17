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
  const [hasReservation, setHasReservation] = useState(false)
  const [writing, setWriting] = useState(false)
  const writeLock = useRef(false)
  const main = useRef(null)

  async function runWrite(operation) {
    if (writeLock.current) return
    writeLock.current = true
    setWriting(true)
    try { await operation() }
    finally {
      writeLock.current = false
      setWriting(false)
    }
  }

  function navigate(next) {
    if (writeLock.current) return
    if (next === 'reservation') {
      setHasReservation(false)
      setReservationSession((session) => session + 1)
    }
    setVisited((previous) => ({ ...previous, [next]: true }))
    setView(next)
  }

  useEffect(() => {
    // Moving focus to the new view also brings its beginning into view.
    main.current?.focus()
  }, [view, reservationSession])

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
            {visited.reservation && <><button className="back-home text-button" disabled={writing} onClick={() => navigate('home')}>← Volver al inicio</button>
              <ReservationFlow key={reservationSession} runWrite={runWrite} writing={writing}
                onActiveChange={setHasReservation} onHome={() => navigate('home')} /></>}
          </div>
        </main>
        <SiteFooter disabled={writing} onHome={() => navigate('home')} />
        <SiteHeader view={view} onNavigate={navigate} disabled={writing}
          onResume={hasReservation ? () => setView('reservation') : undefined} />
      </div>
    </>
  )
}
