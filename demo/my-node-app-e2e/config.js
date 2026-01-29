/**
 * Configuration for E2E tests loaded from environment variables
 */
export const config = {
    apiBaseUrl: process.env.API_BASE_URL || 'http://localhost:8081',
    databaseUrl: process.env.DATABASE_URL || 'postgres://postgres:Secret@Postgres@localhost:5432/node-test'
};
