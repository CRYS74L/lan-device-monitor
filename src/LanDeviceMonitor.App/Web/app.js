'use strict';
const $ = id => document.getElementById(id);
let token = localStorage.getItem('lan-access') || '';
if (location.hash.startsWith('#access=')) {
  token = decodeURIComponent(location.hash.slice(8));
  localStorage.setItem('lan-access', token);
  history.replaceState(null, '', location.pathname);
}
let snapshot = null, selected = '', filter = 'all', page = 'devices', timer, pushConfig, busy = false;
const esc = x => String(x ?? '—').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const date = x => x ? new Date(x).toLocaleString('zh-CN', { hour12:false }) : '—';
function bytes(n, speed = false) {
  if (n == null) return '—';
  let v = Math.max(0, n) / 1024, unit = 0;
  while (v >= 1024 && unit < 2) { v /= 1024; unit++; }
  return `${Number(v.toFixed(2))} ${['KB','MB','GB'][unit]}${speed ? '/s' : ''}`;
}
async function api(path, data) {
  const response = await fetch('/api/' + path, { method:data === undefined ? 'GET' : 'POST', headers:{ Authorization:'Bearer ' + token, ...(data === undefined ? {} : {'Content-Type':'application/json'}) }, body:data === undefined ? undefined : JSON.stringify(data), signal:AbortSignal.timeout(18000), cache:'no-store' });
  if (response.status === 401) { showLogin(); throw new Error('访问码无效或已被重置，请重新连接。'); }
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.error || (response.status === 429 ? '请等待 15 秒后再发送测试。' : `请求失败（${response.status}）`));
  }
  return response.status === 204 ? null : response.json().catch(() => null);
}
function showLogin() { $('login').hidden = false; document.querySelectorAll('.page').forEach(el => el.hidden = true); }
function navigate(next) {
  page = next; document.querySelectorAll('[data-page]').forEach(b => b.classList.toggle('active', b.dataset.page === page));
  if (!$('login').hidden) return;
  document.querySelectorAll('.page').forEach(el => el.hidden = el.id !== page);
  if (page === 'settings') checkPush();
}
document.querySelectorAll('[data-page]').forEach(b => b.onclick = () => navigate(b.dataset.page));
document.querySelectorAll('[data-filter]').forEach(b => b.onclick = () => { filter = b.dataset.filter; document.querySelectorAll('[data-filter]').forEach(el => el.classList.toggle('active', el === b)); renderList(); });
$('search').oninput = renderList;
$('login-form').onsubmit = async e => {
  e.preventDefault(); token = $('access-code').value.trim(); $('login-status').textContent = '正在连接…';
  if (await refresh()) { localStorage.setItem('lan-access', token); $('access-code').value = ''; $('login-status').textContent = ''; }
};
function renderList() {
  if (!snapshot) return;
  const query = $('search').value.toLowerCase();
  const all = snapshot.devices;
  $('count').textContent = `${all.filter(d => d.online).length} 台在线 / ${all.length} 台设备`;
  const devices = all.filter(d => (filter === 'all' || d.online === (filter === 'online')) && `${d.name} ${d.ip} ${d.mac}`.toLowerCase().includes(query));
  const scroll = $('device-list-panel').scrollTop;
  $('device-list').innerHTML = devices.map(d => `<button class="device ${selected === d.id ? 'selected' : ''}" data-id="${esc(d.id)}"><span><strong>${esc(d.name)}</strong><small><span class="${d.online ? 'online' : ''}">● ${d.online ? '在线' : '离线'}</span> · ${esc(d.ip || '无 IP')}</small></span><span class="speed"><span class="down">↓ ${bytes(d.download,true)}</span><small class="up">↑ ${bytes(d.upload,true)}</small></span></button>`).join('') || '<p class="empty">没有符合条件的设备</p>';
  $('device-list-panel').scrollTop = scroll;
  document.querySelectorAll('[data-id]').forEach(b => b.onclick = () => { selected = b.dataset.id; $('devices').classList.add('detail-open'); renderList(); renderDetail(); });
}
function renderDetail() {
  const d = snapshot?.devices.find(x => x.id === selected);
  if (!d) { $('detail').innerHTML = '<p class="empty">选择一台设备，查看连接与流量信息。</p>'; return; }
  const scroll = $('detail').querySelector('.history')?.scrollTop || 0;
  $('detail').innerHTML = `<button class="back">← 设备列表</button><div class="detail-title"><div><h2>${esc(d.name)}</h2><span class="muted">${esc(d.type)}</span></div><span class="badge ${d.online ? 'online' : 'muted'}">${d.online ? '在线' : '离线'}</span></div><div class="meta"><span>${esc(d.connection)}</span><span>IP ${esc(d.ip)}</span><span>MAC ${esc(d.mac)}</span></div><div class="stats"><div class="stat"><small>↓ 当前下载</small><strong class="down">${bytes(d.download,true)}</strong><div class="total">累计 ${bytes(d.totalDownload)}</div></div><div class="stat"><small>↑ 当前上传</small><strong class="up">${bytes(d.upload,true)}</strong><div class="total">累计 ${bytes(d.totalUpload)}</div></div></div><h3>流量趋势</h3><div class="chart-wrap"><canvas class="chart" aria-label="下载蓝色、上传橙色的实时网速曲线" role="img"></canvas><div class="chart-legend"><span>最近 ${d.history.length} 个有效采样 · 空缺表示未采到数据</span><span><span class="down">下载</span> / <span class="up">上传</span></span></div></div><h3>上下线记录</h3><div class="history">${d.sessions.map(s => `<div class="session"><span><span class="online">上线</span><br>${date(s.onlineAt)}</span><span>${s.offlineAt ? '离线' : '<span class="online">仍在线</span>'}<br>${s.offlineAt ? date(s.offlineAt) : '—'}</span></div>`).join('') || '<p class="empty">暂时没有记录</p>'}</div><p>显示最近 100 次会话 · 上线提醒${d.notifyOnline ? '已开启' : '未开启'} · 离线提醒${d.notifyOffline ? '已开启' : '未开启'}</p>`;
  $('detail').querySelector('.back').onclick = () => $('devices').classList.remove('detail-open');
  $('detail').querySelector('.history').scrollTop = scroll;
  drawChart(d.history);
}
function drawChart(samples) {
  const canvas = $('detail').querySelector('canvas'); if (!canvas) return;
  const rect = canvas.getBoundingClientRect(); if (rect.width === 0) return;
  const scale = window.devicePixelRatio || 1; canvas.width = rect.width * scale; canvas.height = rect.height * scale;
  const ctx = canvas.getContext('2d'); ctx.scale(scale,scale); const w = rect.width, h = rect.height;
  const max = Math.max(1024,...samples.flatMap(s => [s.upload || 0,s.download || 0]));
  const styles = getComputedStyle(document.documentElement); ctx.font = '10px system-ui'; ctx.fillStyle = styles.getPropertyValue('--muted'); ctx.fillText(bytes(max,true),10,17);
  ctx.strokeStyle = styles.getPropertyValue('--line'); for (let i=1;i<4;i++) { ctx.beginPath();ctx.moveTo(10,25+(h-40)*i/4);ctx.lineTo(w-10,25+(h-40)*i/4);ctx.stroke(); }
  for (const [key,color] of [['download','--download'],['upload','--upload']]) {
    ctx.strokeStyle = styles.getPropertyValue(color); ctx.lineWidth=2;ctx.beginPath(); let connected=false,previous=0;
    samples.forEach((s,i) => { const at=new Date(s.at).getTime(); if (s[key] == null) {connected=false;return;} const x=10+i/Math.max(1,samples.length-1)*(w-20), y=h-15-s[key]/max*(h-40); if (connected && at-previous<65000)ctx.lineTo(x,y);else ctx.moveTo(x,y);connected=true;previous=at; });ctx.stroke();
  }
  if (!samples.length) ctx.fillText('等待新的流量采样…',20,h/2);
}
function renderRouter() {
  const info = snapshot?.internet;
  const card = (title, fields) => `<div class="panel"><h2>${title}</h2><dl class="fields">${fields.map(([label,value]) => `<div><dt>${label}</dt><dd>${esc(value || '—')}</dd></div>`).join('')}</dl></div>`;
  $('router-content').innerHTML = (info ? card('IPv4 上网信息', [['状态',info.isConnected?'已连接':'未连接'],['连接类型',info.ipv4ConnectionType],['IP 地址',info.ipv4Address],['子网掩码',info.ipv4SubnetMask],['网关',info.ipv4Gateway],['DNS',info.ipv4Dns]]) + card('IPv6 上网信息',[['连接类型',info.ipv6ConnectionType],['WAN 地址',info.wanIpv6Address],['WAN 网关',info.wanIpv6Gateway],['LAN 地址',info.lanIpv6Address],['LAN 前缀',info.lanIpv6Prefix],['DNS',info.ipv6Dns]]) : '<div class="panel"><p>正在等待路由器上网信息。</p></div>') + `<div class="panel"><h2>Mesh 节点</h2>${(snapshot?.mesh || []).map(n => `<div class="node"><strong>${esc(n.displayName)}</strong> <span class="badge">${n.isGateway?'主路由':'节点'}</span><small>${esc(n.ipAddress)} · ${esc(n.hardware)}</small><small>${esc(n.connectionText)} · ${n.connectedDeviceCount} 台设备</small></div>`).join('') || '<p>暂无节点信息，电脑端将定期刷新。</p>'}</div>`;
}
async function refresh() {
  clearTimeout(timer);
  if (!token) { showLogin(); $('status').textContent='尚未连接'; return false; }
  try {
    snapshot = await api('snapshot'); $('login').hidden=true; navigate(page);
    const stale = !snapshot.refreshedAt || Date.now()-new Date(snapshot.refreshedAt).getTime()>15000;
    $('status').textContent = `${snapshot.connection} · ${stale ? '数据尚未更新，请检查电脑端连接' : '最近更新 '+date(snapshot.refreshedAt)}`;
    $('status').classList.toggle('error',stale);
    if (!selected && snapshot.devices.length) selected=snapshot.devices[0].id;
    renderList(); renderDetail(); renderRouter(); return true;
  } catch(e) { $('status').textContent=e.message+' 现有数据可能已过期。'; $('status').classList.add('error'); $('login-status').textContent=e.message; return false; }
  finally { timer=setTimeout(refresh,document.hidden?15000:2000); }
}
function pushSupport() {
  if (!isSecureContext) return '当前连接未受信任，请先安装并信任电脑提供的 CA 证书。';
  const apple = /iPhone|iPad|iPod/.test(navigator.userAgent) || navigator.platform === 'MacIntel' && navigator.maxTouchPoints>1;
  if (apple && !matchMedia('(display-mode: standalone)').matches && !navigator.standalone) return '请先添加到主屏幕，然后从主屏幕图标打开并启用通知。';
  if (!('serviceWorker' in navigator) || !('PushManager' in window) || !('Notification' in window)) return '此浏览器不支持 Web Push，请换用支持的浏览器。';
  if (Notification.permission === 'denied') return '通知权限已被拒绝，请在系统或浏览器设置中允许后重试。';
  return '';
}
let checkingPush=false;
async function checkPush() {
  if(checkingPush || busy) return; checkingPush=true;
  try {
    const unsupported=pushSupport(); $('push-state').textContent=unsupported || '可以启用系统通知';
    $('subscribe').disabled=!!unsupported; $('test-push').disabled=true;
    pushConfig=await api('push');
    if(!pushConfig.enabled){$('push-state').textContent='电脑端已关闭网页通知';$('subscribe').disabled=true;return;}
    if(!unsupported){ const reg=await navigator.serviceWorker.register('/sw.js'); const sub=await reg.pushManager.getSubscription(); const registered=sub && (await api('subscription-status',serialize(sub))).registered; $('push-state').textContent=registered?'本浏览器已订阅。可发送测试确认投递。':sub?'电脑端尚未登记此订阅，请重新点击启用通知。':'尚未启用本设备通知'; $('test-push').disabled=!registered; }
  } catch(e) { $('push-state').textContent=e.message; } finally {checkingPush=false;}
}
function keyBytes(key){const raw=atob(key.replace(/-/g,'+').replace(/_/g,'/').padEnd(Math.ceil(key.length/4)*4,'='));return Uint8Array.from(raw,c=>c.charCodeAt(0));}
function serialize(sub){const json=sub.toJSON();return { endpoint:json.endpoint,p256dh:json.keys.p256dh,auth:json.keys.auth };}
async function pushAction(action){if(busy)return;busy=true;$('push-result').textContent='正在处理…';try{await action();}catch(e){$('push-result').textContent=e.message;}finally{busy=false;await checkPush();}}
$('subscribe').onclick=()=>{
  // Request permission directly inside the gesture, before asynchronous network work (Safari).
  const permission=Notification.requestPermission();
  pushAction(async()=>{ if(await permission!=='granted')throw new Error('未获通知授权。'); const reg=await navigator.serviceWorker.ready;
    const config=await api('push'); if(!config.enabled)throw new Error('电脑端已关闭网页通知');
    let sub=await reg.pushManager.getSubscription(); if(!sub)sub=await reg.pushManager.subscribe({userVisibleOnly:true,applicationServerKey:keyBytes(config.publicKey)});
    try{await api('subscribe',serialize(sub));}catch(e){await sub.unsubscribe();throw e;}
    $('push-result').textContent='订阅已保存。请发送测试并在锁屏状态确认通知。'; });
};
async function unsubscribe(){const reg=await navigator.serviceWorker.getRegistration();const sub=await reg?.pushManager.getSubscription();if(sub){await api('unsubscribe',serialize(sub));await sub.unsubscribe();}}
$('unsubscribe').onclick=()=>pushAction(async()=>{await unsubscribe();$('push-result').textContent='已关闭本设备通知。';});
$('test-push').onclick=()=>pushAction(async()=>{const reg=await navigator.serviceWorker.ready;const sub=await reg.pushManager.getSubscription();if(!sub)throw new Error('请先启用通知');const result=await api('test',serialize(sub));$('push-result').textContent=result.status;});
$('logout').onclick=async()=>{try{if('serviceWorker' in navigator)await unsubscribe();}catch(e){$('login-status').textContent='未能撤销订阅，请在浏览器设置中关闭通知：'+e.message;}token='';localStorage.removeItem('lan-access');clearTimeout(timer);showLogin();$('status').textContent='已退出';};
window.addEventListener('resize',()=>{const d=snapshot?.devices.find(x=>x.id===selected);if(d)drawChart(d.history);});
document.addEventListener('visibilitychange',()=>{if(!document.hidden)refresh();});
refresh();
