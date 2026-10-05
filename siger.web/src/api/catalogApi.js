import { request } from './http'

export const getCategories = (signal) => request('/categories', { signal })

export function getProducts(categoryId, pageNumber, signal) {
  const query = new URLSearchParams({ pageNumber, pageSize: 12 })
  if (categoryId) query.set('categoryId', categoryId)
  return request(`/products?${query}`, { signal })
}
