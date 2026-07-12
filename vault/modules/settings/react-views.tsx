export function SettingsView({ request, activeTab }) {
  if (activeTab !== "settings") return null;
  return <div style={{padding:"20px"}}><h3>Account & Organization Settings</h3><p>Manage system theme details, profiles, and API access tokens.</p></div>;
}