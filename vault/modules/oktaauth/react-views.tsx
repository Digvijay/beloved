import React, { useState } from 'react';
import { Shield, ArrowRight } from 'lucide-react';

export function OktaAuthView({ login, request, setActiveTab }: { login: any; request: any; setActiveTab: any }) {
  const [email, setEmail] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleOktaLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const data = await request('/auth/okta', {
        method: 'POST',
        body: JSON.stringify({ email, token: 'okta-simulated-sso-id-token' }),
      });
      login(data.token, data.user);
      setActiveTab('items');
    } catch (err: any) {
      setError(err.message || 'Okta SSO Authentication failed');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div style={{ maxWidth: '480px', margin: '4rem auto' }} className="card">
      <div style={{ textAlign: 'center', marginBottom: '2rem' }}>
        <div style={{
          display: 'inline-flex',
          padding: '1rem',
          borderRadius: '50%',
          background: 'rgba(59, 130, 246, 0.1)',
          color: 'var(--accent)',
          marginBottom: '1rem'
        }}>
          <Shield size={32} />
        </div>
        <h2>Okta Single Sign-On</h2>
        <p style={{ color: 'var(--text-secondary)', fontSize: '0.9rem', marginTop: '0.5rem' }}>
          Securely sign in using your corporate Okta identity credentials.
        </p>
      </div>

      {error && <div className="alert alert-error">{error}</div>}

      <form onSubmit={handleOktaLogin}>
        <div className="form-group">
          <label>Corporate Email Address</label>
          <input
            type="email"
            className="form-control"
            placeholder="you@company.com"
            value={email}
            onChange={e => setEmail(e.target.value)}
            required
          />
        </div>

        <button
          type="submit"
          className="btn btn-primary"
          style={{ width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '0.5rem' }}
          disabled={loading}
        >
          {loading ? 'Connecting to Okta...' : 'Sign In with Okta'}
          {!loading && <ArrowRight size={16} />}
        </button>
      </form>
    </div>
  );
}
