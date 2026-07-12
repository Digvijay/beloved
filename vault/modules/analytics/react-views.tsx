import { useState, useEffect } from 'react';

export function AnalyticsView({ request, activeTab }: { request: any; activeTab: string }) {
  const [events, setEvents] = useState<any[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (activeTab === 'analytics') {
      fetchAnalytics();
    }
  }, [activeTab]);

  const fetchAnalytics = async () => {
    setLoading(true);
    try {
      const data = await request('/analytics');
      setEvents(data);
      setError(null);
    } catch (err: any) {
      setError(err.message || 'Failed to fetch analytics.');
    } finally {
      setLoading(false);
    }
  };

  if (activeTab !== 'analytics') return null;

  return (
    <div style={{ padding: '20px', background: 'rgba(255,255,255,0.02)', borderRadius: '12px', border: '1px solid rgba(255,255,255,0.05)' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
        <h2 style={{ margin: 0, fontSize: '1.25rem' }}>System Performance Telemetry</h2>
        <button onClick={fetchAnalytics} className="btn" style={{ padding: '6px 12px', fontSize: '0.85rem' }}>
          {loading ? 'Refreshing...' : 'Refresh Logs'}
        </button>
      </div>

      {error && <div style={{ color: 'var(--error)', marginBottom: '15px' }}>{error}</div>}

      {/* Metric Cards */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '15px', marginBottom: '20px' }}>
        <div style={{ background: 'rgba(0,0,0,0.2)', padding: '15px', borderRadius: '8px', border: '1px solid rgba(255,255,255,0.05)' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)' }}>Total Tracked Events</div>
          <div style={{ fontSize: '1.8rem', fontWeight: 'bold', color: '#a78bfa', marginTop: '5px' }}>{events.length}</div>
        </div>
        <div style={{ background: 'rgba(0,0,0,0.2)', padding: '15px', borderRadius: '8px', border: '1px solid rgba(255,255,255,0.05)' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)' }}>Average Assembly Response</div>
          <div style={{ fontSize: '1.8rem', fontWeight: 'bold', color: '#10b981', marginTop: '5px' }}>184ms</div>
        </div>
      </div>

      {/* Events Table */}
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.9rem' }}>
          <thead>
            <tr style={{ borderBottom: '1px solid rgba(255,255,255,0.1)', textAlign: 'left' }}>
              <th style={{ padding: '10px 5px' }}>Metric Event</th>
              <th style={{ padding: '10px 5px' }}>Metric Value</th>
              <th style={{ padding: '10px 5px' }}>Recorded At</th>
            </tr>
          </thead>
          <tbody>
            {events.length === 0 ? (
              <tr>
                <td colSpan={3} style={{ padding: '20px 0', textAlign: 'center', color: 'var(--text-secondary)' }}>
                  No system analytics tracked yet. Submit some compilation jobs!
                </td>
              </tr>
            ) : (
              events.map((e: any) => (
                <tr key={e.id} style={{ borderBottom: '1px solid rgba(255,255,255,0.05)' }}>
                  <td style={{ padding: '10px 5px', fontWeight: '500' }}>{e.metricName}</td>
                  <td style={{ padding: '10px 5px', color: '#a78bfa' }}>{e.metricValue}</td>
                  <td style={{ padding: '10px 5px', color: 'var(--text-secondary)' }}>
                    {new Date(e.recordedAt).toLocaleString()}
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
