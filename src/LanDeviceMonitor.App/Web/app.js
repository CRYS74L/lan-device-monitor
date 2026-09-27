import {
  $,
  esc,
  icon,
  deviceIcon,
  hydrateIcons,
  text,
  bytes,
  rate,
  total,
  date,
  clock,
  duration,
  fields,
  sessionsMarkup,
  toast,
  copy,
} from "./ui.js";
import { TrafficChart } from "./chart.js";

hydrateIcons();
document.querySelector(".skip-link").addEventListener("click", (event) => {
  event.preventDefault();
  $("main").focus();
});
const chart = new TrafficChart($("traffic-chart"));
const reduced = matchMedia("(prefers-reduced-motion: reduce)");
const wide = matchMedia(
  "(min-width:801px), (min-width:650px) and (orientation:landscape) and (max-height:550px)",
);
const narrow = {
  get matches() {
    return !wide.matches;
  },
};
let snapshot = null,
  selected = "",
  filter = "all",
  range = 10,
  detailData = null,
  detailController = null;
let timer,
  polling = false,
  connected = false,
  listOrder = "",
  historyCursor = null,
  historyLoading = false,
  historyDevice = "",
  historyGeneration = 0;
let lastRoute = null,
  listScroll = 0;
const rows = new Map(),
  animations = new WeakMap();

async function request(path, signal) {
  const controller = new AbortController();
  const abort = () => controller.abort();
  signal?.addEventListener("abort", abort, { once: true });
  if (signal?.aborted) controller.abort();
  const timeout = setTimeout(abort, 10000);
  try {
    const response = await fetch("/api/" + path, {
      signal: controller.signal,
      cache: "no-store",
    });
    if (!response.ok)
      throw new Error(
        response.status === 404
          ? "该设备已不在列表中"
          : `请求失败（${response.status}）`,
      );
    return await response.json();
  } finally {
    clearTimeout(timeout);
    signal?.removeEventListener("abort", abort);
  }
}
function transition(el, direction = 1) {
  if (reduced.matches) return;
  const prior = animations.get(el);
  const from = prior
    ? {
        transform: getComputedStyle(el).transform,
        opacity: getComputedStyle(el).opacity,
      }
    : { transform: `translateX(${direction * 18}px)`, opacity: 0.35 };
  prior?.cancel();
  const anim = el.animate([from, { transform: "translateX(0)", opacity: 1 }], {
    duration: 230,
    easing: "cubic-bezier(.2,.8,.25,1)",
  });
  animations.set(el, anim);
  anim.finished
    .then(() => {
      if (animations.get(el) === anim) animations.delete(el);
    })
    .catch(() => {});
}
function route() {
  const hash = location.hash.slice(1);
  return {
    network: hash === "network",
    device: hash.startsWith("device=")
      ? new URLSearchParams(hash).get("device")
      : null,
  };
}
function applyRoute(animate = true) {
  if (animate && lastRoute === location.hash) return;
  const wasDetail = document.body.classList.contains("detail-open");
  lastRoute = location.hash;
  const r = route();
  $("devices-page").hidden = r.network;
  $("network-page").hidden = !r.network;
  document.body.classList.toggle("detail-open", !!r.device && !r.network);
  document.querySelectorAll("[data-tab]").forEach((a) => {
    if (a.dataset.tab === (r.network ? "network" : "devices"))
      a.setAttribute("aria-current", "page");
    else a.removeAttribute("aria-current");
  });
  if (r.device) {
    const changed = selected !== r.device;
    selected = r.device;
    if (changed) {
      detailData = null;
      text("detail-footnote", "正在读取设备信息…");
      $("session-preview").innerHTML = "";
      chart.update([], Date.now(), range);
    }
    renderDeviceMeta();
    loadDetail();
  }
  renderList();
  if (r.network) renderNetwork();
  if (animate)
    transition(
      r.network ? $("network-page") : r.device ? $("detail") : $("device-list"),
      r.device ? 1 : -1,
    );
  if (!r.network && r.device && narrow.matches) {
    window.scrollTo(0, 0);
    $("back-button").focus({ preventScroll: true });
  } else if (wasDetail && !r.device && !r.network) {
    window.scrollTo(0, listScroll);
    requestAnimationFrame(() =>
      rows.get(selected)?.focus({ preventScroll: true }),
    );
  } else if (animate && r.network) window.scrollTo(0, 0);
}
function selectDevice(id) {
  if (route().device === id) return;
  if (!route().device) listScroll = window.scrollY;
  history.pushState(
    { lanDevice: true },
    "",
    `#device=${encodeURIComponent(id)}`,
  );
  applyRoute();
}
window.addEventListener("popstate", () => {
  const target = history.state?.lanDialog;
  document.querySelectorAll("dialog").forEach((d) => {
    if (d.open && d.id !== target) animateSheet(d, false);
  });
  if (target && $(target) && !$(target).open) {
    animateSheet($(target), true);
    renderAbout();
  }
  applyRoute();
});
window.addEventListener("hashchange", () => applyRoute());
$("back-button").onclick = () => {
  if (history.state?.lanDevice) history.back();
  else {
    history.replaceState(null, "", "#devices");
    applyRoute();
  }
  requestAnimationFrame(() =>
    rows.get(selected)?.focus({ preventScroll: true }),
  );
};
function renderList() {
  if (!snapshot) return;
  const all = snapshot.devices,
    query = $("search").value.trim().toLocaleLowerCase();
  text("online-count", all.filter((d) => d.online).length);
  text("offline-count", all.filter((d) => !d.online).length);
  text("total-count", `${all.length} 台设备`);
  const visible = all
    .filter(
      (d) =>
        (filter === "all" || d.online === (filter === "online")) &&
        `${d.name} ${d.ip} ${d.mac} ${d.type}`
          .toLocaleLowerCase()
          .includes(query),
    )
    .sort(
      (a, b) =>
        Number(b.online) - Number(a.online) ||
        a.name.localeCompare(b.name, "zh-CN"),
    );
  const ids = new Set(all.map((d) => d.id));
  for (const [id, row] of rows)
    if (!ids.has(id)) {
      row.remove();
      rows.delete(id);
    }
  const showing = new Set(visible.map((d) => d.id));
  for (const [id, row] of rows) row.hidden = !showing.has(id);
  for (const d of visible) {
    let row = rows.get(d.id);
    if (!row) {
      row = document.createElement("button");
      row.className = "device-row";
      row.dataset.id = d.id;
      row.innerHTML =
        '<span class="device-icon"></span><span class="row-info"><span class="row-name"></span><span class="row-meta"></span></span><span class="row-rates"><span class="down"></span><span class="up"></span></span>';
      rows.set(d.id, row);
      $("device-list").append(row);
    }
    row.hidden = false;
    row.classList.toggle("selected", d.id === selected);
    row.classList.toggle("offline", !d.online);
    row.setAttribute("aria-pressed", String(d.id === selected));
    if (row.dataset.kind !== d.iconType) {
      row.querySelector(".device-icon").innerHTML = deviceIcon(d.iconType);
      row.dataset.kind = d.iconType;
    }
    const set = (selector, value) => {
      const el = row.querySelector(selector);
      if (el.textContent !== value) el.textContent = value;
    };
    set(".row-name", d.name);
    set(".row-meta", `${d.connection} · ${d.online ? "在线" : "离线"}`);
    set(".down", d.online ? "↓ " + rate(d.download) : "离线");
    set(".up", d.online ? "↑ " + rate(d.upload) : "");
    row.setAttribute(
      "aria-label",
      `${d.name}，${d.online ? "在线" : "离线"}，${d.connection}`,
    );
  }
  const order = visible.map((d) => d.id).join("|");
  if (order !== listOrder) {
    const focused = document.activeElement?.dataset.id;
    for (const d of visible) $("device-list").append(rows.get(d.id));
    if (focused) rows.get(focused)?.focus({ preventScroll: true });
    listOrder = order;
  }
  $("list-empty").hidden = visible.length > 0;
}
$("device-list").onclick = (e) => {
  const button = e.target.closest("[data-id]");
  if (button) selectDevice(button.dataset.id);
};
$("search").oninput = renderList;
$("filters").onclick = (e) => {
  const button = e.target.closest("[data-filter]");
  if (!button) return;
  filter = button.dataset.filter;
  document
    .querySelectorAll("[data-filter]")
    .forEach((b) => b.setAttribute("aria-pressed", String(b === button)));
  renderList();
};
$("clear-search").onclick = () => {
  $("search").value = "";
  filter = "all";
  document
    .querySelectorAll("[data-filter]")
    .forEach((b) =>
      b.setAttribute("aria-pressed", String(b.dataset.filter === "all")),
    );
  renderList();
  $("search").focus();
};
function device() {
  return snapshot?.devices.find((d) => d.id === selected);
}
function renderDeviceMeta() {
  const d = device();
  $("detail-empty").hidden = !!d;
  $("detail-content").hidden = !d;
  if (!d) return;
  text("device-name", d.name);
  text("device-subtitle", `${d.type} · ${d.connection}`);
  text("device-status", d.online ? "在线" : "离线");
  $("device-status").classList.toggle("offline", !d.online);
  if ($("device-symbol").dataset.kind !== d.iconType) {
    $("device-symbol").innerHTML = deviceIcon(d.iconType);
    $("device-symbol").dataset.kind = d.iconType;
  }
  text("device-ip", d.ip || "暂无 IP 地址");
  text(
    "device-uptime",
    d.online
      ? d.onlineSince
        ? "已在线 " + duration(d.onlineSince)
        : "当前在线"
      : "最后在线 " + date(d.lastSeen),
  );
  for (const [key, n] of [
    ["download", d.download],
    ["upload", d.upload],
  ]) {
    const f = bytes(n, true);
    text(key + "-value", f.value);
    text(key + "-unit", f.unit);
  }
  text("download-total", total(d.totalDownload));
  text("upload-total", total(d.totalUpload));
  text("record-range", "记录范围 · " + d.recordRange);
  text("usage-total", "合计 " + total(d.totalDownload + d.totalUpload));
  text("detail-type", d.type);
  fields($("connection-fields"), [
    ["连接方式", d.connection],
    ["IP 地址", d.ip, true],
    ["MAC 地址", d.mac, true],
  ]);
  fields($("profile-fields"), [
    ["设备类型", d.type],
    ["路由器名称", d.routerName],
    ["原始名称", d.originalName],
    ["自定义名称", d.customName || "未设置"],
    ["自定义类型", d.customType || "自动识别"],
    ["首次发现", date(d.firstSeen)],
    ["最后在线", d.online ? "当前在线" : date(d.lastSeen)],
    ["Windows 上线提醒", d.notifyOnline ? "已开启" : "未开启"],
    ["Windows 离线提醒", d.notifyOffline ? "已开启" : "未开启"],
  ]);
}
async function loadDetail() {
  if (!device()) return;
  detailController?.abort();
  detailController = new AbortController();
  const controller = detailController,
    id = selected;
  try {
    const data = await request(
      "device/" + encodeURIComponent(id),
      controller.signal,
    );
    if (controller.signal.aborted || selected !== id) return;
    detailData = data;
    chart.update(
      data.history,
      data.serverTime,
      range,
      snapshot?.preferences?.refreshIntervalSeconds || 1,
    );
    const html = sessionsMarkup(data.sessions.slice(0, 3));
    if ($("session-preview").innerHTML !== html)
      $("session-preview").innerHTML = html;
    text("history-button", `全部 ${data.totalSessions} 次 ›`);
    text("detail-footnote", "只读查看 · 设置请在电脑端调整");
  } catch (e) {
    if (!controller.signal.aborted)
      text("detail-footnote", "详情暂未更新，正在重试。");
  }
}
document.querySelectorAll("[data-range]").forEach(
  (b) =>
    (b.onclick = () => {
      range = Number(b.dataset.range);
      document
        .querySelectorAll("[data-range]")
        .forEach((el) => el.setAttribute("aria-pressed", String(el === b)));
      if (detailData)
        chart.update(
          detailData.history,
          detailData.serverTime,
          range,
          snapshot?.preferences?.refreshIntervalSeconds || 1,
        );
    }),
);
function renderNetwork() {
  const info = snapshot?.internet;
  const mesh = snapshot?.mesh || [];
  text(
    "network-title",
    info
      ? info.isConnected
        ? "互联网已连接"
        : "互联网未连接"
      : "等待路由器信息",
  );
  text(
    "network-summary",
    mesh.length
      ? `${mesh.length} 个网络节点 · ${snapshot.devices.filter((d) => d.online).length} 台设备在线`
      : "电脑正在读取路由器信息",
  );
  text("network-state", info?.isConnected ? "● 已连接" : "");
  fields($("ipv4-fields"), [
    ["连接类型", info?.ipv4ConnectionType],
    ["IP 地址", info?.ipv4Address, true],
    ["子网掩码", info?.ipv4SubnetMask],
    ["默认网关", info?.ipv4Gateway, true],
    ["DNS", info?.ipv4Dns, true],
  ]);
  fields($("ipv6-fields"), [
    ["连接类型", info?.ipv6ConnectionType],
    ["WAN 地址", info?.wanIpv6Address, true],
    ["WAN 网关", info?.wanIpv6Gateway, true],
    ["LAN 地址", info?.lanIpv6Address, true],
    ["LAN 前缀", info?.lanIpv6Prefix, true],
    ["DNS", info?.ipv6Dns, true],
  ]);
  text("mesh-count", mesh.length ? `${mesh.length} 个节点` : "暂未返回节点");
  text(
    "mesh-note",
    "设备数量为各节点直接连接数。" +
      (snapshot?.meshUpdatedAt
        ? "最近同步 " + clock(snapshot.meshUpdatedAt) + " · 每分钟更新"
        : "正在等待同步。"),
  );
  const signature = JSON.stringify(mesh);
  if ($("mesh-list").dataset.content !== signature) {
    $("mesh-list").dataset.content = signature;
    $("mesh-list").innerHTML = mesh.length
      ? mesh
          .map(
            (n) =>
              `<article class="mesh-node"><span>${icon("router")}</span><div><h4>${esc(n.displayName)}<span class="node-kind">${n.isGateway ? "主路由" : "节点"}</span></h4><p>${esc(n.hardware)} · ${esc(n.connectionText)}</p><p>${esc(n.ipAddress)}</p></div><div class="mesh-device-count"><b>${n.connectedDeviceCount}</b>台设备</div></article>`,
          )
          .join("")
      : '<div class="inline-empty">路由器暂未返回 Mesh 节点信息</div>';
  }
}
function renderAbout() {
  if (!$("about-dialog").open || !snapshot) return;
  const p = snapshot.preferences || {};
  fields($("about-connection"), [
    ["软件版本", p.version],
    ["连接状态", connected ? snapshot.connection : "连接中断"],
    ["最近更新", date(snapshot.refreshedAt)],
    ["刷新间隔", (p.refreshIntervalSeconds || 1) + " 秒"],
    ["路由器地址", p.routerAddress, true],
    ["网页地址", location.origin, true],
  ]);
  fields($("about-preferences"), [
    [
      "桌面主题",
      { System: "跟随系统", Light: "浅色", Dark: "深色" }[p.desktopTheme],
    ],
    ["登录 Windows 后启动", p.startWithWindows ? "已开启" : "未开启"],
    ["关闭窗口后", p.closeToTray ? "继续在托盘运行" : "退出"],
    [
      "通知免打扰",
      p.doNotDisturb ? `${p.quietStart} — ${p.quietEnd}` : "未开启",
    ],
    ["IPv6 非 Native 提醒", p.ipv6Notification ? "已开启" : "未开启"],
    [
      "紧急退出快捷键",
      p.emergencyHotKeyEnabled ? p.emergencyHotKey || "已开启" : "未开启",
    ],
  ]);
}
function animateSheet(el, open) {
  const old = animations.get(el);
  const start = old
    ? {
        transform: getComputedStyle(el).transform,
        opacity: getComputedStyle(el).opacity,
      }
    : null;
  old?.cancel();
  if (open && !el.open) el.showModal();
  if (reduced.matches) {
    if (!open) el.close();
    return;
  }
  const from = start || {
    transform: open ? "translateY(26px)" : "translateY(0)",
    opacity: open ? 0.3 : 1,
  };
  const to = {
    transform: open ? "translateY(0)" : "translateY(26px)",
    opacity: open ? 1 : 0,
  };
  const animation = el.animate([from, to], {
    duration: open ? 260 : 180,
    easing: "cubic-bezier(.22,.8,.25,1)",
  });
  animations.set(el, animation);
  animation.finished
    .then(() => {
      if (animations.get(el) !== animation) return;
      animations.delete(el);
      if (!open) el.close();
    })
    .catch(() => {});
}
function openSheet(id) {
  const el = $(id);
  if (el.open) return;
  history.pushState({ ...history.state, lanDialog: id }, "", location.href);
  animateSheet(el, true);
}
function closeSheet(id) {
  if (history.state?.lanDialog === id) history.back();
  else animateSheet($(id), false);
}
document
  .querySelectorAll("[data-close]")
  .forEach((b) => (b.onclick = () => closeSheet(b.dataset.close)));
document.querySelectorAll("dialog").forEach((d) => {
  d.addEventListener("cancel", (e) => {
    e.preventDefault();
    closeSheet(d.id);
  });
  d.addEventListener("click", (e) => {
    if (e.target !== d) return;
    const r = d.getBoundingClientRect();
    if (
      e.clientX < r.left ||
      e.clientX > r.right ||
      e.clientY < r.top ||
      e.clientY > r.bottom
    )
      closeSheet(d.id);
  });
});
$("about-button").onclick = () => {
  openSheet("about-dialog");
  renderAbout();
};
document.addEventListener("click", (e) => {
  const b = e.target.closest("[data-copy]");
  if (b) copy(b.dataset.copy);
});
$("history-button").onclick = async () => {
  historyDevice = selected;
  historyGeneration++;
  historyCursor = null;
  historyLoading = false;
  $("all-sessions").innerHTML = "";
  $("load-more").hidden = false;
  text("history-result", "");
  text("history-subtitle", device()?.name || "");
  openSheet("history-dialog");
  await loadHistory(true);
};
$("history-dialog").addEventListener("close", () => {
  historyGeneration++;
  historyLoading = false;
});
async function loadHistory(first = false) {
  if (historyLoading) return;
  historyLoading = true;
  $("load-more").disabled = true;
  text("load-more", "正在读取…");
  const generation = historyGeneration;
  try {
    const query = historyCursor
      ? "?before=" + encodeURIComponent(historyCursor)
      : "";
    const data = await request(
      "device/" + encodeURIComponent(historyDevice) + query,
    );
    if (generation !== historyGeneration) return;
    $("all-sessions").insertAdjacentHTML(
      "beforeend",
      sessionsMarkup(data.sessions),
    );
    historyCursor = data.nextBefore;
    $("load-more").hidden = !data.hasMore;
    text(
      "history-result",
      data.hasMore ? "" : `共 ${data.totalSessions} 次会话，已显示全部记录`,
    );
  } catch (e) {
    if (generation === historyGeneration)
      text("history-result", e.message + "，可点击按钮重试。");
  } finally {
    if (generation === historyGeneration) {
      historyLoading = false;
      $("load-more").disabled = false;
      text("load-more", "加载更早的记录");
    }
  }
}
$("load-more").onclick = () => loadHistory();
async function poll(manual = false) {
  if (polling) return;
  polling = true;
  clearTimeout(timer);
  if (manual) $("refresh-button").disabled = true;
  try {
    snapshot = await request("snapshot");
    connected = true;
    const age = Date.now() - new Date(snapshot.refreshedAt).getTime();
    const stale =
      !snapshot.refreshedAt ||
      age >
        Math.max(
          15000,
          (snapshot.preferences?.refreshIntervalSeconds || 1) * 3000,
        );
    $("connection-status").classList.toggle("stale", stale);
    $("connection-status").querySelector("span").textContent = stale
      ? "等待更新"
      : snapshot.connection === "已连接"
        ? "已连接"
        : snapshot.connection;
    $("connection-banner").hidden = !stale;
    text(
      "connection-banner",
      snapshot.connection &&
        !["已连接", "等待监控数据"].includes(snapshot.connection)
        ? snapshot.connection + "。正在等待新的监控数据。"
        : "数据暂未更新，显示的是最近一次记录。请确认电脑与路由器保持连接。",
    );
    if (selected && !snapshot.devices.some((d) => d.id === selected)) {
      selected = "";
      detailData = null;
      history.replaceState(null, "", "#devices");
      applyRoute(false);
      toast("该设备已不在列表中");
    }
    if (!selected && snapshot.devices.length && !route().device)
      selected =
        snapshot.devices.find((d) => d.online)?.id || snapshot.devices[0].id;
    renderList();
    renderDeviceMeta();
    renderNetwork();
    renderAbout();
    if (!route().network) await loadDetail();
    if (manual) toast("已更新");
  } catch (e) {
    connected = false;
    $("connection-status").classList.add("stale");
    $("connection-status").querySelector("span").textContent = "连接中断";
    $("connection-banner").hidden = false;
    text(
      "connection-banner",
      "暂时无法连接电脑。请确认电脑端仍在运行，并连接同一局域网。页面将自动重试。",
    );
  } finally {
    polling = false;
    $("refresh-button").disabled = false;
    timer = setTimeout(poll, document.hidden ? 15000 : 2000);
  }
}
$("refresh-button").onclick = () => poll(true);
document.addEventListener("visibilitychange", () => {
  if (!document.hidden) poll();
});
matchMedia("(prefers-color-scheme: dark)").addEventListener("change", () => {
  if (detailData)
    chart.update(detailData.history, detailData.serverTime, range);
});
applyRoute(false);
poll();
