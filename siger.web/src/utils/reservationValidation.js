export const partySizeLimits = { min: 1, max: 12 }

export const sanitizePhone = (value) => value.replace(/[^0-9]/g, '').slice(0, 15)

export function phoneError(value) {
  return /^[0-9]{10,15}$/.test(value) ? '' : 'Ingresa un número de teléfono válido.'
}

export function emailError(value) {
  const email = value.trim()
  return !email || /^[a-z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)+$/i.test(email)
    ? '' : 'Ingresa un correo electrónico válido.'
}
