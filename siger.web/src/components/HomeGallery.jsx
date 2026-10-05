import { restaurantImages } from '../restaurantImages'

export default function HomeGallery() {
  return (
    <>
      <section className="visual-course" aria-labelledby="share-title">
        <div className="visual-caption"><span className="eyebrow">A la mesa</span><h2 id="share-title">El placer<br />de <em>compartir.</em></h2></div>
        <img src={restaurantImages.menu.src} alt={restaurantImages.menu.alt} width="800" height="1000" loading="lazy" />
      </section>
      <section className="atmosphere" aria-labelledby="atmosphere-title">
        <img src={restaurantImages.atmosphere.src} alt={restaurantImages.atmosphere.alt} width="1400" height="1000" loading="lazy" />
        <h2 id="atmosphere-title">Quédate<br /><em>un momento.</em></h2>
        <p className="photo-disclaimer">Fotografías de inspiración · Imágenes temporales</p>
      </section>
    </>
  )
}
