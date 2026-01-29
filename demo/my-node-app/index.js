import http from 'http';
import { initDatabase, closeDatabase } from './db.js';
import { handleAuthors } from './handlers.js';

const PORT = process.env.PORT || 8081;

async function main() {
    console.log('Starting server...');
    
    // Initialize database (creates DB if not exists, runs migrations, seeds data)
    await initDatabase();
    
    const server = http.createServer(async (req, res) => {
        const url = new URL(req.url, `http://${req.headers.host}`);
        console.log(`${req.method} ${url.pathname} ${req.socket.remoteAddress}`);
        
        if (url.pathname === '/authors') {
            await handleAuthors(req, res);
        } else {
            res.writeHead(404, { 'Content-Type': 'text/plain' });
            res.end('Not Found');
        }
    });
    
    server.listen(PORT, () => {
        console.log(`Server starting on port ${PORT}`);
    });
    
    // Graceful shutdown
    process.on('SIGINT', async () => {
        console.log('Shutting down...');
        await closeDatabase();
        process.exit(0);
    });
    
    process.on('SIGTERM', async () => {
        console.log('Shutting down...');
        await closeDatabase();
        process.exit(0);
    });
}

main().catch(err => {
    console.error('Failed to start server:', err);
    process.exit(1);
});
