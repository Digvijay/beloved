// Items CRUD Module Endpoints
app.get('/api/items', authenticateToken, (req, res) => {
    const db = app.locals.db;
    db.all(
        `SELECT * FROM items WHERE user_id = ? ORDER BY created_at DESC`,
        [req.user.id],
        (err, rows) => {
            if (err) return res.status(500).json({ message: 'Failed to fetch items', error: err.message });
            res.json(rows);
        }
    );
});

app.post('/api/items', authenticateToken, (req, res) => {
    const { name, description, quantity } = req.body;
    if (!name) return res.status(400).json({ message: 'Name is required' });

    const db = app.locals.db;
    db.run(
        `INSERT INTO items (user_id, name, description, quantity) VALUES (?, ?, ?, ?)`,
        [req.user.id, name, description, quantity || 1],
        function (err) {
            if (err) return res.status(500).json({ message: 'Failed to create item', error: err.message });
            res.status(201).json({ id: this.lastID, name, description, quantity });
        }
    );
});

app.delete('/api/items/:id', authenticateToken, (req, res) => {
    const itemId = req.params.id;
    const db = app.locals.db;
    
    db.run(
        `DELETE FROM items WHERE id = ? AND user_id = ?`,
        [itemId, req.user.id],
        function (err) {
            if (err) return res.status(500).json({ message: 'Failed to delete item', error: err.message });
            if (this.changes === 0) return res.status(404).json({ message: 'Item not found or unauthorized' });
            res.json({ message: 'Item deleted successfully' });
        }
    );
});
