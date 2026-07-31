import React, { useState } from 'react';
import { KeyRound, ArrowRight } from 'lucide-react';

export function EntraIdAuthView({ login, request, setActiveTab }: { login: any; request: any; setActiveTab: any }) {
  const [tenantId, setTenantId] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleEntraIdLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const data = await request('/auth/entraid', {
        method: 'POST',
        body: JSON.stringify({ tenantId, token: 'entra-simulated-jwt-bearer' }),
      });
      login(data.token, data.user);
      setActiveTab('items');
    } catch (err: any) {
      setError(err.message || 'Microsoft Entra ID Authentication failed');
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
          background: 'rgba(16, 185, 129, 0.1)',
          color: 'var(--success)',
          marginBottom: '1rem'
        }}>
          <KeyRound size={32} />
        </div>
        <h2>Microsoft Entra ID</h2>
        <p style={{ color: 'var(--text-secondary)', fontSize: '0.9rem', marginTop: '0.5rem' }}>
          Federated sign-in using your Azure AD or Microsoft 365 Directory tenant.
        </p>
      </div>

      {error && <div className="alert alert-error">{error}</div>}

      <form onSubmit={handleEntraIdLogin}>
        <div className="form-group">
          <label>Entra Directory (Tenant) ID / Domain</label>
          <input
            type="text"
            className="form-control"
            placeholder="e.g. contoso.onmicrosoft.com or UUID"
            value={tenantId}
            onChange={e => setTenantId(e.target.value)}
            required
          />
        </div>

        <button
          type="submit"
          className="btn btn-primary"
          style={{ width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '0.5rem', background: '#2563EB', borderColor: '#2563EB' }}
          disabled={loading}
        >
          {loading ? 'Redirecting to Microsoft...' : 'Sign In with Microsoft'}
          {!loading && <ArrowRight size={16} />}
        </button>
      </form>
    </div>
  );
}
