import os
import re
from decimal import Decimal
from typing import Optional

import psycopg
from psycopg.rows import dict_row
from psycopg_pool import ConnectionPool

DB_NAME = os.getenv("DB_NAME", "python-test")
BASE_DATABASE_URL = os.getenv(
    "BASE_DATABASE_URL",
    "postgres://postgres:Secret%40Postgres@localhost:5432/postgres",
)
TARGET_DATABASE_URL = os.getenv(
    "DATABASE_URL",
    f"postgres://postgres:Secret%40Postgres@localhost:5432/{DB_NAME}",
)
_pool: Optional[ConnectionPool] = None


def ensure_database() -> None:
    """Create the target database if it does not already exist."""
    with psycopg.connect(BASE_DATABASE_URL, autocommit=True) as conn:
        with conn.cursor() as cur:
            cur.execute(
                "SELECT EXISTS(SELECT 1 FROM pg_database WHERE datname = %s)",
                (DB_NAME,),
            )
            exists = cur.fetchone()[0]

            if exists:
                print(f"Database already exists: {DB_NAME}")
                return

            if re.search(r'[;"\']', DB_NAME):
                raise ValueError(f"Invalid database name: {DB_NAME}")

            cur.execute(f'CREATE DATABASE "{DB_NAME}"')
            print(f"Created database: {DB_NAME}")


def init_database() -> None:
    """Initialize pool, schema, and seed data."""
    global _pool

    ensure_database()

    _pool = ConnectionPool(conninfo=TARGET_DATABASE_URL, min_size=1, max_size=10)

    with _pool.connection() as conn:
        with conn.cursor() as cur:
            cur.execute("SELECT 1")
        print(f"Connected to database: {DB_NAME}")

    init_schema()
    seed_data()


def init_schema() -> None:
    schema = """
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
    """

    with _pool.connection() as conn:
        with conn.transaction():
            with conn.cursor() as cur:
                cur.execute(schema)
    print("Schema initialized")


def seed_data() -> None:
    with _pool.connection() as conn:
        with conn.transaction():
            with conn.cursor() as cur:
                cur.execute("SELECT COUNT(*) FROM authors")
                count = cur.fetchone()[0]
                if count > 0:
                    print(f"Database already has {count} authors, skipping seed")
                    return

                authors = [
                    {
                        "name": "George Orwell",
                        "email": "george@orwell.com",
                        "books": [
                            {"title": "1984", "published": True, "price": 12.99},
                            {"title": "Animal Farm", "published": True, "price": 9.99},
                            {
                                "title": "Coming Up for Air",
                                "published": False,
                                "price": 14.50,
                            },
                        ],
                    },
                    {
                        "name": "Jane Austen",
                        "email": "jane@austen.com",
                        "books": [
                            {"title": "Pride and Prejudice", "published": True, "price": 11.99},
                            {"title": "Emma", "published": True, "price": 10.99},
                        ],
                    },
                    {
                        "name": "Isaac Asimov",
                        "email": "isaac@asimov.com",
                        "books": [
                            {"title": "Foundation", "published": True, "price": 15.99},
                            {"title": "I, Robot", "published": True, "price": 13.99},
                            {
                                "title": "The Caves of Steel",
                                "published": True,
                                "price": 12.50,
                            },
                            {"title": "Nightfall", "published": False, "price": 8.99},
                        ],
                    },
                ]

                for author in authors:
                    cur.execute(
                        "INSERT INTO authors (name, email) VALUES (%s, %s) RETURNING id",
                        (author["name"], author["email"]),
                    )
                    author_id = cur.fetchone()[0]

                    for book in author["books"]:
                        cur.execute(
                            """
                            INSERT INTO books (author_id, title, published, price)
                            VALUES (%s, %s, %s, %s)
                            """,
                            (author_id, book["title"], book["published"], book["price"]),
                        )

    print(f"Seeded {len(authors)} authors with books")


def get_pool() -> ConnectionPool:
    if _pool is None:
        raise RuntimeError("Database pool is not initialized")
    return _pool


def close_pool() -> None:
    global _pool
    if _pool is not None:
        _pool.close()
        _pool = None
        print("Database connection pool closed")


def _as_float(value):
    if isinstance(value, Decimal):
        return float(value)
    return value


def fetch_authors_with_books():
    with get_pool().connection() as conn:
        with conn.cursor(row_factory=dict_row) as cur:
            cur.execute("SELECT id, name, email, created_at FROM authors ORDER BY id")
            authors = cur.fetchall()

            for author in authors:
                cur.execute(
                    """
                    SELECT id, author_id, title, published, price
                    FROM books
                    WHERE author_id = %s
                    ORDER BY id
                    """,
                    (author["id"],),
                )
                books = cur.fetchall()
                for book in books:
                    book["price"] = _as_float(book["price"])
                author["books"] = books

            return authors


def create_author_with_books(name: str, email: str, books: list) -> dict:
    with get_pool().connection() as conn:
        with conn.transaction():
            with conn.cursor(row_factory=dict_row) as cur:
                cur.execute(
                    """
                    INSERT INTO authors (name, email)
                    VALUES (%s, %s)
                    RETURNING id, name, email, created_at
                    """,
                    (name, email),
                )
                author = cur.fetchone()
                author_books = []

                for book_req in books:
                    cur.execute(
                        """
                        INSERT INTO books (author_id, title, published, price)
                        VALUES (%s, %s, %s, %s)
                        RETURNING id, author_id, title, published, price
                        """,
                        (
                            author["id"],
                            book_req.get("title"),
                            bool(book_req.get("published", False)),
                            float(book_req.get("price", 0)),
                        ),
                    )
                    book = cur.fetchone()
                    book["price"] = _as_float(book["price"])
                    author_books.append(book)

                author["books"] = author_books
                return author
