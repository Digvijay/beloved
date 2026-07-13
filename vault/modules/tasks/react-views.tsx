import React, { useState, useEffect } from 'react';
import { Plus, ListTodo, FileText } from 'lucide-react';

export function TasksView({ request }: { request: any; activeTab: string }) {
  const [tasks, setTasks] = useState<Array<{ id: number; title: string; status: 'Todo' | 'InProgress' | 'Done' }>>([]);
  const [newTitle, setNewTitle] = useState('');
  const [error, setError] = useState('');

  const fetchTasks = async () => {
    try {
      const data = await request('/tasks');
      setTasks(data);
    } catch (err: any) {
      setError(err.message || 'Failed to load tasks');
    }
  };

  useEffect(() => {
    fetchTasks();
  }, []);

  const handleAddTask = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newTitle.trim()) return;

    try {
      const added = await request('/tasks', {
        method: 'POST',
        body: JSON.stringify({ title: newTitle, status: 'Todo' }),
      });
      setTasks(prev => [...prev, added]);
      setNewTitle('');
    } catch (err: any) {
      setError(err.message || 'Failed to add task');
    }
  };

  const handleUpdateStatus = async (id: number, currentStatus: 'Todo' | 'InProgress' | 'Done') => {
    const nextStatusMap: Record<string, 'Todo' | 'InProgress' | 'Done'> = {
      'Todo': 'InProgress',
      'InProgress': 'Done',
      'Done': 'Todo'
    };
    const nextStatus = nextStatusMap[currentStatus];

    try {
      const updated = await request(`/tasks/${id}`, {
        method: 'PUT',
        body: JSON.stringify({ status: nextStatus }),
      });
      setTasks(prev => prev.map(t => t.id === id ? { ...t, status: nextStatus } : t));
    } catch (err: any) {
      setError(err.message || 'Failed to update task status');
    }
  };

  const columns: Array<'Todo' | 'InProgress' | 'Done'> = ['Todo', 'InProgress', 'Done'];

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <h2 style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
          <ListTodo style={{ color: 'var(--accent)' }} /> Kanban Planner
        </h2>
        <form onSubmit={handleAddTask} style={{ display: 'flex', gap: '0.5rem' }}>
          <input
            type="text"
            className="form-control"
            placeholder="Add new task..."
            value={newTitle}
            onChange={e => setNewTitle(e.target.value)}
            required
            style={{ width: '240px' }}
          />
          <button type="submit" className="btn btn-primary" style={{ display: 'flex', alignItems: 'center', gap: '0.25rem' }}>
            <Plus size={16} /> Add Task
          </button>
        </form>
      </div>

      {error && <div className="alert alert-error">{error}</div>}

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '1.5rem' }}>
        {columns.map(col => (
          <div key={col} style={{ background: 'var(--bg-secondary)', border: '1px solid var(--border-color)', borderRadius: '12px', padding: '1rem', minHeight: '360px' }}>
            <h3 style={{ borderBottom: '2px solid var(--border-color)', paddingBottom: '0.5rem', marginBottom: '1rem', fontSize: '1rem', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <span>{col}</span>
              <span className="badge" style={{ background: 'rgba(59, 130, 246, 0.1)', color: 'var(--accent)' }}>
                {tasks.filter(t => t.status === col).length}
              </span>
            </h3>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
              {tasks.filter(t => t.status === col).map(t => (
                <div key={t.id} onClick={() => handleUpdateStatus(t.id, t.status)} style={{ background: 'var(--card-bg)', border: '1px solid var(--border-color)', borderRadius: '8px', padding: '0.75rem 1rem', cursor: 'pointer', display: 'flex', gap: '0.5rem', alignItems: 'center', transition: 'border-color 0.2s' }}>
                  <FileText size={16} style={{ color: 'var(--text-secondary)' }} />
                  <span style={{ fontSize: '0.9rem', color: 'var(--text-primary)' }}>{t.title}</span>
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
