package main

import (
	"encoding/json"
	"log"
	"net/http"
)

func authorsHandler(w http.ResponseWriter, r *http.Request) {
	log.Printf("%s %s %s", r.Method, r.URL.Path, r.RemoteAddr)

	switch r.Method {
	case http.MethodGet:
		getAuthors(w, r)
	case http.MethodPost:
		createAuthor(w, r)
	default:
		http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
	}
}

func getAuthors(w http.ResponseWriter, r *http.Request) {
	rows, err := db.Query("SELECT id, name, email, created_at FROM authors ORDER BY id")
	if err != nil {
		log.Printf("Error querying authors: %v", err)
		http.Error(w, "Internal server error", http.StatusInternalServerError)
		return
	}
	defer rows.Close()

	var authors []Author
	for rows.Next() {
		var a Author
		if err := rows.Scan(&a.ID, &a.Name, &a.Email, &a.CreatedAt); err != nil {
			log.Printf("Error scanning author: %v", err)
			http.Error(w, "Internal server error", http.StatusInternalServerError)
			return
		}

		books, err := getBooksByAuthor(a.ID)
		if err != nil {
			log.Printf("Error getting books for author %d: %v", a.ID, err)
			http.Error(w, "Internal server error", http.StatusInternalServerError)
			return
		}
		a.Books = books
		authors = append(authors, a)
	}

	if authors == nil {
		authors = []Author{}
	}

	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(http.StatusOK)
	json.NewEncoder(w).Encode(authors)
	log.Printf("Returned %d authors", len(authors))
}

func getBooksByAuthor(authorID int) ([]Book, error) {
	rows, err := db.Query("SELECT id, author_id, title, published, price FROM books WHERE author_id = $1", authorID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var books []Book
	for rows.Next() {
		var b Book
		if err := rows.Scan(&b.ID, &b.AuthorID, &b.Title, &b.Published, &b.Price); err != nil {
			return nil, err
		}
		books = append(books, b)
	}

	if books == nil {
		books = []Book{}
	}
	return books, nil
}

func createAuthor(w http.ResponseWriter, r *http.Request) {
	var req CreateAuthorRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		log.Printf("Error decoding request: %v", err)
		http.Error(w, "Bad request", http.StatusBadRequest)
		return
	}

	tx, err := db.Begin()
	if err != nil {
		log.Printf("Error starting transaction: %v", err)
		http.Error(w, "Internal server error", http.StatusInternalServerError)
		return
	}
	defer tx.Rollback()

	var author Author
	err = tx.QueryRow(
		"INSERT INTO authors (name, email) VALUES ($1, $2) RETURNING id, name, email, created_at",
		req.Name, req.Email,
	).Scan(&author.ID, &author.Name, &author.Email, &author.CreatedAt)
	if err != nil {
		log.Printf("Error inserting author: %v", err)
		http.Error(w, "Internal server error", http.StatusInternalServerError)
		return
	}

	for _, bookReq := range req.Books {
		var book Book
		err = tx.QueryRow(
			"INSERT INTO books (author_id, title, published, price) VALUES ($1, $2, $3, $4) RETURNING id, author_id, title, published, price",
			author.ID, bookReq.Title, bookReq.Published, bookReq.Price,
		).Scan(&book.ID, &book.AuthorID, &book.Title, &book.Published, &book.Price)
		if err != nil {
			log.Printf("Error inserting book: %v", err)
			http.Error(w, "Internal server error", http.StatusInternalServerError)
			return
		}
		author.Books = append(author.Books, book)
	}

	if err := tx.Commit(); err != nil {
		log.Printf("Error committing transaction: %v", err)
		http.Error(w, "Internal server error", http.StatusInternalServerError)
		return
	}

	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(http.StatusCreated)
	json.NewEncoder(w).Encode(author)
	log.Printf("Created author %d with %d books", author.ID, len(author.Books))
}
