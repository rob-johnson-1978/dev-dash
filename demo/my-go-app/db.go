package main

import (
	"database/sql"
	"log"
)

var db *sql.DB

func initSchema() error {
	schema := `
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
	`
	_, err := db.Exec(schema)
	return err
}

func seedData() error {
	var count int
	if err := db.QueryRow("SELECT COUNT(*) FROM authors").Scan(&count); err != nil {
		return err
	}
	if count > 0 {
		log.Printf("Database already has %d authors, skipping seed", count)
		return nil
	}

	log.Println("Seeding database with sample data...")

	seedAuthors := []struct {
		name  string
		email string
		books []struct {
			title     string
			published bool
			price     float64
		}
	}{
		{
			name:  "George Orwell",
			email: "george@orwell.com",
			books: []struct {
				title     string
				published bool
				price     float64
			}{
				{"1984", true, 12.99},
				{"Animal Farm", true, 9.99},
				{"Coming Up for Air", false, 14.50},
			},
		},
		{
			name:  "Jane Austen",
			email: "jane@austen.com",
			books: []struct {
				title     string
				published bool
				price     float64
			}{
				{"Pride and Prejudice", true, 11.99},
				{"Emma", true, 10.99},
			},
		},
		{
			name:  "Isaac Asimov",
			email: "isaac@asimov.com",
			books: []struct {
				title     string
				published bool
				price     float64
			}{
				{"Foundation", true, 15.99},
				{"I, Robot", true, 13.99},
				{"The Caves of Steel", true, 12.50},
				{"Nightfall", false, 8.99},
			},
		},
	}

	tx, err := db.Begin()
	if err != nil {
		return err
	}
	defer tx.Rollback()

	for _, a := range seedAuthors {
		var authorID int
		err := tx.QueryRow(
			"INSERT INTO authors (name, email) VALUES ($1, $2) RETURNING id",
			a.name, a.email,
		).Scan(&authorID)
		if err != nil {
			return err
		}

		for _, b := range a.books {
			_, err := tx.Exec(
				"INSERT INTO books (author_id, title, published, price) VALUES ($1, $2, $3, $4)",
				authorID, b.title, b.published, b.price,
			)
			if err != nil {
				return err
			}
		}
	}

	if err := tx.Commit(); err != nil {
		return err
	}

	log.Printf("Seeded %d authors with books", len(seedAuthors))
	return nil
}
