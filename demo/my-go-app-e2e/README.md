# my-go-app E2E Tests

Black-box / outside-in tests for the `my-go-app` API.

These tests treat the API as a complete black box — they call real HTTP endpoints and verify both the responses and the actual database state.

## Prerequisites

- Go 1.23+
- The `my-go-app` API running (with its PostgreSQL database)

## Running the Tests

First, ensure the API and database are running:

```bash
cd ../
docker compose up -d
```

Then run the tests:

```bash
# From this directory
go mod tidy
go test -v ./...
```

## Configuration

Tests can be configured via environment variables:

| Variable | Default | Description |
|----------|---------|-------------|
| `API_BASE_URL` | `http://localhost:8080` | Base URL of the running API |
| `DATABASE_URL` | `postgres://postgres:Secret@Postgres@localhost:5432/postgres?sslmode=disable` | Direct database connection for verification |

Example with custom config:

```bash
API_BASE_URL=http://localhost:9000 \
DATABASE_URL=postgres://user:pass@localhost:5432/mydb?sslmode=disable \
go test -v ./...
```

## What These Tests Verify

1. **GET /authors** - Returns valid JSON list of authors with books
2. **POST /authors** - Creates authors (with optional books) and returns correct response
3. **Database writes** - Verifies data is actually persisted to PostgreSQL
4. **Round-trip** - Creates data via POST, then verifies it appears in GET
5. **Error handling** - Invalid payloads return appropriate error codes

## Test Isolation

Each test creates unique data (using timestamps in emails) and cleans up after itself, so tests can run in parallel and repeatedly without conflicts.
