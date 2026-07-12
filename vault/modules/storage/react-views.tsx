import { useState } from "react";
export function StorageView({ request, activeTab }) {
  if (activeTab !== "storage") return null;
  return <div style={{padding:"20px"}}><h3>Storage Sandbox</h3><p>Upload files dynamically using secure container links.</p><input type="file" /><button className="btn" style={{marginLeft:"10px"}}>Upload File</button></div>;
}