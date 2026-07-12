import React, { useState, useEffect } from 'react';
import { PlusCircle, List, Trash2, RefreshCw } from 'lucide-react';

export function ItemsView({ request, token }: { request: any; token: string | null }) {
  const [items, setItems] = useState<any[]>([]);
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [quantity, setQuantity] = useState(1);

  const fetchItems = async () => {
    if (!token) return;
    try {
      const data = await request('/items');
      setItems(data);
    } catch (err) {
      console.error(err);
    }
  };

  useEffect(() => {
    fetchItems();
  }, [token]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await request('/items', {
        method: 'POST',
        body: JSON.stringify({ name, description, quantity }),
      });
      setName('');
      setDescription('');
      setQuantity(1);
      fetchItems();
    } catch (err) {
      alert('Failed to save item');
    }
  };

  const handleDelete = async (id: number) => {
    if (!confirm('Delete this item?')) return;
    try {
      await request(`/items/${id}`, { method: 'DELETE' });
      fetchItems();
    } catch (err) {
      alert('Delete failed');
    }
  };

  return (
    <div style={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: '2rem' }}>
      <div className="card">
        <h3><PlusCircle size={18} style={{ marginRight: '0.5rem', verticalAlign: 'middle' }} /> Add Item</h3>
        <form onSubmit={handleSubmit} style={{ marginTop: '1rem' }}>
          <div className="form-group">
            <label>Name</label>
            <input type="text" className="form-control" value={name} onChange={e => setName(e.target.value)} required />
          </div>
          <div className="form-group">
            <label>Description</label>
            <textarea className="form-control" rows={3} value={description} onChange={e => setDescription(e.target.value)} />
          </div>
          <div className="form-group">
            <label>Quantity</label>
            <input type="number" className="form-control" min={1} value={quantity} onChange={e => setQuantity(parseInt(e.target.value))} required />
          </div>
          <button type="submit" className="btn btn-primary" style={{ width: '100%' }}>Save Item</button>
        </form>
      </div>

      <div className="card">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem' }}>
          <h3><List size={18} style={{ marginRight: '0.5rem', verticalAlign: 'middle' }} /> Managed Items</h3>
          <button className="btn btn-secondary" onClick={fetchItems}><RefreshCw size={14} /></button>
        </div>
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Description</th>
              <th>Qty</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {items.length === 0 ? (
              <tr>
                <td colSpan={4} style={{ textAlign: 'center', opacity: 0.5 }}>No items found. Add one on the left!</td>
              </tr>
            ) : (
              items.map(item => (
                <tr key={item.id}>
                  <td style={{ fontWeight: 600 }}>{item.name}</td>
                  <td>{item.description || '-'}</td>
                  <td><span style={{ padding: '0.2rem 0.5rem', background: 'rgba(255,255,255,0.05)', borderRadius: '4px' }}>{item.quantity}</span></td>
                  <td>
                    <button className="btn btn-secondary" style={{ color: 'var(--error)', padding: '0.3rem 0.5rem' }} onClick={() => handleDelete(item.id)}>
                      <Trash2 size={14} />
                    </button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
