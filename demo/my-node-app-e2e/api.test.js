import { describe, it, before, after } from 'node:test';
import assert from 'node:assert';
import { config } from './config.js';
import pg from 'pg';

const { Client } = pg;

/**
 * Helper to make HTTP requests
 */
async function request(method, path, body = null) {
    const url = `${config.apiBaseUrl}${path}`;
    const options = {
        method,
        headers: {
            'Content-Type': 'application/json'
        }
    };
    
    if (body) {
        options.body = JSON.stringify(body);
    }
    
    const response = await fetch(url, options);
    return response;
}

/**
 * Helper to cleanup test authors from database
 */
async function cleanupAuthor(authorId) {
    const client = new Client({ connectionString: config.databaseUrl });
    try {
        await client.connect();
        // Books are deleted via CASCADE
        await client.query('DELETE FROM authors WHERE id = $1', [authorId]);
    } catch (err) {
        console.warn(`Warning: cleanup failed for author ${authorId}:`, err.message);
    } finally {
        await client.end();
    }
}

/**
 * Generate unique email for test isolation
 */
function uniqueEmail(prefix = 'e2e-test') {
    return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2)}@test.com`;
}

// =============================================================================
// GET /authors Tests
// =============================================================================

describe('GET /authors', () => {
    it('should return 200 OK', async () => {
        const res = await request('GET', '/authors');
        assert.strictEqual(res.status, 200);
    });
    
    it('should return application/json content type', async () => {
        const res = await request('GET', '/authors');
        assert.strictEqual(res.headers.get('Content-Type'), 'application/json');
    });
    
    it('should return valid JSON array', async () => {
        const res = await request('GET', '/authors');
        const data = await res.json();
        assert.ok(Array.isArray(data), 'response should be an array');
    });
    
    it('should return authors with correct structure', async () => {
        const res = await request('GET', '/authors');
        const authors = await res.json();
        
        // If there are seeded authors, check structure
        if (authors.length > 0) {
            const author = authors[0];
            assert.ok('id' in author, 'author should have id');
            assert.ok('name' in author, 'author should have name');
            assert.ok('email' in author, 'author should have email');
            assert.ok('books' in author, 'author should have books');
            assert.ok(Array.isArray(author.books), 'books should be an array');
        }
    });
});

// =============================================================================
// POST /authors Tests
// =============================================================================

describe('POST /authors', () => {
    it('should return 201 Created', async () => {
        const email = uniqueEmail();
        const res = await request('POST', '/authors', {
            name: 'E2E Test Author',
            email
        });
        
        assert.strictEqual(res.status, 201);
        
        const created = await res.json();
        await cleanupAuthor(created.id);
    });
    
    it('should return created author with ID', async () => {
        const email = uniqueEmail();
        const res = await request('POST', '/authors', {
            name: 'E2E Test Author',
            email
        });
        
        const created = await res.json();
        
        assert.ok(created.id, 'created author should have an ID');
        assert.strictEqual(created.name, 'E2E Test Author');
        assert.strictEqual(created.email, email);
        
        await cleanupAuthor(created.id);
    });
    
    it('should write author to database', async () => {
        const email = uniqueEmail('e2e-db-test');
        const res = await request('POST', '/authors', {
            name: 'Database Write Test',
            email
        });
        
        const created = await res.json();
        assert.strictEqual(res.status, 201);
        
        // Verify data is actually in the database
        const client = new Client({ connectionString: config.databaseUrl });
        try {
            await client.connect();
            const result = await client.query(
                'SELECT id, name, email FROM authors WHERE email = $1',
                [email]
            );
            
            assert.strictEqual(result.rows.length, 1, 'author should exist in database');
            assert.strictEqual(result.rows[0].id, created.id);
            assert.strictEqual(result.rows[0].name, 'Database Write Test');
            assert.strictEqual(result.rows[0].email, email);
        } finally {
            await client.end();
        }
        
        await cleanupAuthor(created.id);
    });
    
    it('should create author with books and write all to database', async () => {
        const email = uniqueEmail('e2e-books-test');
        const res = await request('POST', '/authors', {
            name: 'Author With Books',
            email,
            books: [
                { title: 'First Book', published: true, price: 19.99 },
                { title: 'Second Book', published: false, price: 29.99 }
            ]
        });
        
        const created = await res.json();
        assert.strictEqual(res.status, 201);
        
        // Verify books are in the database
        const client = new Client({ connectionString: config.databaseUrl });
        try {
            await client.connect();
            const result = await client.query(
                'SELECT title, published, price FROM books WHERE author_id = $1 ORDER BY title',
                [created.id]
            );
            
            assert.strictEqual(result.rows.length, 2, 'should have created 2 books');
            assert.strictEqual(result.rows[0].title, 'First Book');
            assert.strictEqual(result.rows[0].published, true);
            assert.strictEqual(parseFloat(result.rows[0].price), 19.99);
            assert.strictEqual(result.rows[1].title, 'Second Book');
            assert.strictEqual(result.rows[1].published, false);
        } finally {
            await client.end();
        }
        
        await cleanupAuthor(created.id);
    });
    
    it('should return 400 for invalid JSON payload', async () => {
        const res = await fetch(`${config.apiBaseUrl}/authors`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: '{invalid json'
        });
        
        assert.strictEqual(res.status, 400);
    });
});

// =============================================================================
// Round-Trip Tests (Create then Get)
// =============================================================================

describe('Round-trip tests', () => {
    it('should show created author in GET /authors', async () => {
        const email = uniqueEmail('e2e-roundtrip');
        
        // Create author
        const createRes = await request('POST', '/authors', {
            name: 'Roundtrip Test Author',
            email
        });
        const created = await createRes.json();
        assert.strictEqual(createRes.status, 201);
        
        // Fetch all authors
        const getRes = await request('GET', '/authors');
        const authors = await getRes.json();
        
        // Find our author
        const found = authors.find(a => a.email === email);
        
        assert.ok(found, 'created author should appear in GET /authors');
        assert.strictEqual(found.id, created.id);
        assert.strictEqual(found.name, 'Roundtrip Test Author');
        
        await cleanupAuthor(created.id);
    });
    
    it('should show created books in GET /authors response', async () => {
        const email = uniqueEmail('e2e-books-roundtrip');
        
        // Create author with books
        const createRes = await request('POST', '/authors', {
            name: 'Author Books Roundtrip',
            email,
            books: [
                { title: 'Visible Book', published: true, price: 15.00 }
            ]
        });
        const created = await createRes.json();
        assert.strictEqual(createRes.status, 201);
        
        // Fetch all authors
        const getRes = await request('GET', '/authors');
        const authors = await getRes.json();
        
        // Find our author and check books
        const found = authors.find(a => a.email === email);
        
        assert.ok(found, 'created author should appear in GET /authors');
        assert.strictEqual(found.books.length, 1, 'author should have 1 book in GET response');
        assert.strictEqual(found.books[0].title, 'Visible Book');
        
        await cleanupAuthor(created.id);
    });
});

// =============================================================================
// HTTP Method Tests
// =============================================================================

describe('HTTP methods', () => {
    it('should return 405 for DELETE method', async () => {
        const res = await fetch(`${config.apiBaseUrl}/authors`, {
            method: 'DELETE'
        });
        
        assert.strictEqual(res.status, 405);
    });
    
    it('should return 405 for PUT method', async () => {
        const res = await fetch(`${config.apiBaseUrl}/authors`, {
            method: 'PUT'
        });
        
        assert.strictEqual(res.status, 405);
    });
});
