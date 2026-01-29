import pg from 'pg';

const { Pool, Client } = pg;

const DB_NAME = process.env.DB_NAME || 'node-test';
const BASE_DATABASE_URL = process.env.DATABASE_URL || 'postgres://postgres:Secret@Postgres@localhost:5432/postgres';

let pool = null;

/**
 * Ensures the target database exists, creating it if necessary
 */
async function ensureDatabase() {
    const client = new Client({ connectionString: BASE_DATABASE_URL });
    
    try {
        await client.connect();
        
        // Check if database exists
        const result = await client.query(
            'SELECT EXISTS(SELECT 1 FROM pg_database WHERE datname = $1)',
            [DB_NAME]
        );
        
        if (!result.rows[0].exists) {
            // Create database (can't use parameterized query for CREATE DATABASE)
            // Simple validation to prevent SQL injection
            if (/[;"']/.test(DB_NAME)) {
                throw new Error(`Invalid database name: ${DB_NAME}`);
            }
            await client.query(`CREATE DATABASE "${DB_NAME}"`);
            console.log(`Created database: ${DB_NAME}`);
        } else {
            console.log(`Database already exists: ${DB_NAME}`);
        }
    } finally {
        await client.end();
    }
}

/**
 * Initializes the database connection pool and schema
 */
export async function initDatabase() {
    // First ensure the database exists
    await ensureDatabase();
    
    // Connect to our target database
    const targetDbUrl = process.env.DATABASE_URL || `postgres://postgres:Secret@Postgres@localhost:5432/${DB_NAME}`;
    pool = new Pool({ connectionString: targetDbUrl });
    
    // Test connection
    const client = await pool.connect();
    try {
        await client.query('SELECT 1');
        console.log(`Connected to database: ${DB_NAME}`);
    } finally {
        client.release();
    }
    
    // Initialize schema
    await initSchema();
    console.log('Schema initialized');
    
    // Seed data
    await seedData();
}

/**
 * Creates tables if they don't exist
 */
async function initSchema() {
    const schema = `
        CREATE TABLE IF NOT EXISTS authors (
            id SERIAL PRIMARY KEY,
            name VARCHAR(255) NOT NULL,
            email VARCHAR(255) NOT NULL,
            created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS books (
            id SERIAL PRIMARY KEY,
            author_id INTEGER REFERENCES authors(id) ON DELETE CASCADE,
            title VARCHAR(255) NOT NULL,
            published BOOLEAN DEFAULT false,
            price DECIMAL(10,2) DEFAULT 0.00
        );
    `;
    await pool.query(schema);
}

/**
 * Seeds the database with sample data
 */
async function seedData() {
    const countResult = await pool.query('SELECT COUNT(*) FROM authors');
    const count = parseInt(countResult.rows[0].count, 10);
    
    if (count > 0) {
        console.log(`Database already has ${count} authors, skipping seed`);
        return;
    }
    
    console.log('Seeding database with sample data...');
    
    const seedAuthors = [
        {
            name: 'George Orwell',
            email: 'george@orwell.com',
            books: [
                { title: '1984', published: true, price: 12.99 },
                { title: 'Animal Farm', published: true, price: 9.99 },
                { title: 'Coming Up for Air', published: false, price: 14.50 },
            ]
        },
        {
            name: 'Jane Austen',
            email: 'jane@austen.com',
            books: [
                { title: 'Pride and Prejudice', published: true, price: 11.99 },
                { title: 'Emma', published: true, price: 10.99 },
            ]
        },
        {
            name: 'Isaac Asimov',
            email: 'isaac@asimov.com',
            books: [
                { title: 'Foundation', published: true, price: 15.99 },
                { title: 'I, Robot', published: true, price: 13.99 },
                { title: 'The Caves of Steel', published: true, price: 12.50 },
                { title: 'Nightfall', published: false, price: 8.99 },
            ]
        }
    ];
    
    const client = await pool.connect();
    try {
        await client.query('BEGIN');
        
        for (const author of seedAuthors) {
            const authorResult = await client.query(
                'INSERT INTO authors (name, email) VALUES ($1, $2) RETURNING id',
                [author.name, author.email]
            );
            const authorId = authorResult.rows[0].id;
            
            for (const book of author.books) {
                await client.query(
                    'INSERT INTO books (author_id, title, published, price) VALUES ($1, $2, $3, $4)',
                    [authorId, book.title, book.published, book.price]
                );
            }
        }
        
        await client.query('COMMIT');
        console.log(`Seeded ${seedAuthors.length} authors with books`);
    } catch (err) {
        await client.query('ROLLBACK');
        throw err;
    } finally {
        client.release();
    }
}

/**
 * Gets the database pool
 */
export function getPool() {
    return pool;
}

/**
 * Closes the database connection
 */
export async function closeDatabase() {
    if (pool) {
        await pool.end();
        console.log('Database connection closed');
    }
}
