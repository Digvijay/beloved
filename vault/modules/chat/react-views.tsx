import React, { useState } from 'react';
import { Send, Bot, User } from 'lucide-react';

export function ChatView({ request }: { request: any; activeTab: string }) {
  const [messages, setMessages] = useState<Array<{ sender: 'user' | 'bot'; text: string }>>([
    { sender: 'bot', text: 'Hello! I am your AI assistant. How can I help you build today?' }
  ]);
  const [input, setInput] = useState('');
  const [sending, setSending] = useState(false);

  const handleSend = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!input.trim() || sending) return;

    const userText = input;
    setInput('');
    setMessages(prev => [...prev, { sender: 'user', text: userText }]);
    setSending(true);

    try {
      const data = await request('/chat', {
        method: 'POST',
        body: JSON.stringify({ message: userText }),
      });
      setMessages(prev => [...prev, { sender: 'bot', text: data.reply }]);
    } catch (err: any) {
      setMessages(prev => [...prev, { sender: 'bot', text: 'Error: Failed to fetch reply.' }]);
    } finally {
      setSending(false);
    }
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: 'calc(100vh - 12rem)', border: '1px solid var(--border-color)', borderRadius: '12px', background: 'var(--card-bg)', overflow: 'hidden' }}>
      <div style={{ flex: 1, padding: '1.5rem', overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: '1rem' }}>
        {messages.map((msg, idx) => (
          <div key={idx} style={{
            display: 'flex',
            gap: '0.75rem',
            alignSelf: msg.sender === 'user' ? 'flex-end' : 'flex-start',
            maxWidth: '75%',
            flexDirection: msg.sender === 'user' ? 'row-reverse' : 'row'
          }}>
            <div style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              width: '32px',
              height: '32px',
              borderRadius: '50%',
              background: msg.sender === 'user' ? 'var(--accent)' : 'rgba(59, 130, 246, 0.1)',
              color: msg.sender === 'user' ? '#fff' : 'var(--accent)',
              flexShrink: 0
            }}>
              {msg.sender === 'user' ? <User size={16} /> : <Bot size={16} />}
            </div>
            <div style={{
              padding: '0.75rem 1rem',
              borderRadius: '12px',
              fontSize: '0.95rem',
              lineHeight: '1.4',
              background: msg.sender === 'user' ? 'rgba(59, 130, 246, 0.08)' : 'var(--bg-secondary)',
              border: '1px solid var(--border-color)',
              color: 'var(--text-primary)'
            }}>
              {msg.text}
            </div>
          </div>
        ))}
      </div>

      <form onSubmit={handleSend} style={{ display: 'flex', gap: '0.75rem', padding: '1rem', borderTop: '1px solid var(--border-color)', background: 'var(--card-bg)' }}>
        <input
          type="text"
          className="form-control"
          placeholder="Ask something..."
          value={input}
          onChange={e => setInput(e.target.value)}
          disabled={sending}
          style={{ flex: 1 }}
          required
        />
        <button type="submit" className="btn btn-primary" style={{ display: 'flex', alignItems: 'center', gap: '0.25rem' }} disabled={sending}>
          <Send size={16} />
        </button>
      </form>
    </div>
  );
}
