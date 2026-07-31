import { useState, useEffect } from 'react';
import { LogOut, Activity } from 'lucide-react';

export default function App() {
  const [token, setToken] = useState<string | null>(localStorage.getItem('token'));
  const [user, setUser] = useState<{ username: string } | null>(
    JSON.parse(localStorage.getItem('user') || 'null')
  );
  const [activeTab, setActiveTab] = useState<string>('login');

  const apiBase = window.location.origin.includes('localhost') || window.location.origin.includes('127.0.0.1')
    ? 'http://localhost:5000/api'
    : '/api';

  useEffect(() => {
    if (token) {
      // Find first tab that isn't login or register
      setActiveTab('items');
    } else {
      setActiveTab('login');
    }
  }, [token]);

  const request = async (path: string, options: RequestInit = {}) => {
    const headers = {
      'Content-Type': 'application/json',
      ...(token ? { 'Authorization': `Bearer ${token}` } : {}),
      ...options.headers,
    };

    const res = await fetch(`${apiBase}${path}`, {
      ...options,
      headers,
    });

    if (res.status === 401) {
      logout();
      throw new Error('Session expired');
    }

    const data = await res.json();
    if (!res.ok) throw new Error(data.message || 'API request failed');
    return data;
  };

  const login = (newToken: string, newUser: { username: string }) => {
    setToken(newToken);
    setUser(newUser);
    localStorage.setItem('token', newToken);
    localStorage.setItem('user', JSON.stringify(newUser));
  };

  const logout = () => {
    setToken(null);
    setUser(null);
    localStorage.removeItem('token');
    localStorage.removeItem('user');
    setActiveTab('login');
  };

  return (
    <div className="app-shell">
      {/* Sidebar Navigation */}
      <nav className="sidebar">
        <div className="brand">
          <div className="brand-icon">B</div>
          <span className="brand-name">Beloved App</span>
        </div>

        <ul className="nav-links">
          {/* MODULE_NAV_ITEMS_START */}
          {/* MODULE_NAV_ITEMS_END */}

          {token && (
            <li>
              <button onClick={logout} className="nav-item" style={{ width: '100%', textAlign: 'left', background: 'none', border: 'none' }}>
                <LogOut size={18} /> Logout
              </button>
            </li>
          )}
        </ul>
      </nav>

      {/* Main Panel Content */}
      <main className="main-content">
        <header className="header">
          <h1 className="page-title">{activeTab.toUpperCase()}</h1>
          {user && (
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <span style={{ fontSize: '0.9rem', color: 'var(--text-secondary)' }}>Welcome, {user.username}</span>
              <span className="badge" style={{ background: 'rgba(16, 185, 129, 0.1)', color: 'var(--success)' }}>
                <Activity size={12} style={{ marginRight: '0.25rem' }} /> Active
              </span>
            </div>
          )}
        </header>

        <div style={{ flex: 1 }}>
          {/* MODULE_VIEWS_START */}
          {/* MODULE_VIEWS_END */}
        </div>
      </main>
    </div>
  );
}
