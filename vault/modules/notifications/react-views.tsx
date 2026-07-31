import { useState, useEffect } from 'react';

export function NotificationsView({ request, activeTab }: { request: any; activeTab: string }) {
  const [notifications, setNotifications] = useState<any[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (activeTab === 'notifications') {
      fetchNotifications();
    }
  }, [activeTab]);

  const fetchNotifications = async () => {
    setLoading(true);
    try {
      const data = await request('/notifications');
      setNotifications(data);
    } catch (err: any) {
      console.error(err.message);
    } finally {
      setLoading(false);
    }
  };

  const markAsRead = async (id: string) => {
    try {
      await request(`/notifications/${id}/read`, { method: 'POST' });
      setNotifications(prev => prev.map(n => n.id === id ? { ...n, isRead: true } : n));
    } catch (err: any) {
      console.error(err.message);
    }
  };

  if (activeTab !== 'notifications') return null;

  return (
    <div style={{ padding: '20px', background: 'rgba(255,255,255,0.02)', borderRadius: '12px', border: '1px solid rgba(255,255,255,0.05)' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
        <h2 style={{ margin: 0, fontSize: '1.25rem' }}>Notification Hub</h2>
        <button onClick={fetchNotifications} className="btn" style={{ padding: '6px 12px', fontSize: '0.85rem' }} disabled={loading}>
          {loading ? 'Updating...' : 'Reload Inbox'}
        </button>
      </div>

      <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
        {notifications.length === 0 ? (
          <div style={{ padding: '30px', textAlign: 'center', color: 'var(--text-secondary)' }}>
            Your notification queue is currently empty.
          </div>
        ) : (
          notifications.map(n => (
            <div key={n.id} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '15px', borderRadius: '8px', background: n.isRead ? 'rgba(0,0,0,0.1)' : 'rgba(124,58,237,0.05)', border: n.isRead ? '1px solid rgba(255,255,255,0.03)' : '1px solid rgba(124,58,237,0.2)' }}>
              <div>
                <div style={{ fontWeight: n.isRead ? 'normal' : 'bold', color: n.isRead ? 'var(--text-secondary)' : '#fff' }}>{n.title}</div>
                <div style={{ fontSize: '0.85rem', color: 'var(--text-secondary)', marginTop: '4px' }}>{n.message}</div>
                <div style={{ fontSize: '0.75rem', color: 'var(--text-secondary)', marginTop: '8px' }}>{new Date(n.timestamp).toLocaleString()}</div>
              </div>
              {!n.isRead && (
                <button onClick={() => markAsRead(n.id)} className="btn" style={{ padding: '4px 8px', fontSize: '0.75rem' }}>
                  Mark read
                </button>
              )}
            </div>
          ))
        )}
      </div>
    </div>
  );
}
