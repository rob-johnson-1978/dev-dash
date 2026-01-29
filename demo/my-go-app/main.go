package main

import (
	"database/sql"
	"log"
	"net/http"
	"os"

	_ "github.com/lib/pq"
)

func main() {
	log.SetOutput(os.Stdout)
	log.SetFlags(log.LstdFlags | log.Lmicroseconds)

	dbName := getEnv("DB_NAME", "go-test")
	baseConnStr := getEnv("DATABASE_URL", "postgres://postgres:Secret@Postgres@localhost:5432/postgres?sslmode=disable")

	// Ensure the target database exists
	if err := ensureDatabase(baseConnStr, dbName); err != nil {
		log.Fatalf("Failed to ensure database exists: %v", err)
	}

	// Connect to the target database
	connStr := getEnv("DATABASE_URL", "postgres://postgres:Secret@Postgres@localhost:5432/"+dbName+"?sslmode=disable")

	var err error
	db, err = sql.Open("postgres", connStr)
	if err != nil {
		log.Fatalf("Failed to connect to database: %v", err)
	}
	defer db.Close()

	if err := db.Ping(); err != nil {
		log.Fatalf("Failed to ping database: %v", err)
	}
	log.Printf("Connected to database: %s", dbName)

	if err := initSchema(); err != nil {
		log.Fatalf("Failed to initialize schema: %v", err)
	}
	log.Println("Schema initialized")

	if err := seedData(); err != nil {
		log.Fatalf("Failed to seed data: %v", err)
	}

	http.HandleFunc("/authors", authorsHandler)

	port := getEnv("PORT", "8080")
	log.Printf("Server starting on port %s", port)
	if err := http.ListenAndServe(":"+port, nil); err != nil {
		log.Fatalf("Server failed: %v", err)
	}
}

func getEnv(key, fallback string) string {
	if value, ok := os.LookupEnv(key); ok {
		return value
	}
	return fallback
}
