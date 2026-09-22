'use strict';
const $ = id => document.getElementById(id);
let snapshot = null, selected = '', filter = 'all', page = 'devices', timer;
const esc = x => String(x ?? '—').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const date = x => x ? new Date(x).toLocaleString('zh-CN', { hour12:false }) : '—';
function bytes(n, speed = false) {
  if (n == null) return '—';
  let v = Math.max(0, n) / 1024, unit = 0;
  while (v >= 1024 && unit < 2) { v /= 1024; unit++; }
  return `${Number(v.toFixed(2))} ${['KB','MB','GB'][unit]}${speed ? '/s' : ''}`;
}
async function api(path) {
  const response = await fetch('/api/' + path, { signal:AbortSignal.timeout(10000), cache:'no-store' });
  if (!response.ok) throw new Error(`连接失败（${response.status}）`);
  return response.json();
}
function navigate(next) {
  page = next; document.querySelectorAll('[data-page]').forEach(b => b.classList.toggle('active', b.dataset.page === page));
  document.querySelectorAll('.page').forEach(el => el.hidden = el.id !== page);
}
document.querySelectorAll('[data-page]').forEach(b => b.onclick = () => navigate(b.dataset.page));
document.querySelectorAll('[data-filter]').forEach(b => b.onclick = () => { filter = b.dataset.filter; document.querySelectorAll('[data-filter]').forEach(el => el.classList.toggle('active', el === b)); renderList(); });
$('search').oninput = renderList;
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
  $('detail').innerHTML = `<button class="back">← 设备列表</button><div class="detail-title"><div><h2>${esc(d.name)}</h2><span class="muted">${esc(d.type)}</span></div><span class="badge ${d.online ? 'online' : 'muted'}">${d.online ? '在线' : '离线'}</span></div><div class="meta"><span>${esc(d.connection)}</span><span>IP ${esc(d.ip)}</span><span>MAC ${esc(d.mac)}</span></div><div class="stats"><div class="stat"><small>↓ 当前下载</small><strong class="down">${bytes(d.download,true)}</strong><div class="total">累计 ${bytes(d.totalDownload)}</div></div><div class="stat"><small>↑ 当前上传</small><strong class="up">${bytes(d.upload,true)}</strong><div class="total">累计 ${bytes(d.totalUpload)}</div></div></div><h3>流量趋势</h3><div class="chart-wrap"><canvas class="chart" aria-label="下载蓝色、上传橙色的实时网速曲线" role="img"></canvas><div class="chart-legend"><span>最近 ${d.history.length} 个有效采样 · 空缺表示未采到数据</span><span><span class="down">下载</span> / <span class="up">上传</span></span></div></div><h3>上下线记录</h3><div class="history">${d.sessions.map(s => `<div class="session"><span><span class="online">上线</span><br>${date(s.onlineAt)}</span><span>${s.offlineAt ? '离线' : '<span class="online">仍在线</span>'}<br>${s.offlineAt ? date(s.offlineAt) : '—'}</span></div>`).join('') || '<p class="empty">暂时没有记录</p>'}</div><p>显示最近 100 次会话</p>`;
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
  try {
    snapshot = await api('snapshot'); navigate(page);
    const stale = !snapshot.refreshedAt || Date.now()-new Date(snapshot.refreshedAt).getTime()>15000;
    $('status').textContent = `${snapshot.connection} · ${stale ? '数据尚未更新，请检查电脑端连接' : '最近更新 '+date(snapshot.refreshedAt)}`;
    $('status').classList.toggle('error',stale);
    if (!selected && snapshot.devices.length) selected=snapshot.devices[0].id;
    renderList(); renderDetail(); renderRouter(); return true;
  } catch(e) { $('status').textContent=e.message+' 现有数据可能已过期。'; $('status').classList.add('error'); return false; }
  finally { timer=setTimeout(refresh,document.hidden?15000:2000); }
}
window.addEventListener('resize',()=>{const d=snapshot?.devices.find(x=>x.id===selected);if(d)drawChart(d.history);});
document.addEventListener('visibilitychange',()=>{if(!document.hidden)refresh();});
refresh();
