import { restaurantImages } from '../restaurantImages'

export default function Hero() {
  return (
    <section className="hero" aria-labelledby="hero-title">
      <img className="hero-image" src={restaurantImages.hero.src} alt={restaurantImages.hero.alt} width="1200" height="1500" fetchPriority="high" />
      <div className="hero-content"><p className="eyebrow">El gusto de encontrarnos</p>
        <h1 id="hero-title">Superior<span>Kitchen Essentials</span></h1>
        <p>Sabores para compartir.</p></div>
      <span className="hero-scroll" aria-hidden="true">Descubre ↓</span>
    </section>
  )
}
