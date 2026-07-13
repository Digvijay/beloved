import React, { useState } from 'react';
import { ShoppingBag, CreditCard, Trash2 } from 'lucide-react';

interface CartItem {
  id: number;
  name: string;
  price: number;
  quantity: number;
}

export function CartView({ request }: { request: any; activeTab: string }) {
  const [items, setItems] = useState<CartItem[]>([
    { id: 1, name: 'Premium SaaS Subscription', price: 49.00, quantity: 1 },
    { id: 2, name: 'API Developer License', price: 99.00, quantity: 2 }
  ]);
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState('');

  const calculateSubtotal = () => items.reduce((acc, item) => acc + (item.price * item.quantity), 0);

  const handleRemove = (id: number) => {
    setItems(prev => prev.filter(item => item.id !== id));
  };

  const handleCheckout = async () => {
    setError('');
    setSuccess(false);

    try {
      const subtotal = calculateSubtotal();
      await request('/cart/checkout', {
        method: 'POST',
        body: JSON.stringify({ subtotal, itemIds: items.map(i => i.id) }),
      });
      setSuccess(true);
      setItems([]);
    } catch (err: any) {
      setError(err.message || 'Checkout calculation failed');
    }
  };

  return (
    <div style={{ maxWidth: '600px', margin: '2rem auto' }} className="card">
      <h2 style={{ marginBottom: '1.5rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
        <ShoppingBag style={{ color: 'var(--accent)' }} /> Shopping Cart
      </h2>

      {success && <div className="alert alert-success">Order placed successfully! Mock invoice sent to billing repository.</div>}
      {error && <div className="alert alert-error">{error}</div>}

      {items.length === 0 && !success ? (
        <p style={{ textAlign: 'center', color: 'var(--text-secondary)', padding: '2rem 0' }}>Your shopping cart is empty.</p>
      ) : (
        <>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem', marginBottom: '1.5rem' }}>
            {items.map(item => (
              <div key={item.id} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '0.75rem 1rem', border: '1px solid var(--border-color)', borderRadius: '8px', background: 'var(--bg-secondary)' }}>
                <div>
                  <h4 style={{ margin: 0, fontSize: '0.95rem' }}>{item.name}</h4>
                  <span style={{ fontSize: '0.85rem', color: 'var(--text-secondary)' }}>
                    ${item.price.toFixed(2)} × {item.quantity}
                  </span>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '1rem' }}>
                  <span style={{ fontWeight: '600', fontSize: '0.95rem' }}>
                    ${(item.price * item.quantity).toFixed(2)}
                  </span>
                  <button onClick={() => handleRemove(item.id)} style={{ background: 'none', border: 'none', color: 'var(--error)', cursor: 'pointer', padding: '4px' }}>
                    <Trash2 size={16} />
                  </button>
                </div>
              </div>
            ))}
          </div>

          {items.length > 0 && (
            <div style={{ borderTop: '1px solid var(--border-color)', paddingTop: '1rem', display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '1rem', fontWeight: '500' }}>
                <span>Subtotal</span>
                <span>${calculateSubtotal().toFixed(2)}</span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.9rem', color: 'var(--text-secondary)' }}>
                <span>Estimated Tax (10%)</span>
                <span>${(calculateSubtotal() * 0.1).toFixed(2)}</span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '1.1rem', fontWeight: '600', borderTop: '1px dashed var(--border-color)', paddingTop: '0.5rem', marginTop: '0.5rem' }}>
                <span>Total</span>
                <span>${(calculateSubtotal() * 1.1).toFixed(2)}</span>
              </div>

              <button onClick={handleCheckout} className="btn btn-primary" style={{ width: '100%', marginTop: '1rem', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '0.5rem' }}>
                <CreditCard size={16} /> Proceed to Checkout
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );
}
