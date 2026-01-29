import { getPool } from './db.js';

/**
 * Main router for /authors endpoint
 */
export async function handleAuthors(req, res) {
    switch (req.method) {
        case 'GET':
            await getAuthors(req, res);
            break;
        case 'POST':
            await createAuthor(req, res);
            break;
        default:
            res.writeHead(405, { 'Content-Type': 'text/plain' });
            res.end('Method not allowed');
    }
}

/**
 * GET /authors - Returns all authors with their books
 */
async function getAuthors(req, res) {
    const pool = getPool();
    
    try {
        // Get all authors
        const authorsResult = await pool.query(
            'SELECT id, name, email, created_at FROM authors ORDER BY id'
        );
        
        const authors = [];
        for (const row of authorsResult.rows) {
            // Get books for each author
            const booksResult = await pool.query(
                'SELECT id, author_id, title, published, price FROM books WHERE author_id = $1',
                [row.id]
            );
            
            authors.push({
                id: row.id,
                name: row.name,
                email: row.email,
                created_at: row.created_at,
                books: booksResult.rows.map(b => ({
                    id: b.id,
                    author_id: b.author_id,
                    title: b.title,
                    published: b.published,
                    price: parseFloat(b.price)
                }))
            });
        }
        
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify(authors));
        console.log(`Returned ${authors.length} authors`);
    } catch (err) {
        console.error('Error querying authors:', err);
        res.writeHead(500, { 'Content-Type': 'text/plain' });
        res.end('Internal server error');
    }
}

/**
 * POST /authors - Creates a new author with optional books
 */
async function createAuthor(req, res) {
    const pool = getPool();
    
    try {
        // Parse request body
        const body = await parseJsonBody(req);
        if (!body) {
            res.writeHead(400, { 'Content-Type': 'text/plain' });
            res.end('Bad request');
            return;
        }
        
        const { name, email, books = [] } = body;
        
        const client = await pool.connect();
        try {
            await client.query('BEGIN');
            
            // Insert author
            const authorResult = await client.query(
                'INSERT INTO authors (name, email) VALUES ($1, $2) RETURNING id, name, email, created_at',
                [name, email]
            );
            
            const author = {
                id: authorResult.rows[0].id,
                name: authorResult.rows[0].name,
                email: authorResult.rows[0].email,
                created_at: authorResult.rows[0].created_at,
                books: []
            };
            
            // Insert books
            for (const bookReq of books) {
                const bookResult = await client.query(
                    'INSERT INTO books (author_id, title, published, price) VALUES ($1, $2, $3, $4) RETURNING id, author_id, title, published, price',
                    [author.id, bookReq.title, bookReq.published || false, bookReq.price || 0]
                );
                
                const book = bookResult.rows[0];
                author.books.push({
                    id: book.id,
                    author_id: book.author_id,
                    title: book.title,
                    published: book.published,
                    price: parseFloat(book.price)
                });
            }
            
            await client.query('COMMIT');
            
            res.writeHead(201, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify(author));
            console.log(`Created author ${author.id} with ${author.books.length} books`);
        } catch (err) {
            await client.query('ROLLBACK');
            throw err;
        } finally {
            client.release();
        }
    } catch (err) {
        console.error('Error creating author:', err);
        res.writeHead(500, { 'Content-Type': 'text/plain' });
        res.end('Internal server error');
    }
}

/**
 * Parses JSON body from request
 */
function parseJsonBody(req) {
    return new Promise((resolve) => {
        let body = '';
        req.on('data', chunk => {
            body += chunk.toString();
        });
        req.on('end', () => {
            try {
                resolve(JSON.parse(body));
            } catch {
                console.error('Error parsing JSON body');
                resolve(null);
            }
        });
        req.on('error', () => {
            resolve(null);
        });
    });
}
