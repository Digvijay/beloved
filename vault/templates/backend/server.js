const express = require('express');
const cors = require('cors');
const helmet = require('helmet');
const sqlite3 = require('sqlite3').verbose();
const jwt = require('jsonwebtoken');
const path = require('path');
const fs = require('fs');

const app = express();
const PORT = process.env.PORT || 5005;
const JWT_SECRET = process.env.JWT_SECRET || 'beloved-super-secret-vault-key';

// Secure headers & configuration
app.use(helmet({
    contentSecurityPolicy: false // Allow dynamic scripts/styles loaded locally in dev
}));
app.use(cors({
    origin: '*', // Customize for production isolation
    methods: ['GET', 'POST', 'PUT', 'DELETE'],
    allowedHeaders: ['Content-Type', 'Authorization']
}));
app.use(express.json());

// Initialize SQLite database
const dbFile = path.join(__dirname, 'database.sqlite');
const db = new sqlite3.Database(dbFile, (err) => {
    if (err) {
        console.error('Database connection error:', err);
    } else {
        console.log('Database connected successfully at:', dbFile);
        initializeDatabaseSchema();
    }
});

// Middleware for JWT Verification
function authenticateToken(req, res, next) {
    const authHeader = req.headers['authorization'];
    const token = authHeader && authHeader.split(' ')[1];
    
    if (!token) return res.status(401).json({ message: 'Authentication required' });
    
    jwt.verify(token, JWT_SECRET, (err, user) => {
        if (err) return res.status(403).json({ message: 'Invalid or expired token' });
        req.user = user;
        next();
    });
}

function initializeDatabaseSchema() {
    // Initial schema for core tables
    db.serialize(() => {
        db.run(`
            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT UNIQUE,
                password TEXT,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP
            )
        `);
        
        // TABLE_SCHEMAS_START
        // TABLE_SCHEMAS_END
    });
}

// Global utilities helper for dynamic modules to use
app.locals.db = db;
app.locals.authenticateToken = authenticateToken;
app.locals.JWT_SECRET = JWT_SECRET;
app.locals.jwt = jwt;

app.get('/health', (req, res) => {
    res.json({ status: 'ok', time: new Date() });
});

// MODULE_ENDPOINTS_START
// MODULE_ENDPOINTS_END

// Error Handling
app.use((err, req, res, next) => {
    console.error(err.stack);
    res.status(500).json({ message: 'Internal Server Error', error: err.message });
});

app.listen(PORT, () => {
    console.log(`Beloved app server running securely on port ${PORT}`);
});
