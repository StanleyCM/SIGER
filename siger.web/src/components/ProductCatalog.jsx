import { useEffect, useId, useState } from 'react'
import { getCategories, getProducts } from '../api/catalogApi'
import { formatAmount } from '../utils/format'
import { groupProducts, sortCategories } from '../utils/catalog'
import { MAX_PRODUCT_QUANTITY } from '../config/preorder'
import ProductImage from './ProductImage'

export default function ProductCatalog({ onAdd, onDecrease, items = [], disabled = false }) {
  const id = useId()
  const [filter, setFilter] = useState({ category: '', page: 1, attempt: 0 })
  const [result, setResult] = useState(null)
  const key = `${filter.category}:${filter.page}:${filter.attempt}`
  const loading = result?.key !== key

  useEffect(() => {
    const controller = new AbortController()
    Promise.all([getCategories(controller.signal), getProducts(filter.category, filter.page, controller.signal)])
      .then(([categories, products]) => {
        if (!controller.signal.aborted) setResult({ key, categories, products })
      })
      .catch((error) => {
        if (!controller.signal.aborted) setResult({ key, error: error.message })
      })
    return () => controller.abort()
  }, [key, filter.category, filter.page])

  return (
    <div className="catalog" aria-busy={loading}>
      <div className="catalog-toolbar">
        <label htmlFor={id}>Categoría</label>
        <select id={id} value={filter.category} disabled={disabled || loading}
          onChange={(event) => setFilter({ ...filter, category: event.target.value, page: 1 })}>
          <option value="">Todas las categorías</option>
          {sortCategories(result?.categories || []).map((category) => <option key={category.id} value={category.id}>{category.name}</option>)}
        </select>
      </div>
      {loading ? <p className="empty-state" role="status">Cargando el menú…</p> : result.error ? (
        <div className="error-message" role="alert">
          <p>{result.error}</p>
          <button className="button button-outline" type="button" disabled={disabled}
            onClick={() => setFilter({ ...filter, attempt: filter.attempt + 1 })}>Volver a intentar</button>
        </div>
      ) : result.products.items.length === 0 ? (
        <p className="empty-state" role="status">{filter.category ? 'No hay productos disponibles en esta categoría.' : 'El menú estará disponible próximamente. Puedes solicitar una reserva sin preorden.'}</p>
      ) : (
        <>
          <div className="catalog-groups">
            {groupProducts(result.categories, result.products.items).map((category) => (
              <section className="catalog-category" key={category.id} aria-labelledby={`${id}-category-${category.id}`}>
                <h3 className="category-title" id={`${id}-category-${category.id}`}>{category.name}</h3>
                <div className="product-grid">
                  {category.products.map((product) => {
                    const quantity = items.find((item) => item.productId === product.id)?.quantity || 0
                    return (
                      <article className="product-card" key={product.id} aria-labelledby={`${id}-product-${product.id}`}>
                        <ProductImage src={product.imageUrl} name={product.name} />
                        <h4 id={`${id}-product-${product.id}`}>{product.name}</h4>
                        {product.description && <p className="product-description">{product.description}</p>}
                        <div className="product-bottom">
                          <span className="price"><span className="sr-only">Precio: </span>{formatAmount(product.price)}</span>
                          {onAdd && (quantity === 0 ? (
                            <button className="button button-outline button-small product-add" type="button" disabled={disabled}
                              aria-label={`Agregar ${product.name}`} onClick={() => onAdd(product)}>Agregar <span aria-hidden="true">+</span></button>
                          ) : (
                            <div className="quantity-control product-quantity" role="group" aria-label={`Cantidad en preorden de ${product.name}`}>
                              <button type="button" disabled={disabled} aria-label={`Quitar una unidad de ${product.name}`} onClick={() => onDecrease(product.id)}>−</button>
                              <output aria-label={`Cantidad de ${product.name} en el catálogo`} aria-live="polite">{quantity}</output>
                              <button type="button" disabled={disabled || quantity >= MAX_PRODUCT_QUANTITY} aria-label={`Agregar una unidad de ${product.name}`} onClick={() => onAdd(product)}>+</button>
                            </div>
                          ))}
                        </div>
                      </article>
                    )
                  })}
                </div>
              </section>
            ))}
          </div>
          {result.products.totalPages > 1 && (
            <nav className="pagination" aria-label="Páginas del menú">
              <button className="button button-outline button-small" type="button" disabled={disabled || filter.page === 1}
                onClick={() => setFilter({ ...filter, page: filter.page - 1 })}>Anterior</button>
              <span>Página {filter.page} de {result.products.totalPages}</span>
              <button className="button button-outline button-small" type="button" disabled={disabled || filter.page >= result.products.totalPages}
                onClick={() => setFilter({ ...filter, page: filter.page + 1 })}>Siguiente</button>
            </nav>
          )}
        </>
      )}
    </div>
  )
}
