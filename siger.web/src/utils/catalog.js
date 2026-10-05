const preferredCategories = ['entradas', 'platos fuertes', 'bebidas', 'postres']

export function sortCategories(categories) {
  const rank = (name) => {
    const index = preferredCategories.indexOf(name.trim().toLocaleLowerCase('es'))
    return index < 0 ? preferredCategories.length : index
  }
  return [...categories].sort((a, b) => rank(a.name) - rank(b.name)
    || a.name.localeCompare(b.name, 'es') || String(a.id).localeCompare(String(b.id)))
}

export function groupProducts(categories, products) {
  const groups = new Map(categories.map((category) => [category.id, { ...category, products: [] }]))
  for (const product of products) {
    if (!groups.has(product.categoryId)) {
      groups.set(product.categoryId, { id: product.categoryId, name: 'Otras opciones', products: [] })
    }
    groups.get(product.categoryId).products.push(product)
  }
  return sortCategories([...groups.values()]).filter((group) => group.products.length > 0)
}
