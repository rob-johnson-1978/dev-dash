/**
 * @typedef {Object} Author
 * @property {number} id
 * @property {string} name
 * @property {string} email
 * @property {Date} created_at
 * @property {Book[]} [books]
 */

/**
 * @typedef {Object} Book
 * @property {number} id
 * @property {number} author_id
 * @property {string} title
 * @property {boolean} published
 * @property {number} price
 */

/**
 * @typedef {Object} CreateAuthorRequest
 * @property {string} name
 * @property {string} email
 * @property {CreateBookRequest[]} [books]
 */

/**
 * @typedef {Object} CreateBookRequest
 * @property {string} title
 * @property {boolean} [published]
 * @property {number} [price]
 */

export {};
