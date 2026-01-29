package main

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"testing"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

var cfg Config

// TestMain runs once before all tests
func TestMain(m *testing.M) {
	cfg = LoadConfig()
	m.Run()
}

// Author represents the API response structure
type Author struct {
	ID        int       `json:"id"`
	Name      string    `json:"name"`
	Email     string    `json:"email"`
	CreatedAt time.Time `json:"created_at"`
	Books     []Book    `json:"books,omitempty"`
}

// Book represents a book in the API response
type Book struct {
	ID        int     `json:"id"`
	AuthorID  int     `json:"author_id"`
	Title     string  `json:"title"`
	Published bool    `json:"published"`
	Price     float64 `json:"price"`
}

// CreateAuthorRequest is the request payload for creating an author
type CreateAuthorRequest struct {
	Name  string              `json:"name"`
	Email string              `json:"email"`
	Books []CreateBookRequest `json:"books,omitempty"`
}

// CreateBookRequest is the request payload for creating a book
type CreateBookRequest struct {
	Title     string  `json:"title"`
	Published bool    `json:"published"`
	Price     float64 `json:"price"`
}

// =============================================================================
// GET /authors Tests
// =============================================================================

func TestGetAuthors_ReturnsOK(t *testing.T) {
	resp, err := http.Get(cfg.APIBaseURL + "/authors")
	require.NoError(t, err)
	defer resp.Body.Close()

	assert.Equal(t, http.StatusOK, resp.StatusCode)
	assert.Equal(t, "application/json", resp.Header.Get("Content-Type"))
}

func TestGetAuthors_ReturnsValidJSON(t *testing.T) {
	resp, err := http.Get(cfg.APIBaseURL + "/authors")
	require.NoError(t, err)
	defer resp.Body.Close()

	var authors []Author
	err = json.NewDecoder(resp.Body).Decode(&authors)
	require.NoError(t, err, "response should be valid JSON array of authors")

	// Should return an array (could be empty or have seeded data)
	assert.IsType(t, []Author{}, authors)
}

func TestGetAuthors_ContainsSeededData(t *testing.T) {
	resp, err := http.Get(cfg.APIBaseURL + "/authors")
	require.NoError(t, err)
	defer resp.Body.Close()

	var authors []Author
	err = json.NewDecoder(resp.Body).Decode(&authors)
	require.NoError(t, err)

	// The app seeds data on startup, so we should have at least some authors
	// Skip this assertion if running against a fresh/empty database
	if len(authors) > 0 {
		// Verify structure of first author
		assert.NotEmpty(t, authors[0].ID)
		assert.NotEmpty(t, authors[0].Name)
		assert.NotEmpty(t, authors[0].Email)
	}
}

// =============================================================================
// POST /authors Tests
// =============================================================================

func TestCreateAuthor_ReturnsCreated(t *testing.T) {
	uniqueEmail := fmt.Sprintf("e2e-test-%d@test.com", time.Now().UnixNano())
	payload := CreateAuthorRequest{
		Name:  "E2E Test Author",
		Email: uniqueEmail,
	}
	body, _ := json.Marshal(payload)

	resp, err := http.Post(
		cfg.APIBaseURL+"/authors",
		"application/json",
		bytes.NewReader(body),
	)
	require.NoError(t, err)
	defer resp.Body.Close()

	assert.Equal(t, http.StatusCreated, resp.StatusCode)

	var created Author
	err = json.NewDecoder(resp.Body).Decode(&created)
	require.NoError(t, err)

	assert.NotZero(t, created.ID, "created author should have an ID")
	assert.Equal(t, "E2E Test Author", created.Name)
	assert.Equal(t, uniqueEmail, created.Email)

	// Cleanup
	cleanupAuthor(t, created.ID)
}

func TestCreateAuthor_WritesToDatabase(t *testing.T) {
	uniqueEmail := fmt.Sprintf("e2e-db-test-%d@test.com", time.Now().UnixNano())
	payload := CreateAuthorRequest{
		Name:  "Database Write Test",
		Email: uniqueEmail,
	}
	body, _ := json.Marshal(payload)

	// Act: call the API
	resp, err := http.Post(
		cfg.APIBaseURL+"/authors",
		"application/json",
		bytes.NewReader(body),
	)
	require.NoError(t, err)
	defer resp.Body.Close()
	require.Equal(t, http.StatusCreated, resp.StatusCode)

	var created Author
	json.NewDecoder(resp.Body).Decode(&created)

	// Assert: verify data is actually in the database
	ctx := context.Background()
	conn, err := pgx.Connect(ctx, cfg.DatabaseURL)
	require.NoError(t, err, "should connect to database")
	defer conn.Close(ctx)

	var dbID int
	var dbName, dbEmail string
	err = conn.QueryRow(ctx,
		"SELECT id, name, email FROM authors WHERE email = $1",
		uniqueEmail,
	).Scan(&dbID, &dbName, &dbEmail)

	require.NoError(t, err, "author should exist in database")
	assert.Equal(t, created.ID, dbID)
	assert.Equal(t, "Database Write Test", dbName)
	assert.Equal(t, uniqueEmail, dbEmail)

	// Cleanup
	cleanupAuthor(t, created.ID)
}

func TestCreateAuthorWithBooks_WritesAllToDatabase(t *testing.T) {
	uniqueEmail := fmt.Sprintf("e2e-books-test-%d@test.com", time.Now().UnixNano())
	payload := CreateAuthorRequest{
		Name:  "Author With Books",
		Email: uniqueEmail,
		Books: []CreateBookRequest{
			{Title: "First Book", Published: true, Price: 19.99},
			{Title: "Second Book", Published: false, Price: 29.99},
		},
	}
	body, _ := json.Marshal(payload)

	resp, err := http.Post(
		cfg.APIBaseURL+"/authors",
		"application/json",
		bytes.NewReader(body),
	)
	require.NoError(t, err)
	defer resp.Body.Close()
	require.Equal(t, http.StatusCreated, resp.StatusCode)

	var created Author
	json.NewDecoder(resp.Body).Decode(&created)

	// Verify books are in the database
	ctx := context.Background()
	conn, err := pgx.Connect(ctx, cfg.DatabaseURL)
	require.NoError(t, err)
	defer conn.Close(ctx)

	rows, err := conn.Query(ctx,
		"SELECT title, published, price FROM books WHERE author_id = $1 ORDER BY title",
		created.ID,
	)
	require.NoError(t, err)
	defer rows.Close()

	var books []Book
	for rows.Next() {
		var b Book
		err := rows.Scan(&b.Title, &b.Published, &b.Price)
		require.NoError(t, err)
		books = append(books, b)
	}

	require.Len(t, books, 2, "should have created 2 books")
	assert.Equal(t, "First Book", books[0].Title)
	assert.Equal(t, true, books[0].Published)
	assert.Equal(t, 19.99, books[0].Price)
	assert.Equal(t, "Second Book", books[1].Title)
	assert.Equal(t, false, books[1].Published)

	// Cleanup
	cleanupAuthor(t, created.ID)
}

func TestCreateAuthor_InvalidPayload_ReturnsBadRequest(t *testing.T) {
	resp, err := http.Post(
		cfg.APIBaseURL+"/authors",
		"application/json",
		bytes.NewReader([]byte(`{invalid json`)),
	)
	require.NoError(t, err)
	defer resp.Body.Close()

	assert.Equal(t, http.StatusBadRequest, resp.StatusCode)
}

// =============================================================================
// Round-Trip Tests (Create then Get)
// =============================================================================

func TestCreateThenGet_AuthorAppearsInList(t *testing.T) {
	uniqueEmail := fmt.Sprintf("e2e-roundtrip-%d@test.com", time.Now().UnixNano())
	payload := CreateAuthorRequest{
		Name:  "Roundtrip Test Author",
		Email: uniqueEmail,
	}
	body, _ := json.Marshal(payload)

	// Create author
	resp, err := http.Post(cfg.APIBaseURL+"/authors", "application/json", bytes.NewReader(body))
	require.NoError(t, err)
	require.Equal(t, http.StatusCreated, resp.StatusCode)

	var created Author
	json.NewDecoder(resp.Body).Decode(&created)
	resp.Body.Close()

	// Fetch all authors
	resp, err = http.Get(cfg.APIBaseURL + "/authors")
	require.NoError(t, err)
	defer resp.Body.Close()

	var authors []Author
	json.NewDecoder(resp.Body).Decode(&authors)

	// Find our author
	var found *Author
	for _, a := range authors {
		if a.Email == uniqueEmail {
			found = &a
			break
		}
	}

	require.NotNil(t, found, "created author should appear in GET /authors")
	assert.Equal(t, created.ID, found.ID)
	assert.Equal(t, "Roundtrip Test Author", found.Name)

	// Cleanup
	cleanupAuthor(t, created.ID)
}

func TestCreateAuthorWithBooks_BooksAppearInGetResponse(t *testing.T) {
	uniqueEmail := fmt.Sprintf("e2e-books-roundtrip-%d@test.com", time.Now().UnixNano())
	payload := CreateAuthorRequest{
		Name:  "Author Books Roundtrip",
		Email: uniqueEmail,
		Books: []CreateBookRequest{
			{Title: "Visible Book", Published: true, Price: 15.00},
		},
	}
	body, _ := json.Marshal(payload)

	// Create
	resp, err := http.Post(cfg.APIBaseURL+"/authors", "application/json", bytes.NewReader(body))
	require.NoError(t, err)
	require.Equal(t, http.StatusCreated, resp.StatusCode)

	var created Author
	json.NewDecoder(resp.Body).Decode(&created)
	resp.Body.Close()

	// Get all authors
	resp, err = http.Get(cfg.APIBaseURL + "/authors")
	require.NoError(t, err)
	defer resp.Body.Close()

	var authors []Author
	json.NewDecoder(resp.Body).Decode(&authors)

	// Find our author and check books
	var found *Author
	for _, a := range authors {
		if a.Email == uniqueEmail {
			found = &a
			break
		}
	}

	require.NotNil(t, found)
	require.Len(t, found.Books, 1, "author should have 1 book in GET response")
	assert.Equal(t, "Visible Book", found.Books[0].Title)

	// Cleanup
	cleanupAuthor(t, created.ID)
}

// =============================================================================
// HTTP Method Tests
// =============================================================================

func TestAuthorsEndpoint_UnsupportedMethod_Returns405(t *testing.T) {
	req, _ := http.NewRequest(http.MethodDelete, cfg.APIBaseURL+"/authors", nil)
	client := &http.Client{}
	resp, err := client.Do(req)
	require.NoError(t, err)
	defer resp.Body.Close()

	assert.Equal(t, http.StatusMethodNotAllowed, resp.StatusCode)
}

// =============================================================================
// Helpers
// =============================================================================

func cleanupAuthor(t *testing.T, authorID int) {
	t.Helper()
	ctx := context.Background()
	conn, err := pgx.Connect(ctx, cfg.DatabaseURL)
	if err != nil {
		t.Logf("Warning: could not connect for cleanup: %v", err)
		return
	}
	defer conn.Close(ctx)

	// Books are deleted via CASCADE
	_, err = conn.Exec(ctx, "DELETE FROM authors WHERE id = $1", authorID)
	if err != nil {
		t.Logf("Warning: cleanup failed for author %d: %v", authorID, err)
	}
}
