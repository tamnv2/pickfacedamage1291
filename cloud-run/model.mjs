/** Pure response parity mapping from existing project-owned Google Sheet columns. */
const v=(r,i)=>String(r[i]??'').trim();
const num=(v)=>Number.isFinite(Number(v))?Number(v):0;
export function normalizeUsername(value){return String(value||'').trim().toLowerCase();}
export function authorizeAction(action,profile){
  if (!profile || profile.active!==true) return false;
  const role=String(profile.role||'').toLowerCase();
  const p=profile.permissions||{};
  if (action==='list_audit') return role==='admin';
  if (action==='get_image') return role==='admin'||p.view_reports===true||p.damage_entry===true;
  if (action==='pull_changes') return role==='admin'||p.view_reports===true||p.damage_entry===true||p.sync_google===true;
  if (action==='pull_products') return true; // legacy Gateway allows any active user
  if (action==='verify') return true;
  return false;
}
export function extractFileId(url){
  const m=String(url||'').match(/\/file\/d\/([\w-]{10,160})\//);
  return m?m[1]:'';
}
function dateOnly(s){const raw=String(s||'').trim();const m=raw.match(/^(\d{2})\/(\d{2})\/(\d{4})$/);return m?m[3]+'-'+m[2]+'-'+m[1]:raw.slice(0,10);}
export function parseProductRow(r){
  return {sku:v(r,0),product_name:v(r,1),base_unit:v(r,2),first_seen_at:v(r,3),last_seen_at:v(r,4),source_file:v(r,5),change_seq:num(r[6])};
}
export function parseReportRow(r){
  const time=v(r,2).split(':');const images=[];
  for(let i=0;i<5;i++){const url=v(r,9+i),hash=v(r,23+i);if(url||hash)images.push({sequence:i+1,url,file_id:extractFileId(url),sha256:hash});}
  return {
    change_seq:num(r[20]),deleted:v(r,21).toUpperCase()==='TRUE'||v(r,4)==='__DELETED__',
    fingerprint:v(r,22),deleted_at:v(r,28),deleted_by:v(r,29),
    report:{report_id:v(r,0),occurred_date:dateOnly(v(r,1)),hour:num(time[0]),minute:num(time[1]),shift:v(r,3),sku:v(r,4),product_name:v(r,5),location:v(r,6),quantity:num(r[7]),base_unit:v(r,8),created_at:v(r,14),created_by:v(r,16),version:Math.max(1,num(r[17])),updated_at:v(r,18),updated_by:v(r,19)},
    images
  };
}
export function parseAuditRow(r){
  let details=null;try{details=JSON.parse(v(r,8)||'null');}catch{details=v(r,8);}
  return {event_id:v(r,0),server_time:num(r[1]),client_time:v(r,2),uid:v(r,3),username:v(r,4),device_id:v(r,5),session_id:v(r,6),action:v(r,7),details};
}
