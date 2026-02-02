import os

API_BASE_URL = os.getenv("API_BASE_URL", "http://localhost:8082")
DATABASE_URL = os.getenv(
    "DATABASE_URL",
    "postgres://postgres:Secret%40Postgres@localhost:5432/python-test",
)
