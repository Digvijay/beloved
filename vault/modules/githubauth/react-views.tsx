import React, { useState } from 'react';
import { Github, ArrowRight } from 'lucide-react';

export function GithubAuthView({ login, request, setActiveTab }: { login: any; request: any; setActiveTab: any }) {
  const [code, setCode] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleGithubLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const data = await request('/auth/github', {
        method: 'POST',
        body: JSON.stringify({ code: code || 'github-mock-code-123' }),
      });
      login(data.token, data.user);
      setActiveTab('items');
    } catch (err: any) {
      setError(err.message || 'GitHub OAuth sign-in failed');
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
          background: 'rgba(31, 41, 55, 0.1)',
          color: 'var(--text-primary)',
          marginBottom: '1rem'
        }}>
          <Github size={32} />
        </div>
        <h2>Sign In with GitHub</h2>
        <p style={{ color: 'var(--text-secondary)', fontSize: '0.9rem', marginTop: '0.5rem' }}>
          Connect securely using your GitHub developer account identity.
        </p>
      </div>

      {error && <div className="alert alert-error">{error}</div>}

      <form onSubmit={handleGithubLogin}>
        <div className="form-group">
          <label>Simulated OAuth Verification Code (Optional)</label>
          <input
            type="text"
            className="form-control"
            placeholder="e.g. auth-code-from-github-callback"
            value={code}
            onChange={e => setCode(e.target.value)}
          />
        </div>

        <button
          type="submit"
          className="btn btn-primary"
          style={{ width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '0.5rem', background: '#1F2937', borderColor: '#1F2937' }}
          disabled={loading}
        >
          {loading ? 'Authenticating with GitHub...' : 'Continue with GitHub'}
          {!loading && <ArrowRight size={16} />}
        </button>
      </form>
    </div>
  );
}
