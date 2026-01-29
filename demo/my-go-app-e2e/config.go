package main

import "os"

// Config holds the configuration for E2E tests
type Config struct {
	APIBaseURL  string
	DatabaseURL string
}

// LoadConfig loads configuration from environment variables with sensible defaults
func LoadConfig() Config {
	return Config{
		APIBaseURL:  getEnv("API_BASE_URL", "http://localhost:8080"),
		DatabaseURL: getEnv("DATABASE_URL", "postgres://postgres:Secret@Postgres@localhost:5432/go-test?sslmode=disable"),
	}
}

func getEnv(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}
