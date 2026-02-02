import random
import time

import psycopg
import pytest
import requests

from config import API_BASE_URL, DATABASE_URL


def _request(method: str, path: str, json=None, data=None):
    url = f"{API_BASE_URL}{path}"
    return requests.request(method, url, json=json, data=data, headers={"Content-Type": "application/json"})


def _cleanup_author(author_id: int) -> None:
    try:
        with psycopg.connect(DATABASE_URL) as conn:
            with conn.cursor() as cur:
                cur.execute("DELETE FROM authors WHERE id = %s", (author_id,))
    except Exception as exc:  # pragma: no cover - best-effort cleanup
        print(f"Warning: cleanup failed for author {author_id}: {exc}")


def _unique_email(prefix: str = "e2e-test") -> str:
    suffix = f"{int(time.time() * 1000)}-{random.randint(1000, 9999)}"
    return f"{prefix}-{suffix}@test.com"


# =============================================================================
# GET /authors
# =============================================================================


def test_get_authors_returns_200():
    res = _request("GET", "/authors")
    assert res.status_code == 200


def test_get_authors_returns_json():
    res = _request("GET", "/authors")
    assert res.headers.get("Content-Type") == "application/json"


def test_get_authors_returns_array():
    res = _request("GET", "/authors")
    data = res.json()
    assert isinstance(data, list)


def test_get_authors_structure():
    res = _request("GET", "/authors")
    authors = res.json()
    if authors:
        author = authors[0]
        assert "id" in author
        assert "name" in author
        assert "email" in author
        assert "books" in author
        assert isinstance(author["books"], list)


# =============================================================================
# POST /authors
# =============================================================================


def test_create_author_returns_201():
    email = _unique_email()
    res = _request("POST", "/authors", json={"name": "E2E Test Author", "email": email})
    assert res.status_code == 201
    created = res.json()
    _cleanup_author(created["id"])


def test_create_author_returns_created_author():
    email = _unique_email()
    res = _request("POST", "/authors", json={"name": "E2E Test Author", "email": email})
    created = res.json()

    assert created.get("id")
    assert created["name"] == "E2E Test Author"
    assert created["email"] == email

    _cleanup_author(created["id"])


def test_create_author_writes_to_database():
    email = _unique_email("e2e-db-test")
    res = _request("POST", "/authors", json={"name": "Database Write Test", "email": email})
    created = res.json()
    assert res.status_code == 201

    with psycopg.connect(DATABASE_URL) as conn:
        with conn.cursor() as cur:
            cur.execute("SELECT id, name, email FROM authors WHERE email = %s", (email,))
            row = cur.fetchone()
            assert row is not None
            assert row[0] == created["id"]
            assert row[1] == "Database Write Test"
            assert row[2] == email

    _cleanup_author(created["id"])


def test_create_author_with_books_writes_all_to_database():
    email = _unique_email("e2e-books-test")
    payload = {
        "name": "Author With Books",
        "email": email,
        "books": [
            {"title": "First Book", "published": True, "price": 19.99},
            {"title": "Second Book", "published": False, "price": 29.99},
        ],
    }

    res = _request("POST", "/authors", json=payload)
    created = res.json()
    assert res.status_code == 201

    with psycopg.connect(DATABASE_URL) as conn:
        with conn.cursor() as cur:
            cur.execute(
                "SELECT title, published, price FROM books WHERE author_id = %s ORDER BY title",
                (created["id"],),
            )
            rows = cur.fetchall()

    assert len(rows) == 2
    assert rows[0][0] == "First Book"
    assert rows[0][1] is True
    assert float(rows[0][2]) == 19.99
    assert rows[1][0] == "Second Book"
    assert rows[1][1] is False

    _cleanup_author(created["id"])


def test_create_author_invalid_json_returns_400():
    res = _request("POST", "/authors", data="{invalid json")
    assert res.status_code == 400


# =============================================================================
# Round-trip tests
# =============================================================================


def test_roundtrip_author_appears_in_get():
    email = _unique_email("e2e-roundtrip")
    res = _request("POST", "/authors", json={"name": "Roundtrip Test Author", "email": email})
    created = res.json()
    assert res.status_code == 201

    res = _request("GET", "/authors")
    authors = res.json()
    found = next((a for a in authors if a.get("email") == email), None)

    assert found is not None
    assert found["id"] == created["id"]
    assert found["name"] == "Roundtrip Test Author"

    _cleanup_author(created["id"])


def test_roundtrip_books_appear_in_get_response():
    email = _unique_email("e2e-books-roundtrip")
    res = _request(
        "POST",
        "/authors",
        json={
            "name": "Author Books Roundtrip",
            "email": email,
            "books": [{"title": "Visible Book", "published": True, "price": 15.0}],
        },
    )
    created = res.json()
    assert res.status_code == 201

    res = _request("GET", "/authors")
    authors = res.json()
    found = next((a for a in authors if a.get("email") == email), None)

    assert found is not None
    assert len(found.get("books", [])) == 1
    assert found["books"][0]["title"] == "Visible Book"

    _cleanup_author(created["id"])


# =============================================================================
# HTTP methods
# =============================================================================


def test_delete_method_returns_405():
    res = _request("DELETE", "/authors")
    assert res.status_code == 405


def test_put_method_returns_405():
    res = _request("PUT", "/authors")
    assert res.status_code == 405
