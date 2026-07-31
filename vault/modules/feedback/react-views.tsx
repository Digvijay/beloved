import React, { useState } from 'react';
import { ThumbsUp, HelpCircle } from 'lucide-react';

export function FeedbackView({ request }: { request: any; activeTab: string }) {
  const [category, setCategory] = useState<'Idea' | 'Bug' | 'Question'>('Idea');
  const [comment, setComment] = useState('');
  const [rating, setRating] = useState(5);
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState('');

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setSuccess(false);

    try {
      await request('/feedback', {
        method: 'POST',
        body: JSON.stringify({ category, comment, rating }),
      });
      setSuccess(true);
      setComment('');
      setRating(5);
    } catch (err: any) {
      setError(err.message || 'Failed to submit feedback');
    }
  };

  return (
    <div style={{ maxWidth: '600px', margin: '2rem auto' }} className="card">
      <h2 style={{ marginBottom: '1.5rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
        <HelpCircle style={{ color: 'var(--accent)' }} /> Send Feedback
      </h2>

      {success && <div className="alert alert-success">Thank you! Your feedback has been submitted successfully.</div>}
      {error && <div className="alert alert-error">{error}</div>}

      <form onSubmit={handleSubmit}>
        <div className="form-group">
          <label>Category</label>
          <select className="form-control" value={category} onChange={e => setCategory(e.target.value as any)}>
            <option value="Idea">Idea / Suggestion</option>
            <option value="Bug">Bug Report</option>
            <option value="Question">Question</option>
          </select>
        </div>

        <div className="form-group">
          <label>Rating (1-5 Stars)</label>
          <div style={{ display: 'flex', gap: '0.5rem', margin: '0.5rem 0' }}>
            {[1, 2, 3, 4, 5].map(star => (
              <button
                key={star}
                type="button"
                onClick={() => setRating(star)}
                style={{
                  background: star <= rating ? 'var(--accent)' : 'var(--bg-secondary)',
                  color: star <= rating ? '#fff' : 'var(--text-secondary)',
                  border: '1px solid var(--border-color)',
                  borderRadius: '6px',
                  padding: '0.4rem 0.8rem',
                  cursor: 'pointer'
                }}
              >
                {star} ★
              </button>
            ))}
          </div>
        </div>

        <div className="form-group">
          <label>Describe your thoughts</label>
          <textarea
            className="form-control"
            rows={4}
            placeholder="Tell us what you think..."
            value={comment}
            onChange={e => setComment(e.target.value)}
            required
          />
        </div>

        <button type="submit" className="btn btn-primary" style={{ width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '0.5rem' }}>
          <ThumbsUp size={16} /> Submit Feedback
        </button>
      </form>
    </div>
  );
}
