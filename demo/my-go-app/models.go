package main

import "time"

type Author struct {
	ID        int       `json:"id"`
	Name      string    `json:"name"`
	Email     string    `json:"email"`
	CreatedAt time.Time `json:"created_at"`
	Books     []Book    `json:"books,omitempty"`
}

type Book struct {
	ID        int     `json:"id"`
	AuthorID  int     `json:"author_id"`
	Title     string  `json:"title"`
	Published bool    `json:"published"`
	Price     float64 `json:"price"`
}

type CreateAuthorRequest struct {
	Name  string              `json:"name"`
	Email string              `json:"email"`
	Books []CreateBookRequest `json:"books"`
}

type CreateBookRequest struct {
	Title     string  `json:"title"`
	Published bool    `json:"published"`
	Price     float64 `json:"price"`
}
