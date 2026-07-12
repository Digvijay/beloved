// Auth Module Endpoints
const bcrypt = require('bcryptjs');

app.post('/api/auth/register', (req, res) => {
    const { username, password } = req.body;
    if (!username || !password) {
        return res.status(400).json({ message: 'Username and password are required' });
    }

    const db = app.locals.db;
    const JWT_SECRET = app.locals.JWT_SECRET;
    const jwt = app.locals.jwt;

    const hashedPassword = bcrypt.hashSync(password, 10);
    db.run(
        `INSERT INTO users (username, password) VALUES (?, ?)`,
        [username, hashedPassword],
        function (err) {
            if (err) {
                if (err.message.includes('UNIQUE')) {
                    return res.status(400).json({ message: 'Username already exists' });
                }
                return res.status(500).json({ message: 'Failed to create user', error: err.message });
            }
            
            const userId = this.lastID;
            const token = jwt.sign({ id: userId, username }, JWT_SECRET, { expiresIn: '24h' });
            res.status(201).json({
                message: 'User registered successfully',
                token,
                user: { id: userId, username }
            });
        }
    );
});

app.post('/api/auth/login', (req, res) => {
    const { username, password } = req.body;
    if (!username || !password) {
        return res.status(400).json({ message: 'Username and password are required' });
    }

    const db = app.locals.db;
    const JWT_SECRET = app.locals.JWT_SECRET;
    const jwt = app.locals.jwt;

    db.get(
        `SELECT * FROM users WHERE username = ?`,
        [username],
        (err, user) => {
            if (err) return res.status(500).json({ message: 'Database error', error: err.message });
            if (!user) return res.status(401).json({ message: 'Invalid credentials' });

            const validPassword = bcrypt.compareSync(password, user.password);
            if (!validPassword) return res.status(401).json({ message: 'Invalid credentials' });

            const token = jwt.sign({ id: user.id, username: user.username }, JWT_SECRET, { expiresIn: '24h' });
            res.json({
                message: 'Login successful',
                token,
                user: { id: user.id, username: user.username }
            });
        }
    );
});
