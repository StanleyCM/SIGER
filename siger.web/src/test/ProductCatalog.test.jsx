import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import ProductCatalog from '../components/ProductCatalog'
import PreOrderBuilder from '../components/PreOrderBuilder'

const categories = [
  { id: 90, name: 'Especiales' }, { id: 14, name: 'Postres' },
  { id: 11, name: 'Entradas' }, { id: 80, name: 'Acompañamientos' },
  { id: 13, name: 'Bebidas' }, { id: 12, name: 'Platos fuertes' },
]
const products = [
  { id: 1, categoryId: 14, name: 'Cheesecake', price: 250 },
  { id: 2, categoryId: 13, name: 'Limonada natural', price: 120 },
  { id: 3, categoryId: 11, name: 'Bruschetta de tomate', price: 250 },
  { id: 4, categoryId: 12, name: 'Hamburguesa artesanal', price: 550 },
  { id: 5, categoryId: 11, name: 'Croquetas de queso', price: 300 },
  { id: 6, categoryId: 90, name: 'Plato del día', price: 500 },
  { id: 7, categoryId: 80, name: 'Papas', price: 100 },
]

beforeEach(() => {
  vi.stubEnv('VITE_API_BASE_URL', 'https://api.example.test')
  vi.stubGlobal('fetch', vi.fn(async (url) => {
    if (url.endsWith('/categories')) return Response.json(categories)
    const category = new URL(url).searchParams.get('categoryId')
    const items = products.filter((product) => !category || String(product.categoryId) === category)
    return Response.json({ items, totalCount: items.length, totalPages: 1, pageNumber: 1 })
  }))
})

afterEach(() => { vi.unstubAllGlobals(); vi.unstubAllEnvs() })

function renderPreorder() {
  render(<PreOrderBuilder reservationId={9} token="test-token" expiresAt="2099-01-01T00:00:00Z"
    onCreated={vi.fn()} onFinish={vi.fn()} busy={false} setBusy={vi.fn()} />)
}

describe('Catálogo agrupado y cantidades de preorden', () => {
  it('agrupa por categoryId y ordena las conocidas primero, luego las adicionales por nombre', async () => {
    render(<ProductCatalog />)
    await screen.findByRole('heading', { name: 'Bruschetta de tomate' })
    expect(screen.getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent))
      .toEqual(['Entradas', 'Platos fuertes', 'Bebidas', 'Postres', 'Acompañamientos', 'Especiales'])
    const entries = within(screen.getByRole('region', { name: 'Entradas' }))
    expect(entries.getAllByRole('article')).toHaveLength(2)
    expect(entries.getByRole('heading', { name: 'Croquetas de queso' })).toBeInTheDocument()
    expect(entries.queryByRole('heading', { name: 'Cheesecake' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Agregar/ })).not.toBeInTheDocument()
  })

  it('filtra una categoría por API y restaura todas agrupadas', async () => {
    const user = userEvent.setup()
    render(<ProductCatalog />)
    await screen.findByRole('heading', { name: 'Bruschetta de tomate' })
    await user.selectOptions(screen.getByLabelText('Categoría'), '13')
    await screen.findByRole('heading', { name: 'Limonada natural' })
    expect(screen.getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)).toEqual(['Bebidas'])
    expect(screen.getAllByRole('article')).toHaveLength(1)
    expect(fetch.mock.calls.some(([url]) => url.includes('categoryId=13'))).toBe(true)
    await user.selectOptions(screen.getByLabelText('Categoría'), '')
    await screen.findByRole('heading', { name: 'Bruschetta de tomate' })
    expect(screen.getAllByRole('article')).toHaveLength(7)
  })

  it('agrega, aumenta, disminuye y elimina desde la card manteniendo el resumen sincronizado', async () => {
    const user = userEvent.setup()
    renderPreorder()
    await user.click(await screen.findByRole('button', { name: 'Agregar Hamburguesa artesanal', exact: true }))
    const expectQuantity = (quantity) => {
      expect(screen.getByLabelText('Cantidad de Hamburguesa artesanal en el catálogo')).toHaveTextContent(String(quantity))
      expect(screen.getByLabelText('Cantidad de Hamburguesa artesanal', { exact: true })).toHaveTextContent(String(quantity))
    }
    expectQuantity(1)
    await user.click(screen.getByRole('button', { name: 'Agregar una unidad de Hamburguesa artesanal' }))
    expectQuantity(2)
    await user.click(screen.getByRole('button', { name: 'Quitar una unidad de Hamburguesa artesanal' }))
    expectQuantity(1)
    await user.click(screen.getByRole('button', { name: 'Quitar una unidad de Hamburguesa artesanal' }))
    expect(screen.getByRole('button', { name: 'Agregar Hamburguesa artesanal', exact: true })).toBeInTheDocument()
    expect(screen.getByText('Aún no has agregado productos.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Guardar preorden' })).toBeDisabled()
  })

  it('refleja cambios del resumen en la card y conserva la cantidad al cambiar el filtro', async () => {
    const user = userEvent.setup()
    renderPreorder()
    await user.click(await screen.findByRole('button', { name: 'Agregar Hamburguesa artesanal', exact: true }))
    await user.click(screen.getByRole('button', { name: 'Aumentar Hamburguesa artesanal' }))
    expect(screen.getByLabelText('Cantidad de Hamburguesa artesanal en el catálogo')).toHaveTextContent('2')
    await user.selectOptions(screen.getByLabelText('Categoría'), '13')
    await screen.findByRole('heading', { name: 'Limonada natural' })
    await user.selectOptions(screen.getByLabelText('Categoría'), '')
    expect(await screen.findByLabelText('Cantidad de Hamburguesa artesanal en el catálogo')).toHaveTextContent('2')
    await user.click(screen.getByRole('button', { name: 'Disminuir Hamburguesa artesanal' }))
    expect(screen.getByLabelText('Cantidad de Hamburguesa artesanal en el catálogo')).toHaveTextContent('1')
    await user.click(screen.getByRole('button', { name: 'Disminuir Hamburguesa artesanal' }))
    expect(screen.getByRole('button', { name: 'Agregar Hamburguesa artesanal', exact: true })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Agregar Hamburguesa artesanal', exact: true }))
    await user.click(screen.getByRole('button', { name: 'Eliminar Hamburguesa artesanal' }))
    expect(screen.getByRole('button', { name: 'Agregar Hamburguesa artesanal', exact: true })).toBeInTheDocument()
  })

  it('respeta el máximo de 100 unidades y bloquea controles durante el envío', async () => {
    const add = vi.fn()
    const props = { onAdd: add, onDecrease: vi.fn(), items: [{ productId: 4, quantity: 100 }] }
    const { rerender } = render(<ProductCatalog {...props} />)
    expect(await screen.findByRole('button', { name: 'Agregar una unidad de Hamburguesa artesanal' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Quitar una unidad de Hamburguesa artesanal' })).toBeEnabled()
    rerender(<ProductCatalog {...props} disabled />)
    expect(screen.getByRole('button', { name: 'Quitar una unidad de Hamburguesa artesanal' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Agregar Limonada natural' })).toBeDisabled()
    expect(add).not.toHaveBeenCalled()
  })
})
