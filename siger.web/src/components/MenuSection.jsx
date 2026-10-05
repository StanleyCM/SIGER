import ProductCatalog from './ProductCatalog'
import { restaurantImages } from '../restaurantImages'

export default function MenuSection() {
  return (
    <section id="menu" className="section menu-section" aria-labelledby="menu-title">
      <div className="menu-intro">
        <figure className="menu-photo"><img src={restaurantImages.menu.src} alt={restaurantImages.menu.alt} width="800" height="1000" loading="lazy" /><figcaption>Un anticipo del encuentro · Imagen de inspiración</figcaption></figure>
        <div className="menu-editorial"><p className="eyebrow">A tu gusto</p><h2 id="menu-title">Nuestro <em>menú</em></h2>
          <p className="editorial-detail">Algo para ti, mucho para compartir. Explora los platos disponibles por categoría.</p></div>
      </div>
      <ProductCatalog />
    </section>
  )
}
