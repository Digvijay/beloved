export function CommentsView({ request, activeTab }) {
  if (activeTab !== "comments") return null;
  return <div style={{padding:"20px"}}><h3>Collaborative Workspace Feed</h3><textarea placeholder="Write a comment..." style={{width:"100%",background:"rgba(0,0,0,0.2)",color:"#fff",border:"1px solid rgba(255,255,255,0.1)",padding:"10px",borderRadius:"6px"}}></textarea><button className="btn" style={{marginTop:"10px"}}>Post Message</button></div>;
}