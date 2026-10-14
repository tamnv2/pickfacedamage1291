/**
 * PICKFACE DAMAGE 1291 — Cloud Run migration proof-of-structure, NOT production-ready.
 * READ-ONLY actions only. Mutations fail closed until concurrent/idempotent design,
 * live permission checks and OWNER permission authorization are verified.
 *
 * No private-key or OAuth refresh-token files. Runtime uses Cloud Run metadata identity.
 */
import { createServer } from 'node:http';
import { createPublicKey, verify as verifySignature } from 'node:crypto';
import { parseReportRow, parseProductRow, parseAuditRow, normalizeUsername, authorizeAction } from './model.mjs';

export const PROJECT = 'pickface-damage-1291';
export const ROOT = '16jDCy5_Z1X5cKJyNPR1rn_ZqbExQSAeC';
export const SHEET = '1Ubm9EhALocUovzVr3UIspdCMtHIPm2NPUw6UjlZcqw4';
export const IMAGES = '1K_lUl_uE4dskR28cVK4iZJrzFUINfvXf';
export const LOGS = '1Z19VgAAmCN1u7z_xSSztx9IVuq3kFSlK';
const DATABASE = 'https://pickface-damage-1291-default-rtdb.asia-southeast1.firebasedatabase.app';
const JWKS_URL = 'https://www.googleapis.com/service_accounts/v1/jwk/securetoken@system.gserviceaccount.com';
const ACTIONS = Object.freeze(['gateway_info', 'verify', 'pull_changes', 'pull_products', 'list_audit', 'get_image', 'release_metadata']);
let cloudToken = null, tokenExpires = 0, jwks = null, jwksExpires = 0;

function enforceRuntimeProject() {
  const env = process.env.GOOGLE_CLOUD_PROJECT || process.env.GCLOUD_PROJECT;
  if (env !== PROJECT) throw new Error('Cloud Run project identity not verified: fail closed.');
  if (process.env.K_SERVICE && process.env.GOOGLE_CLOUD_REGION && process.env.GOOGLE_CLOUD_REGION !== 'asia-southeast1')
    throw new Error('Cloud Run region not authorized.');
}
function limitedString(v, n=200) { return typeof v === 'string' ? v.trim().slice(0,n) : ''; }
function number(v) { return Number.isFinite(Number(v)) ? Number(v) : 0; }
function clamp(v, min, max) { return Math.min(max, Math.max(min, Math.floor(number(v) || min))); }
function json(res, status, value) {
  res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store', 'X-Content-Type-Options':'nosniff' });
  res.end(JSON.stringify(value));
}
async function remote(url, options={}) {
  const response = await fetch(url, { ...options, signal: AbortSignal.timeout(20000), redirect: 'error' });
  return response;
}
async function cloudAccessToken() {
  const now=Date.now();
  if (cloudToken && now < tokenExpires - 90_000) return cloudToken;
  const res = await remote('http://metadata.google.internal/computeMetadata/v1/instance/service-accounts/default/token', {headers:{'Metadata-Flavor':'Google'}});
  if (!res.ok) throw new Error('Cloud Run service identity unavailable.');
  const token = await res.json();
  if (!token.access_token) throw new Error('Cloud Run service identity missing.');
  cloudToken=token.access_token; tokenExpires=now+number(token.expires_in)*1000;
  return cloudToken;
}
async function googleGet(url, accept='application/json') {
  const token = await cloudAccessToken();
  const r = await remote(url, {headers:{'Authorization': 'Bearer '+token, 'Accept': accept}});
  if (!r.ok) throw new Error('Google resource returned HTTP '+r.status+'; verify in-project service identity and root permissions.');
  return r;
}
async function rtGet(path) {
  const token=await cloudAccessToken();
  // Firebase REST recognizes project service-account OAuth access_token; never log query tokens.
  const url = DATABASE+'/'+path+'.json?access_token='+encodeURIComponent(token);
  const r=await remote(url);
  if (!r.ok) throw new Error('Firebase profile source unavailable ('+r.status+').');
  return r.json();
}
async function getJwks() {
  if (jwks && Date.now()<jwksExpires) return jwks;
  const r=await remote(JWKS_URL);
  if (!r.ok) throw new Error('Firebase verification key source unavailable.');
  jwks=await r.json();
  const match=(r.headers.get('cache-control')||'').match(/max-age=(\d+)/);
  jwksExpires=Date.now()+Math.min(3_600_000,Number(match?.[1]||300)*1000);
  return jwks;
}
function readJwtSegment(v) {
  return JSON.parse(Buffer.from(v,'base64url').toString('utf8'));
}
export async function verifyFirebaseIdToken(token) {
  if (!token || token.length>8192) throw new Error('Missing Firebase ID token.');
  const parts=token.split('.');
  if (parts.length!==3) throw new Error('Invalid Firebase token format.');
  const [h,p,s]=parts;
  const header=readJwtSegment(h), claims=readJwtSegment(p);
  if (header.alg!=='RS256' || !header.kid) throw new Error('Unexpected JWT algorithm.');
  const now=Math.floor(Date.now()/1000);
  if (claims.aud!==PROJECT || claims.iss!==('https://securetoken.google.com/'+PROJECT)
    || !claims.sub || typeof claims.sub!=='string' || claims.sub.length>128
    || !(claims.exp>now) || !(claims.iat<=now) || !(claims.auth_time<=now)) throw new Error('Firebase token claim verification failed.');
  const keys=await getJwks();
  const key=keys.keys?.find(k=>k.kid===header.kid && k.kty==='RSA' && k.use==='sig');
  if (!key || !verifySignature('RSA-SHA256',Buffer.from(h+'.'+p),createPublicKey({key,format:'jwk'}),Buffer.from(s,'base64url')))
    throw new Error('Invalid Firebase token signature.');
  return claims;
}
async function verifiedUser(token) {
  const claims=await verifyFirebaseIdToken(token);
  const profile=await rtGet('users/'+encodeURIComponent(claims.sub));
  if (!profile || profile.active!==true || (profile.uid && profile.uid!==claims.sub)) throw new Error('User profile disabled or UID mismatch.');
  return {uid:claims.sub,profile};
}
async function assertDriveFileInFolder(fileId, parentId) {
  if (!/^[a-zA-Z0-9_-]{10,160}$/.test(fileId)) throw new Error('Drive file ID malformed.');
  const url='https://www.googleapis.com/drive/v3/files/'+encodeURIComponent(fileId)+'?fields=id,name,mimeType,parents,trashed,size&supportsAllDrives=true';
  const info=await (await googleGet(url)).json();
  if (info.trashed || !Array.isArray(info.parents) || !info.parents.includes(parentId)) throw new Error('Drive resource is outside fixed project folder.');
  return info;
}
async function assertFixedScope() {
  await assertDriveFileInFolder(SHEET,ROOT);
  await assertDriveFileInFolder(IMAGES,ROOT);
  await assertDriveFileInFolder(LOGS,ROOT);
}
async function values(range) {
  const uri='https://sheets.googleapis.com/v4/spreadsheets/'+encodeURIComponent(SHEET)+'/values/'+encodeURIComponent(range)+'?valueRenderOption=FORMATTED_VALUE';
  const obj=await (await googleGet(uri)).json();
  return obj.values||[];
}
function pageFrom(rows, afterSeq, limit) {
  const sorted=rows.filter(x=>number(x.change_seq)>afterSeq).sort((a,b)=>a.change_seq-b.change_seq);
  return {changes:sorted.slice(0,limit),latest_seq:Math.max(0,...rows.map(x=>number(x.change_seq))),has_more:sorted.length>limit};
}
async function action(request) {
  const name=limitedString(request.action,80);
  const payload=request.payload && typeof request.payload==='object' && !Array.isArray(request.payload) ? request.payload : {};
  if (name==='gateway_info') return {ok:true,version:'cloud-run-migration-readonly-0.1.0',capabilities:ACTIONS,production_ready:false};
  if (name==='release_metadata') {
    const url='https://api.github.com/repos/tamnv2/pickfacedamage1291/releases/latest';
    const resp=await remote(url,{headers:{Accept:'application/vnd.github+json','User-Agent':'PickfaceDamage1291-CloudRun'}});
    if (!resp.ok) throw new Error('Project GitHub release API unavailable.');
    const data=await resp.json();
    const asset=(data.assets||[]).find(x=>/^PickfaceDamage1291-win-x64-v[0-9.]+\.zip$/.test(x.name));
    return {ok:true,tag_name:String(data.tag_name||''),name:String(data.name||''),body:String(data.body||''),html_url:String(data.html_url||''),asset_name:String(asset?.name||''),asset_url:String(asset?.browser_download_url||''),download_transport:'github_only',drive_mirror:false};
  }
  if (!ACTIONS.includes(name)) throw new Error('NOT_IMPLEMENTED: migration write action not yet safe; legacy operation remains unchanged.');
  const user=await verifiedUser(limitedString(request.id_token,8192));
  if (!authorizeAction(name,user.profile)) throw new Error('Permission denied.');
  if (name==='verify') {
    await assertFixedScope();
    // Never mutate headers as a side-effect of verify; required migrations are separate.
    return {ok:true,uid:user.uid,username:String(user.profile.username||''),version:'cloud-run-migration-readonly-0.1.0',capabilities:ACTIONS};
  }
  if (name==='pull_products') {
    const rows=(await values("'SKU_Catalog'!A2:H")).map(parseProductRow).filter(x=>x.sku);
    return {ok:true,...pageFrom(rows,Math.max(0,number(payload.after_seq)),clamp(payload.limit,1,1000)),server_count:rows.length};
  }
  if (name==='pull_changes') {
    const rows=(await values("'Trang tính1'!A2:AD")).map(parseReportRow).filter(x=>x.report.report_id);
    return {ok:true,...pageFrom(rows,Math.max(0,number(payload.after_seq)),clamp(payload.limit,1,1000))};
  }
  if (name==='list_audit') {
    const before=payload.before_server_time_exclusive ? number(payload.before_server_time_exclusive):Number.MAX_SAFE_INTEGER;
    const limit=clamp(payload.limit,1,100);
    const rows=(await values("'AuditHistory'!A2:I")).map(parseAuditRow).filter(x=>x.event_id && x.server_time<before).sort((a,b)=>b.server_time-a.server_time);
    const entries=rows.slice(0,limit);
    return {ok:true,entries,has_more:rows.length>limit,next_before_server_time:rows.length>limit?entries.at(-1).server_time:null};
  }
  if (name==='get_image') {
    const fileId=limitedString(payload.file_id,160);
    const info=await assertDriveFileInFolder(fileId,IMAGES);
    if (number(info.size)>8_000_000) throw new Error('Image too large for migration JSON compatibility endpoint.');
    const uri='https://www.googleapis.com/drive/v3/files/'+encodeURIComponent(fileId)+'?alt=media&supportsAllDrives=true';
    const response=await googleGet(uri,'application/octet-stream');
    const data=Buffer.from(await response.arrayBuffer());
    if (data.length>8_000_000) throw new Error('Image too large for migration JSON compatibility endpoint.');
    return {ok:true,file_id:fileId,name:info.name||'',mime_type:info.mimeType||'application/octet-stream',data_base64:data.toString('base64')};
  }
  throw new Error('NOT_IMPLEMENTED');
}
const bodyLimit=8_000_000;
const server=createServer(async(req,res)=>{
  if (req.url==='/healthz' && req.method==='GET') return json(res,200,{ok:true,project:PROJECT,stage:'READONLY_NOT_READY_FOR_CUTOVER'});
  if (req.url!=='/gateway' || req.method!=='POST') return json(res,404,{ok:false,error:'Not found.'});
  try {
    let chunks=[],size=0;for await(const chunk of req){size+=chunk.length;if(size>bodyLimit)throw new Error('Payload too large.');chunks.push(chunk);}
    const input=JSON.parse(Buffer.concat(chunks).toString('utf8'));
    const output=await action(input);
    json(res,200,output);
  }catch(e){ 
    // Do not expose Google access tokens, stack traces or upstream response bodies.
    const message=e instanceof Error?e.message:'Invalid request.';
    const safe=/^(NOT_IMPLEMENTED|Permission denied|Image too large|Payload too large|Invalid|Missing|Firebase token|User profile|Drive resource is outside|Drive file ID|Cloud Run service identity)/.test(message)?message:'Yêu cầu không thực hiện được; xem Cloud Run diagnostic log đã loại thông tin nhạy cảm.';
    json(res,400,{ok:false,error:safe});
  }
});
if (process.argv[1] && import.meta.url===new URL('file://'+process.argv[1]).href) {
  enforceRuntimeProject();
  server.listen(Number(process.env.PORT||8080),'0.0.0.0');
}
export {action,server,enforceRuntimeProject,pageFrom};
