import { useState, useEffect } from 'react';

export function BillingView({ request, activeTab }: { request: any; activeTab: string }) {
  const [planData, setPlanData] = useState<any>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (activeTab === 'billing') {
      fetchPlan();
    }
  }, [activeTab]);

  const fetchPlan = async () => {
    setLoading(true);
    try {
      const data = await request('/billing/plan');
      setPlanData(data);
      setError(null);
    } catch (err: any) {
      setError(err.message || 'Failed to fetch plan metrics.');
    } finally {
      setLoading(false);
    }
  };

  const handleCheckout = async (plan: string) => {
    try {
      const res = await request('/billing/checkout', {
        method: 'POST',
        body: JSON.stringify({ plan })
      });
      if (res.url) {
        window.location.href = res.url;
      }
    } catch (err: any) {
      alert(err.message || 'Checkout session initiation failed.');
    }
  };

  const handlePortal = async () => {
    try {
      const res = await request('/billing/portal', { method: 'POST' });
      if (res.url) {
        window.location.href = res.url;
      }
    } catch (err: any) {
      alert(err.message || 'Customer portal redirection failed.');
    }
  };

  if (activeTab !== 'billing') return null;

  return (
    <div style={{ padding: '20px', background: 'rgba(255,255,255,0.02)', borderRadius: '12px', border: '1px solid rgba(255,255,255,0.05)' }}>
      <h2 style={{ margin: '0 0 10px 0', fontSize: '1.25rem' }}>Subscription & Metering Quotas</h2>
      <p style={{ color: 'var(--text-secondary)', fontSize: '0.9rem', marginBottom: '20px' }}>
        Manage your plans, checkout subscriptions, and monitor assembly compile consumption metrics.
      </p>

      {error && <div style={{ color: 'var(--error)', marginBottom: '15px' }}>{error}</div>}

      {loading && !planData ? (
        <div>Loading metrics...</div>
      ) : (
        planData && (
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '20px', marginBottom: '30px' }}>
            <div style={{ background: 'rgba(0,0,0,0.2)', padding: '20px', borderRadius: '8px', border: '1px solid rgba(255,255,255,0.05)' }}>
              <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)' }}>Current Tier</div>
              <div style={{ fontSize: '1.8rem', fontWeight: 'bold', color: '#a78bfa', marginTop: '5px' }}>{planData.plan}</div>
              {planData.stripeCustomerId && (
                <button onClick={handlePortal} className="btn" style={{ marginTop: '15px', padding: '6px 12px', fontSize: '0.8rem' }}>
                  Manage Card & Invoices
                </button>
              )}
            </div>

            <div style={{ background: 'rgba(0,0,0,0.2)', padding: '20px', borderRadius: '8px', border: '1px solid rgba(255,255,255,0.05)' }}>
              <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)' }}>Usage This Month</div>
              <div style={{ fontSize: '1.8rem', fontWeight: 'bold', color: '#10b981', marginTop: '5px' }}>
                {planData.usedThisMonth} / {planData.monthlyQuota ?? '∞'}
              </div>
              <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', marginTop: '5px' }}>
                Remaining: {planData.remaining ?? 'Unlimited'}
              </div>
            </div>
          </div>
        )
      )}

      <h3 style={{ margin: '0 0 15px 0', fontSize: '1.1rem' }}>Available Subscription Plans</h3>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))', gap: '20px' }}>
        {/* Free Plan */}
        <div style={{ background: 'rgba(255,255,255,0.01)', padding: '20px', borderRadius: '8px', border: '1px solid rgba(255,255,255,0.05)', display: 'flex', flexDirection: 'column', justifyContent: 'space-between' }}>
          <div>
            <h4 style={{ margin: 0, fontSize: '1.05rem' }}>Developer Free</h4>
            <div style={{ fontSize: '1.5rem', fontWeight: 'bold', margin: '10px 0' }}>$0 <span style={{ fontSize: '0.8rem', fontWeight: 'normal', color: 'var(--text-secondary)' }}>/ month</span></div>
            <ul style={{ paddingLeft: '20px', fontSize: '0.85rem', color: 'var(--text-secondary)', lineHeight: '1.6' }}>
              <li>50 assembly compiles per month</li>
              <li>Community registry access</li>
              <li>Standard build latency priority</li>
            </ul>
          </div>
          <button className="btn" style={{ width: '100%', marginTop: '15px', background: 'rgba(255,255,255,0.05)', color: '#fff' }} disabled>
            Current Plan
          </button>
        </div>

        {/* Pro Plan */}
        <div style={{ background: 'rgba(255,255,255,0.01)', padding: '20px', borderRadius: '8px', border: '1px solid rgba(167, 139, 250, 0.2)', display: 'flex', flexDirection: 'column', justifyContent: 'space-between', position: 'relative' }}>
          <div style={{ position: 'absolute', top: '-10px', right: '15px', background: '#7c3aed', color: '#fff', fontSize: '0.75rem', fontWeight: 'bold', padding: '2px 8px', borderRadius: '10px' }}>RECOMMENDED</div>
          <div>
            <h4 style={{ margin: 0, fontSize: '1.05rem' }}>Professional Scale</h4>
            <div style={{ fontSize: '1.5rem', fontWeight: 'bold', margin: '10px 0' }}>$49 <span style={{ fontSize: '0.8rem', fontWeight: 'normal', color: 'var(--text-secondary)' }}>/ month</span></div>
            <ul style={{ paddingLeft: '20px', fontSize: '0.85rem', color: 'var(--text-secondary)', lineHeight: '1.6' }}>
              <li>500 assembly compiles per month</li>
              <li>Private OCI module registries</li>
              <li>High-speed cache acceleration</li>
              <li>Priority webhook dispatch queues</li>
            </ul>
          </div>
          <button onClick={() => handleCheckout('Pro')} className="btn" style={{ width: '100%', marginTop: '15px', background: 'linear-gradient(135deg, #7c3aed 0%, #db2777 100%)' }}>
            Upgrade to Pro
          </button>
        </div>
      </div>
    </div>
  );
}
