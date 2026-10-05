import {
  $,
  esc,
  icon,
  deviceIcon,
  hydrateIcons,
  text,
  bytes,
  date,
  clock,
  duration,
  relativeTime,
  dateMarkup,
  sessionsMarkup,
  fields,
  toast,
  copy,
} from "./ui.js";
import { TrafficChart } from "./chart.js";
import { DesktopPrecision, desktopRound } from "./precision.js";

hydrateIcons();
const themeSelect = $("theme-mode");
const themeTrigger = document.createElement("button");
themeTrigger.className = "theme-trigger";
themeTrigger.id = "theme-trigger";
themeTrigger.setAttribute("role", "combobox");
themeTrigger.setAttribute("aria-label", "界面主题");
themeTrigger.setAttribute("aria-haspopup", "listbox");
themeTrigger.setAttribute("aria-expanded", "false");
themeTrigger.setAttribute("aria-controls", "theme-menu");
themeTrigger.innerHTML =
  '<span class="theme-value"></span>' + icon("chevron-down");
themeSelect.hidden = true;
themeSelect.parentElement.append(themeTrigger);
themeSelect.parentElement.querySelector(":scope >[data-icon]").hidden = true;
const themeMenu = document.createElement("div");
themeMenu.id = "theme-menu";
themeMenu.className = "theme-menu";
themeMenu.setAttribute("role", "listbox");
themeMenu.setAttribute("aria-label", "界面主题");
themeMenu.hidden = true;
themeMenu.innerHTML = [
  ["System", "跟随系统"],
  ["Light", "浅色模式"],
  ["Dark", "深色模式"],
]
  .map(
    ([value, label]) =>
      `<button role="option" data-mode="${value}" aria-selected="false"><span>${label}</span></button>`,
  )
  .join("");
document.body.append(themeMenu);
for (const card of document.querySelectorAll(".metric-card")) {
  const content = document.createElement("div");
  content.className = "metric-content";
  content.append(
    card.querySelector(".metric-icon"),
    card.querySelector(":scope >div"),
  );
  card.prepend(content);
}
const chart = new TrafficChart($("traffic-chart"));
const reduced = matchMedia("(prefers-reduced-motion: reduce)");
const compact = matchMedia("(max-width:899px)");
const systemDark = matchMedia("(prefers-color-scheme: dark)");
const state = {
  snapshot: null,
  selected: "",
  filter: "online",
  page: "device",
  displayed: "",
  range: 10,
  detail: null,
  sessions: [],
  total: 0,
  visible: 3,
  cursor: null,
  connected: false,
  nav: 0,
  history: 0,
  compactOpen: false,
};
const rows = new Map();
const rowHistory = new Map();
const filterSelection = { online: "", offline: "" };
let detailController,
  historyController,
  selectionGeneration = 0,
  detailGeneration = 0,
  pollTimer,
  polling = false,
  layoutFrame = 0,
  sidebarExpandedWidth = 390,
  sidebarCollapsed = false;
let localTheme = null;
try {
  localTheme = localStorage.getItem("lan-desktop-web-theme");
} catch {}
let pendingNavigation = false,
  sidebarTimer;
let precision;
const motion = new WeakMap();
function cancelMotion(el) {
  motion.get(el)?.cancel();
  motion.delete(el);
  if (el) delete el.dataset.motion;
}
async function animate(el, keyframes, options) {
  if (!el || reduced.matches) return;
  const previous = motion.get(el);
  previous?.cancel();
  const animation = el.animate(keyframes, { fill: "both", ...options });
  motion.set(el, animation);
  el.dataset.motion = "active";
  try {
    await animation.finished;
  } catch {}
  if (motion.get(el) === animation) {
    motion.delete(el);
    animation.cancel();
    delete el.dataset.motion;
  }
}
async function request(path, signal) {
  const controller = new AbortController();
  const abort = () => controller.abort();
  signal?.addEventListener("abort", abort, { once: true });
  if (signal?.aborted) abort();
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
function sortedDevices() {
  return (state.snapshot?.devices || [])
    .filter((d) => d.online === (state.filter === "online"))
    .sort(
      (a, b) =>
        new Date(b.listSortAt || b.statusChangedAt || b.lastSeen) -
          new Date(a.listSortAt || a.statusChangedAt || a.lastSeen) ||
        a.name.localeCompare(b.name, "zh-CN"),
    );
}
function selectedDevice() {
  return (
    state.snapshot?.devices.find(
      (d) => d.id.toLowerCase() === state.selected.toLowerCase(),
    ) || state.detail?.device
  );
}
function selectContext(id) {
  if (id === state.selected) return false;
  state.selected = id;
  state.displayed = "";
  ++selectionGeneration;
  detailController?.abort();
  historyController?.abort();
  state.detail = null;
  state.sessions = [];
  state.total = 0;
  state.visible = 3;
  state.cursor = null;
  rowHistory.clear();
  $("session-list").replaceChildren();
  $("history-reveal").dataset.shown = "0";
  cancelMotion($("history-reveal"));
  return true;
}
function setSegment(group, index) {
  group.style.setProperty("--selected", index);
  group
    .querySelectorAll("button")
    .forEach((el, i) => el.setAttribute("aria-pressed", String(i === index)));
}
function applyTheme() {
  const prefs = state.snapshot?.preferences || {};
  const mode = localTheme || prefs.desktopTheme || "System";
  const actual = localTheme
    ? mode === "System"
      ? systemDark.matches
        ? "Dark"
        : "Light"
      : mode
    : prefs.desktopThemeResolved ||
      (mode === "System" ? (systemDark.matches ? "Dark" : "Light") : mode);
  document.documentElement.dataset.theme = actual === "Dark" ? "Dark" : "Light";
  document.querySelector("meta[name=theme-color]").content =
    actual === "Dark" ? "#231f17" : "#f3eee2";
  if (themeSelect.value !== mode) themeSelect.value = mode;
  themeTrigger.querySelector(".theme-value").textContent =
    themeSelect.selectedOptions[0]?.textContent || "跟随系统";
  themeMenu
    .querySelectorAll("[data-mode]")
    .forEach((option) =>
      option.setAttribute(
        "aria-selected",
        String(option.dataset.mode === mode),
      ),
    );
}
function setInput(id, value) {
  const el = $(id),
    s = String(value ?? "");
  if (el.value !== s) el.value = s;
}
function setSwitch(id, value) {
  $(id).setAttribute("aria-checked", String(!!value));
}
function renderSettings() {
  const p = state.snapshot?.preferences || {};
  applyTheme();
  setInput("router-address", p.routerAddress);
  setInput("router-password", p.hasRouterPassword ? "••••••••" : "");
  text("refresh-interval", "");
  $("refresh-interval").innerHTML =
    `<span>${esc(p.refreshIntervalSeconds || 1)} 秒</span><span data-icon="chevron-down">${icon("chevron-down")}</span>`;
  setSwitch("start-with-windows", p.startWithWindows);
  setSwitch("do-not-disturb", p.doNotDisturb);
  setSwitch("new-device-notify", p.newDeviceNotification);
  setSwitch("ipv6-notify", p.ipv6Notification);
  setSwitch("lan-access", p.lanAccessEnabled ?? true);
  document.querySelector(".lan-address-row").hidden = !(
    p.lanAccessEnabled ?? true
  );
  $("quiet-times").hidden = !p.doNotDisturb;
  setInput("quiet-start", p.quietStart || "22:00");
  setInput("quiet-end", p.quietEnd || "08:00");
  setInput("lan-address", location.origin + "/");
  text("app-version", p.version || "");
}
function renderConnection() {
  const s = state.snapshot;
  const healthy = state.connected && s?.connection === "已连接";
  $("connection-indicator").classList.toggle("connected", healthy);
  text(
    "connection-status",
    healthy && s.refreshedAt
      ? "更新于 " + clock(s.refreshedAt)
      : s?.connection || "尚未连接",
  );
}
function renderList() {
  const snapshot = state.snapshot;
  if (!snapshot) return;
  text("online-count", snapshot.devices.filter((d) => d.online).length);
  text("offline-count", snapshot.devices.filter((d) => !d.online).length);
  const visible = sortedDevices(),
    existing = new Set(snapshot.devices.map((d) => d.id));
  for (const [id, row] of rows)
    if (!existing.has(id)) {
      row.remove();
      rows.delete(id);
    }
  const shown = new Set(visible.map((d) => d.id));
  for (const [id, row] of rows) {
    row.hidden = !shown.has(id);
    if (row.hidden) {
      row.tabIndex = -1;
      row.classList.remove("selected");
      row.setAttribute("aria-selected", "false");
    }
  }
  const now = snapshot.refreshedAt || Date.now();
  const focusId = visible.some((d) => d.id === state.selected)
    ? state.selected
    : visible[0]?.id;
  for (const d of visible) {
    let row = rows.get(d.id);
    if (!row) {
      row = document.createElement("button");
      row.className = "device-row";
      row.dataset.id = d.id;
      row.setAttribute("role", "option");
      row.innerHTML =
        '<span class="device-icon"></span><span class="row-name"></span><span class="row-elapsed"></span>';
      rows.set(d.id, row);
    }
    row.hidden = false;
    row.classList.toggle("selected", d.id === state.selected);
    row.classList.toggle("offline", !d.online);
    row.setAttribute("aria-selected", String(d.id === state.selected));
    row.tabIndex = d.id === focusId ? 0 : -1;
    const kind = d.iconType || d.type;
    if (row.dataset.kind !== kind) {
      row.querySelector(".device-icon").innerHTML = deviceIcon(kind);
      row.dataset.kind = kind;
    }
    row.querySelector(".row-name").textContent = d.displayName || d.name;
    const since =
      d.statusChangedAt || (d.online ? d.onlineSince : d.offlineSince);
    row.querySelector(".row-elapsed").textContent = relativeTime(since, now);
    row.setAttribute(
      "aria-label",
      `${d.name}，${d.online ? "在线" : "离线"}，${relativeTime(since, now)}`,
    );
  }
  // Move only out-of-order nodes; polling does not recreate focused controls.
  let cursor = $("device-list").firstElementChild;
  for (const d of visible) {
    const row = rows.get(d.id);
    if (row !== cursor) $("device-list").insertBefore(row, cursor);
    cursor = row.nextElementSibling;
  }
  updateScrollMask($("device-list"));
  scheduleLayout();
}
function renderDevice(d, data = state.detail) {
  if (!d) return;
  text("device-name", d.displayName || d.name);
  $("device-heading")?.classList.toggle("offline", !d.online);
  const heading = $("device-icon").parentElement;
  heading.classList.toggle("offline", !d.online);
  const kind = d.iconType || d.type;
  if ($("device-icon").dataset.kind !== kind) {
    $("device-icon").innerHTML = deviceIcon(kind);
    $("device-icon").dataset.kind = kind;
  }
  text("device-status", d.online ? "在线" : "离线");
  text(
    "device-connection",
    d.connection === "网线接入" ? "网线" : d.connection,
  );
  text("device-ip", d.ip || "—");
  text("device-mac", d.mac);
  for (const [field, value, speed] of [
    ["download", d.download, true],
    ["upload", d.upload, true],
    ["download-total", d.totalDownload, false],
    ["upload-total", d.totalUpload, false],
  ]) {
    const parts = bytes(value, speed);
    text(field + "-value", parts.value);
    text(field + "-unit", parts.unit);
  }
  const range = dateMarkup(d.recordRange || "暂无记录");
  for (const id of ["download-range", "upload-range"])
    if ($(id).innerHTML !== range) $(id).innerHTML = range;
  setInput("custom-name", d.customName);
  text("device-type-name", d.type);
  $("device-type-icon").innerHTML = deviceIcon(kind);
  setSwitch("device-notify", d.notifyOnline || d.notifyOffline);
  $("device-management").hidden = d.online;
  chart.update(
    data?.history || [],
    data?.serverTime || state.snapshot?.refreshedAt || Date.now(),
    state.range,
    state.snapshot?.preferences?.refreshIntervalSeconds || 1,
  );
  renderSessions(false);
  scheduleLayout();
}
function mergeSessions(items) {
  const map = new Map(state.sessions.map((s) => [s.onlineAt, s]));
  for (const s of items || []) map.set(s.onlineAt, s);
  state.sessions = [...map.values()]
    .sort((a, b) => new Date(b.onlineAt) - new Date(a.onlineAt))
    .slice(0, 100);
}
async function loadDetail(id) {
  detailController?.abort();
  detailController = new AbortController();
  const controller = detailController,
    generation = ++detailGeneration;
  try {
    const data = await request(
      "device/" + encodeURIComponent(id),
      controller.signal,
    );
    if (
      generation !== detailGeneration ||
      controller.signal.aborted ||
      id !== state.selected
    )
      return null;
    state.detail = data;
    mergeSessions(data.sessions);
    state.total = Math.min(
      100,
      data.totalSessions || data.device.sessionCount || 0,
    );
    if (!state.cursor) state.cursor = data.nextBefore;
    if (state.displayed === id && !pendingNavigation)
      renderDevice(data.device, data);
    return data;
  } catch (error) {
    if (error.name !== "AbortError" && state.displayed === id)
      toast(error.message);
    return null;
  }
}
function renderSessions(userAction = false) {
  const list = $("session-list"),
    oldHeight = $("history-reveal").getBoundingClientRect().height;
  const now =
    state.detail?.serverTime || state.snapshot?.refreshedAt || Date.now();
  const active = new Set();
  for (const s of state.sessions) {
    active.add(s.onlineAt);
    let row = rowHistory.get(s.onlineAt);
    if (!row) {
      const holder = document.createElement("div");
      holder.innerHTML = sessionsMarkup([s], now);
      row = holder.firstElementChild;
      rowHistory.set(s.onlineAt, row);
      list.append(row);
    } else {
      const replacement = document.createElement("div");
      replacement.innerHTML = sessionsMarkup([s], now);
      const fresh = replacement.firstElementChild;
      const times = row.querySelector(".session-times");
      if (times.innerHTML !== fresh.querySelector(".session-times").innerHTML)
        times.innerHTML = fresh.querySelector(".session-times").innerHTML;
      const badge = row.querySelector(".session-duration"),
        content = duration(s.onlineAt, s.offlineAt || now);
      if (badge.textContent !== content) badge.textContent = content;
    }
  }
  for (const [key, row] of rowHistory)
    if (!active.has(key)) {
      row.remove();
      rowHistory.delete(key);
    }
  let cursor = list.firstElementChild;
  for (const s of state.sessions) {
    const row = rowHistory.get(s.onlineAt);
    if (row !== cursor) list.insertBefore(row, cursor);
    cursor = row.nextElementSibling;
  }
  const count = Math.min(state.visible, state.total, state.sessions.length),
    expanded = state.total > 3 && count === state.total;
  const toggle = $("history-toggle");
  toggle.hidden = state.total <= 3;
  toggle.setAttribute("aria-expanded", String(expanded));
  toggle.setAttribute(
    "aria-label",
    expanded ? "收起到最近三条上下线记录" : "最多再展示十五条上下线记录",
  );
  if (!userAction && motion.has($("history-reveal"))) {
    scheduleLayout();
    return;
  }
  const generation = ++state.history;
  cancelMotion($("history-reveal"));
  const previousShown = [...list.children].filter((row) => !row.hidden).length;
  const duringCount =
    userAction && !reduced.matches ? Math.max(previousShown, count) : count;
  for (let i = 0; i < list.children.length; i++)
    list.children[i].hidden = i >= duringCount;
  layoutSessions();
  const last = list.children[count - 1],
    height = last ? last.offsetTop + last.offsetHeight : 0;
  $("history-reveal").style.height = height + "px";
  if (userAction && !reduced.matches) {
    if (count < 3) return;
    animate(
      $("history-reveal"),
      [{ height: oldHeight + "px" }, { height: height + "px" }],
      {
        duration: height > oldHeight ? 420 : 320,
        easing:
          height > oldHeight
            ? "cubic-bezier(.22,1,.36,1)"
            : "cubic-bezier(.65,0,.35,1)",
      },
    ).then(() => {
      if (generation === state.history) {
        for (let i = 0; i < list.children.length; i++)
          list.children[i].hidden = i >= count;
        $("history-reveal").dataset.shown = count;
        $("history-reveal").style.height = height + "px";
        updateScrollMask($("device-page"));
      }
    });
  } else $("history-reveal").dataset.shown = count;
  updateScrollMask($("device-page"));
}
async function toggleHistory() {
  const id = state.selected,
    generation = selectionGeneration;
  historyController?.abort();
  historyController = new AbortController();
  const target =
    state.visible >= state.total
      ? 3
      : Math.min(state.visible + 15, state.total);
  $("history-toggle").disabled = true;
  try {
    while (target > state.sessions.length && state.cursor) {
      const page = await request(
        "device/" +
          encodeURIComponent(id) +
          "?before=" +
          encodeURIComponent(state.cursor),
        historyController.signal,
      );
      if (id !== state.selected || generation !== selectionGeneration) return;
      const previous = state.sessions.length;
      mergeSessions(page.sessions);
      state.cursor = page.hasMore ? page.nextBefore : null;
      if (state.sessions.length === previous) break;
    }
    if (id === state.selected) {
      state.visible = Math.min(target, state.sessions.length);
      renderSessions(true);
    }
  } catch (error) {
    if (error.name !== "AbortError") toast(error.message);
  } finally {
    $("history-toggle").disabled = false;
  }
}
function renderRouter() {
  const info = state.snapshot?.internet,
    mesh = state.snapshot?.mesh || [];
  text(
    "router-state",
    info ? (info.isConnected ? "互联网已连接" : "互联网未连接") : "正在读取",
  );
  $("router-state").classList.toggle("offline", !!info && !info.isConnected);
  $("router-loading").hidden = !!info;
  fields($("ipv4-fields"), [
    ["连接类型", info?.ipv4ConnectionType],
    ["IP 地址", info?.ipv4Address],
    ["子网掩码", info?.ipv4SubnetMask],
    ["默认网关", info?.ipv4Gateway],
    ["DNS", info?.ipv4Dns],
  ]);
  fields($("ipv6-fields"), [
    ["连接类型", info?.ipv6ConnectionType],
    ["WAN 地址", info?.wanIpv6Address],
    ["WAN 网关", info?.wanIpv6Gateway],
    ["LAN 地址", info?.lanIpv6Address],
    ["LAN 前缀", info?.lanIpv6Prefix],
    ["DNS", info?.ipv6Dns],
  ]);
  const signature = JSON.stringify(mesh);
  if ($("mesh-list").dataset.content !== signature) {
    $("mesh-list").dataset.content = signature;
    $("mesh-list").innerHTML = mesh
      .map(
        (n) =>
          `<article class="mesh-node ${n.isGateway ? "gateway" : "child"}"><span class="mesh-icon">${deviceIcon("路由器")}</span><div><div class="mesh-title"><strong>${esc(n.displayName)}</strong><span class="mesh-role">${n.isGateway ? "主路由" : "Mesh 节点"}</span>${!n.isGateway && n.connectionText && n.connectionText !== "Mesh 节点" ? `<span class="mesh-backhaul">${esc(n.connectionText)}</span>` : ""}</div><div class="mesh-ip">${esc(n.ipAddress)}</div><div class="mesh-hardware">${esc(n.hardware)}</div></div><div class="mesh-count"><strong>${Math.max(0, Number(n.connectedDeviceCount) || 0)}</strong><small>连接设备</small></div></article>`,
      )
      .join("");
  }
  scheduleLayout();
}
function updateHash(page, id) {
  const hash =
    page === "device"
      ? id
        ? "#device=" + encodeURIComponent(id)
        : "#devices"
      : "#" + page;
  if (location.hash !== hash) history.pushState({ lanPage: page }, "", hash);
}
async function navigate(
  page,
  id = state.selected,
  { animate: play = true, hash = true } = {},
) {
  const generation = ++state.nav;
  pendingNavigation = true;
  if (page === "device" && id)
    id =
      state.snapshot?.devices.find(
        (d) => d.id.toLowerCase() === id.toLowerCase(),
      )?.id || id;
  const changed =
    page === "device" &&
    (id !== state.selected || id !== state.detail?.device?.id);
  closeAllRangeTips();
  closeThemeMenu();
  const previousPage = state.page;
  const fromList = compact.matches && !state.compactOpen;
  const outgoing = fromList
    ? $("sidebar")
    : document.querySelector(".page:not([hidden])") || $("empty-panel");
  stopScrollMotion(outgoing);
  stopScrollMotion($("device-page"));
  const staticPage = page !== "device" || previousPage !== "device";
  const exit = fromList || !staticPage ? 150 : 260,
    enter = fromList || !staticPage ? 450 : 780;
  let detailPromise;
  if (page === "device") {
    selectContext(id || "");
    if (id) {
      filterSelection[state.filter] = id;
      detailPromise = loadDetail(id);
    }
    renderList();
  }
  if (hash) updateHash(page, id);
  if (play && outgoing && !outgoing.hidden) {
    const cs = getComputedStyle(outgoing);
    await animate(
      outgoing,
      [
        {
          opacity: cs.opacity,
          transform:
            cs.transform === "none" ? "translateY(0) scale(1)" : cs.transform,
        },
        { opacity: 0, transform: "translateY(-8px) scale(.985)" },
      ],
      { duration: exit, easing: "cubic-bezier(.55,0,1,.45)" },
    );
  }
  if (generation !== state.nav) return;
  state.page = page;
  state.compactOpen = compact.matches;
  document.body.classList.toggle("compact-detail", state.compactOpen);
  document.querySelectorAll(".page").forEach((el) => {
    el.hidden = true;
    cancelMotion(el);
  });
  $("empty-panel").hidden = true;
  let target;
  if (page === "device") {
    const d = selectedDevice();
    target = d ? $("device-page") : $("empty-panel");
    if (d) {
      renderDevice(d, state.detail);
      state.displayed = d.id;
      if (changed) $("device-page").scrollTop = 0;
    }
  } else {
    target = $(page + "-page");
    if (page === "router") renderRouter();
    else renderSettings();
  }
  target.hidden = false;
  pendingNavigation = false;
  scheduleLayout();
  if (play)
    animate(
      target,
      [
        { opacity: 0, transform: "translateY(18px) scale(.975)" },
        { opacity: 1, transform: "translateY(0) scale(1)" },
      ],
      {
        duration: enter,
        delay: staticPage && !fromList ? 120 : 0,
        easing: "cubic-bezier(.22,1,.36,1)",
      },
    );
  if (detailPromise)
    detailPromise.then((data) => {
      if (data && generation === state.nav && state.selected === id)
        renderDevice(data.device, data);
    });
}
function openList(push = true) {
  ++state.nav;
  pendingNavigation = false;
  state.compactOpen = false;
  document.body.classList.remove("compact-detail");
  for (const el of document.querySelectorAll(".page")) cancelMotion(el);
  cancelMotion($("sidebar"));
  stopScrollMotion($("device-page"));
  closeAllRangeTips();
  if (push && location.hash !== "#devices")
    history.pushState({ lanPage: "list" }, "", "#devices");
  rows.get(state.selected)?.focus({ preventScroll: true });
  updateScrollMask($("device-list"));
}
function route() {
  const hash = location.hash.slice(1);
  if (hash === "settings") return { page: "settings" };
  if (hash === "router" || hash === "network") return { page: "router" };
  if (hash.startsWith("device="))
    return { page: "device", id: new URLSearchParams(hash).get("device") };
  return { page: compact.matches ? "list" : "device" };
}
function applyRoute(play = true) {
  const r = route();
  if (r.page === "list") {
    openList(false);
    return;
  }
  navigate(r.page, r.id || state.selected, { animate: play, hash: false });
}
async function refresh() {
  if (polling) return;
  polling = true;
  try {
    const data = await request("snapshot?view=overview"),
      first = !state.snapshot,
      previousRefresh = state.snapshot?.refreshedAt;
    state.snapshot = data;
    state.connected = true;
    if (first && !state.selected) {
      const r = route();
      const match =
        r.id &&
        data.devices.find((d) => d.id.toLowerCase() === r.id.toLowerCase());
      const chosen = match || sortedDevices()[0];
      selectContext(chosen?.id || "");
      filterSelection[state.filter] = state.selected;
    }
    renderList();
    renderConnection();
    renderSettings();
    renderRouter();
    if (first) applyRoute(false);
    else if (
      state.selected &&
      !sortedDevices().some((d) => d.id === state.selected)
    ) {
      selectContext("");
      if (
        state.page === "device" &&
        !pendingNavigation &&
        (!compact.matches || state.compactOpen)
      )
        navigate("device", "", { animate: false, hash: false });
    } else if (
      state.page === "device" &&
      state.selected &&
      !pendingNavigation &&
      (!compact.matches || state.compactOpen)
    ) {
      renderDevice(selectedDevice());
      await loadDetail(state.selected);
    }
    if (
      !reduced.matches &&
      data.refreshedAt &&
      data.refreshedAt !== previousRefresh &&
      $("connection-indicator").classList.contains("connected")
    )
      $("connection-indicator").animate(
        [
          { transform: "scale(1)" },
          { transform: "scale(1.12)", offset: 0.2 },
          { transform: "scale(1)" },
        ],
        {
          duration: Math.min(
            1800,
            (data.preferences?.refreshIntervalSeconds || 1) * 1000,
          ),
          easing: "ease-out",
        },
      );
  } catch (error) {
    state.connected = false;
    text(
      "connection-status",
      state.snapshot ? "连接中断，正在重试" : "尚未连接",
    );
    $("connection-indicator").classList.remove("connected");
  } finally {
    polling = false;
    clearTimeout(pollTimer);
    pollTimer = setTimeout(
      refresh,
      document.hidden
        ? 5000
        : Math.max(
            1000,
            (state.snapshot?.preferences?.refreshIntervalSeconds || 1) * 1000,
          ),
    );
  }
}
$("device-list").addEventListener("click", (event) => {
  const row = event.target.closest(".device-row");
  if (row) navigate("device", row.dataset.id);
});
$("device-list").addEventListener("keydown", (event) => {
  if (
    !["ArrowUp", "ArrowDown", "Home", "End", "Enter", " "].includes(event.key)
  )
    return;
  const devices = sortedDevices();
  if (!devices.length) return;
  event.preventDefault();
  const index = Math.max(
    0,
    devices.findIndex((d) => d.id === state.selected),
  );
  const target =
    event.key === "Home"
      ? 0
      : event.key === "End"
        ? devices.length - 1
        : event.key === "ArrowUp"
          ? Math.max(0, index - 1)
          : event.key === "ArrowDown"
            ? Math.min(devices.length - 1, index + 1)
            : index;
  navigate("device", devices[target].id);
  rows.get(devices[target].id)?.focus({ preventScroll: true });
  rows.get(devices[target].id)?.scrollIntoView({ block: "nearest" });
});
function initializeSegments(group, onSelect) {
  const buttons = [...group.querySelectorAll("button")];
  const choose = (index) => {
    if (
      index < 0 ||
      index >= buttons.length ||
      buttons[index].getAttribute("aria-pressed") === "true"
    )
      return;
    setSegment(group, index);
    onSelect(buttons[index], index);
  };
  group.addEventListener("click", (event) => {
    const button = event.target.closest("button");
    if (button) choose(buttons.indexOf(button));
  });
  group.addEventListener("pointerdown", (event) => {
    if (event.button !== 0) return;
    group.setPointerCapture(event.pointerId);
    const rect = group.getBoundingClientRect();
    choose(
      Math.max(
        0,
        Math.min(
          buttons.length - 1,
          Math.floor(
            (event.clientX - rect.left - 2) /
              ((rect.width - 4) / buttons.length),
          ),
        ),
      ),
    );
  });
  group.addEventListener("pointermove", (event) => {
    if (!group.hasPointerCapture(event.pointerId) || !event.buttons) return;
    const rect = group.getBoundingClientRect();
    choose(
      Math.max(
        0,
        Math.min(
          buttons.length - 1,
          Math.floor(
            (event.clientX - rect.left - 2) /
              ((rect.width - 4) / buttons.length),
          ),
        ),
      ),
    );
  });
  group.addEventListener("pointerup", (event) => {
    if (group.hasPointerCapture(event.pointerId))
      group.releasePointerCapture(event.pointerId);
  });
  group.addEventListener("keydown", (event) => {
    if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
    event.preventDefault();
    const current = buttons.findIndex(
      (b) => b.getAttribute("aria-pressed") === "true",
    );
    const next =
      event.key === "Home"
        ? 0
        : event.key === "End"
          ? buttons.length - 1
          : Math.max(
              0,
              Math.min(
                buttons.length - 1,
                current + (event.key === "ArrowRight" ? 1 : -1),
              ),
            );
    choose(next);
    buttons[next].focus();
  });
}
initializeSegments($("filters"), (button) => {
  const filter = button.dataset.filter;
  if (state.filter === filter) return;
  state.filter = filter;
  const visible = sortedDevices();
  const remembered = filterSelection[filter];
  const selected = visible.find((d) => d.id === remembered) || visible[0];
  if (state.page === "device" && !compact.matches)
    navigate("device", selected?.id || "");
  else {
    selectContext(selected?.id || "");
    if (selected) filterSelection[filter] = selected.id;
    renderList();
  }
  $("device-list").scrollTop = 0;
  if (!reduced.matches)
    animate(
      $("device-list"),
      [
        { opacity: 0.65, transform: "translateY(10px)" },
        { opacity: 1, transform: "translateY(0)" },
      ],
      { duration: 480, easing: "cubic-bezier(.22,1,.36,1)" },
    );
});
initializeSegments($("traffic-range"), (button) => {
  state.range = Number(button.dataset.range);
  const d = selectedDevice();
  if (d)
    chart.update(
      state.detail?.history || [],
      state.detail?.serverTime || state.snapshot?.refreshedAt || Date.now(),
      state.range,
      state.snapshot?.preferences?.refreshIntervalSeconds || 1,
    );
});
document
  .querySelectorAll("[data-page]")
  .forEach((button) =>
    button.addEventListener("click", () => navigate(button.dataset.page)),
  );
$("back-button").addEventListener("click", () => openList());
$("history-toggle").addEventListener("click", toggleHistory);
$("router-refresh").addEventListener("click", async () => {
  const button = $("router-refresh");
  button.disabled = true;
  try {
    await refresh();
    renderRouter();
  } finally {
    button.disabled = false;
  }
});
$("copy-address").addEventListener("click", () => copy($("lan-address").value));
$("theme-mode").addEventListener("change", (event) => {
  localTheme = event.target.value;
  try {
    localStorage.setItem("lan-desktop-web-theme", localTheme);
  } catch {}
  applyTheme();
  scheduleLayout();
});
themeTrigger.addEventListener("click", () =>
  themeMenu.hidden ? openThemeMenu() : closeThemeMenu(),
);
themeTrigger.addEventListener("keydown", (event) => {
  if (["ArrowDown", "ArrowUp", "Enter", " "].includes(event.key)) {
    event.preventDefault();
    openThemeMenu();
  }
});
themeMenu.addEventListener("click", (event) => {
  const option = event.target.closest("[data-mode]");
  if (!option) return;
  themeSelect.value = option.dataset.mode;
  themeSelect.dispatchEvent(new Event("change"));
  closeThemeMenu();
  themeTrigger.focus({ preventScroll: true });
});
themeMenu.addEventListener("keydown", (event) => {
  const options = [...themeMenu.querySelectorAll("[data-mode]")],
    index = options.indexOf(document.activeElement);
  if (event.key === "Escape") {
    event.preventDefault();
    closeThemeMenu();
    themeTrigger.focus({ preventScroll: true });
  } else if (["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) {
    event.preventDefault();
    options[
      event.key === "Home"
        ? 0
        : event.key === "End"
          ? options.length - 1
          : Math.max(
              0,
              Math.min(
                options.length - 1,
                index + (event.key === "ArrowDown" ? 1 : -1),
              ),
            )
    ].focus();
  }
});
document.addEventListener("pointerdown", (event) => {
  if (
    !themeMenu.hidden &&
    !themeMenu.contains(event.target) &&
    !themeTrigger.contains(event.target)
  )
    closeThemeMenu();
});
let themeMenuGeneration = 0;
function openThemeMenu() {
  ++themeMenuGeneration;
  cancelMotion(themeMenu);
  themeMenu.hidden = false;
  themeTrigger.setAttribute("aria-expanded", "true");
  const rect = themeTrigger.getBoundingClientRect();
  themeMenu.style.width = rect.width + 18 + "px";
  themeMenu.style.left = rect.left - 12 + "px";
  themeMenu.style.top =
    (rect.bottom + themeMenu.offsetHeight <= innerHeight
      ? rect.bottom
      : rect.top - themeMenu.offsetHeight) + "px";
  const selected = themeMenu.querySelector("[aria-selected=true]"),
    label = selected?.querySelector("span");
  if (label)
    themeMenu.style.left =
      rect.left -
      12 +
      themeTrigger.querySelector(".theme-value").getBoundingClientRect().left -
      label.getBoundingClientRect().left +
      "px";
  animate(
    themeMenu,
    [
      { opacity: 0, transform: "translateY(2px)" },
      { opacity: 1, transform: "translateY(0)" },
    ],
    { duration: 180, easing: "cubic-bezier(.23,1,.32,1)" },
  );
  selected?.focus({ preventScroll: true });
}
function closeThemeMenu() {
  if (themeMenu.hidden) return;
  const generation = ++themeMenuGeneration;
  themeTrigger.setAttribute("aria-expanded", "false");
  const cs = getComputedStyle(themeMenu);
  animate(
    themeMenu,
    [
      { opacity: cs.opacity, transform: cs.transform },
      { opacity: 0, transform: "translateY(2px)" },
    ],
    { duration: 180, easing: "cubic-bezier(.23,1,.32,1)" },
  ).then(() => {
    if (generation === themeMenuGeneration) themeMenu.hidden = true;
  });
}
systemDark.addEventListener("change", applyTheme);
document
  .querySelectorAll("[data-desktop-only]")
  .forEach((el) =>
    el.addEventListener("click", () => toast("该设置或操作请在电脑端完成")),
  );
window.addEventListener("hashchange", () => applyRoute());
window.addEventListener("popstate", () => applyRoute());
document.addEventListener("visibilitychange", () => {
  if (!document.hidden) {
    clearTimeout(pollTimer);
    refresh();
  }
});
function sidebarWidth() {
  return sidebarCollapsed
    ? 48
    : Math.min(
        sidebarExpandedWidth,
        Math.max(280, Math.min(520, innerWidth - 606)),
      ) - 8;
}
function updateSidebar(animateWidth = false) {
  clearTimeout(sidebarTimer);
  $("workspace").classList.toggle(
    "sidebar-moving",
    animateWidth && !reduced.matches,
  );
  document.body.classList.toggle(
    "sidebar-collapsed",
    sidebarCollapsed && !compact.matches,
  );
  $("workspace").style.setProperty("--sidebar-width", sidebarWidth() + "px");
  if (animateWidth)
    sidebarTimer = setTimeout(
      () => $("workspace").classList.remove("sidebar-moving"),
      280,
    );
}
$("sidebar-toggle").addEventListener("click", () => {
  sidebarCollapsed = true;
  updateSidebar(true);
});
$("sidebar-expand").addEventListener("click", () => {
  sidebarCollapsed = false;
  updateSidebar(true);
});
let dragStart;
$("sidebar-splitter").addEventListener("pointerdown", (event) => {
  if (compact.matches || sidebarCollapsed) return;
  dragStart = { x: event.clientX, width: sidebarExpandedWidth };
  $("sidebar-splitter").setPointerCapture(event.pointerId);
});
$("sidebar-splitter").addEventListener("pointermove", (event) => {
  if (!dragStart) return;
  sidebarExpandedWidth = Math.min(
    520,
    Math.max(280, dragStart.width + event.clientX - dragStart.x),
  );
  updateSidebar();
});
$("sidebar-splitter").addEventListener("pointerup", (event) => {
  dragStart = null;
  if ($("sidebar-splitter").hasPointerCapture(event.pointerId))
    $("sidebar-splitter").releasePointerCapture(event.pointerId);
});
$("sidebar-splitter").addEventListener("keydown", (event) => {
  if (!["ArrowLeft", "ArrowRight"].includes(event.key)) return;
  event.preventDefault();
  sidebarExpandedWidth = Math.min(
    520,
    Math.max(280, sidebarExpandedWidth + (event.key === "ArrowRight" ? 1 : -1)),
  );
  updateSidebar();
});
compact.addEventListener("change", () => {
  ++state.nav;
  pendingNavigation = false;
  for (const el of document.querySelectorAll(".page")) {
    cancelMotion(el);
    stopScrollMotion(el);
  }
  cancelMotion($("sidebar"));
  updateSidebar();
  state.compactOpen = false;
  document.body.classList.remove("compact-detail");
  if (!compact.matches && state.snapshot) {
    const r = route();
    navigate(r.page === "list" ? "device" : r.page, r.id || state.selected, {
      animate: false,
      hash: false,
    });
  }
  scheduleLayout();
});
window.addEventListener("resize", () => {
  updateSidebar();
  scheduleLayout();
});
function updateScrollMask(el) {
  precision?.update(el);
}
function layoutSessions() {
  const dpr = devicePixelRatio || 1;
  for (const row of $("session-list").children) {
    if (row.hidden) continue;
    const time = row.querySelector(".session-times"),
      badge = row.querySelector(".session-duration");
    const lines = [...time.querySelectorAll(".session-time")].map(
      (line) =>
        [...line.children].reduce(
          (sum, span) => sum + span.getBoundingClientRect().width,
          0,
        ) +
        Math.max(0, line.children.length - 1) * 10,
    );
    const required =
      24 +
      72 +
      Math.max(...lines) +
      12 +
      badge.getBoundingClientRect().width +
      2 / dpr;
    row.classList.toggle("stacked", required > row.clientWidth - 24);
  }
}
function layoutMetadata() {
  const host = $("network-metadata");
  if (!host.clientWidth) return;
  const canvas =
      layoutMetadata.canvas ||
      (layoutMetadata.canvas = document.createElement("canvas")),
    context = canvas.getContext("2d");
  const widths = [...host.children].map(
    (block) =>
      Math.max(
        ...[...block.children].map((text) => {
          const cs = getComputedStyle(text);
          context.font = cs.font;
          return context.measureText(text.textContent).width;
        }),
      ) +
      2 / (devicePixelRatio || 1),
  );
  let columns =
    widths.reduce((sum, n) => sum + n, 0) + 72 <= host.clientWidth ? 4 : 2;
  if (columns === 2 && Math.max(...widths) > (host.clientWidth - 24) / 2)
    columns = 1;
  host.classList.toggle("intrinsic", columns === 4);
  host.style.setProperty(
    "--metadata-columns",
    `repeat(${columns},minmax(0,1fr))`,
  );
}
function scheduleLayout() {
  if (layoutFrame) return;
  layoutFrame = requestAnimationFrame(() => {
    layoutFrame = 0;
    layoutMetadata();
    const metrics = $("traffic-metrics");
    if (metrics.clientWidth)
      metrics.style.gridTemplateColumns = `repeat(${metrics.clientWidth >= 562 ? 4 : metrics.clientWidth >= 274 ? 2 : 1},minmax(0,1fr))`;
    const header = $("traffic-heading");
    if (header.clientWidth) {
      const title = header.querySelector("h2"),
        cs = getComputedStyle(title);
      const canvas =
          layoutMetadata.canvas ||
          (layoutMetadata.canvas = document.createElement("canvas")),
        ctx = canvas.getContext("2d");
      ctx.font = cs.font;
      header.classList.toggle(
        "wrapped",
        ctx.measureText(title.textContent).width +
          19 +
          228 +
          10 +
          4 / (devicePixelRatio || 1) >
          header.clientWidth,
      );
    }
    const quiet = $("quiet-times");
    if (!quiet.hidden)
      quiet.classList.toggle("stacked", quiet.clientWidth < 430);
    layoutSessions();
    const list = $("session-list"),
      last = [...list.children].filter((row) => !row.hidden).at(-1);
    if (!motion.has($("history-reveal")))
      $("history-reveal").style.height =
        (last ? last.offsetTop + last.offsetHeight : 0) + "px";
    document
      .querySelectorAll(".scroll-pane,.device-list")
      .forEach(updateScrollMask);
    precision?.alignLabels();
  });
}
const observer = new ResizeObserver(scheduleLayout);
for (const el of [
  $("detail-surface"),
  $("sidebar"),
  $("network-metadata"),
  $("traffic-heading"),
  $("traffic-metrics"),
  $("history-reveal"),
])
  observer.observe(el);
document.fonts.ready.then(scheduleLayout);
const wheelStates = new WeakMap();
function stopScrollMotion(el) {
  const run = el && wheelStates.get(el);
  if (run) {
    cancelAnimationFrame(run.frame);
    wheelStates.delete(el);
  }
}
for (const el of document.querySelectorAll(".scroll-pane,.device-list")) {
  el.addEventListener(
    "scroll",
    () => {
      const run = wheelStates.get(el);
      if (
        run &&
        run.expected !== undefined &&
        Math.abs(el.scrollTop - run.expected) > 1
      )
        stopScrollMotion(el);
      updateScrollMask(el);
      if (el === $("device-page")) closeAllRangeTips(true);
      if (el === $("settings-page")) closeThemeMenu();
    },
    { passive: true },
  );
  el.addEventListener("pointerdown", () => stopScrollMotion(el), {
    capture: true,
  });
  el.addEventListener("keydown", () => stopScrollMotion(el), { capture: true });
  el.addEventListener(
    "wheel",
    (event) => {
      if (event.ctrlKey || !event.deltaY || reduced.matches) return;
      const max = el.scrollHeight - el.clientHeight;
      if (max <= 0) return;
      const old = wheelStates.get(el),
        step =
          (el.classList.contains("device-list") ? 170 : 180) *
          Math.sign(event.deltaY) *
          Math.min(1, Math.max(0.05, Math.abs(event.deltaY) / 100));
      const prior = old?.target ?? el.scrollTop;
      const remaining =
        Math.sign(prior - el.scrollTop) === Math.sign(step)
          ? prior - el.scrollTop
          : 0;
      const target = Math.min(
        max,
        Math.max(
          0,
          el.scrollTop + Math.max(-360, Math.min(360, remaining + step)),
        ),
      );
      if (target === el.scrollTop) return;
      event.preventDefault();
      if (old) cancelAnimationFrame(old.frame);
      const run = {
        start: el.scrollTop,
        target,
        at: performance.now(),
        frame: 0,
      };
      wheelStates.set(el, run);
      const frame = (now) => {
        if (wheelStates.get(el) !== run) return;
        const progress = Math.min(1, (now - run.at) / 400);
        el.scrollTop =
          run.start + (run.target - run.start) * (1 - (1 - progress) ** 2);
        run.expected = el.scrollTop;
        if (progress < 1) run.frame = requestAnimationFrame(frame);
        else wheelStates.delete(el);
      };
      run.frame = requestAnimationFrame(frame);
    },
    { passive: false },
  );
}
const tipStates = [];
for (const [cardId, tipId] of [
  ["download-total-card", "download-range"],
  ["upload-total-card", "upload-range"],
]) {
  const card = $(cardId),
    tip = $(tipId),
    item = { card, tip, timer: 0, generation: 0, scrollClosing: false };
  tipStates.push(item);
  const show = () => {
    clearTimeout(item.timer);
    if (item.scrollClosing && !tip.hidden) return;
    const generation = ++item.generation;
    if (!tip.hidden) {
      cancelMotion(tip);
      tip.style.opacity = "1";
      return;
    }
    item.timer = setTimeout(() => {
      if (
        generation !== item.generation ||
        !card.isConnected ||
        $("device-page").hidden ||
        !card.getClientRects().length
      )
        return;
      tip.hidden = false;
      item.scrollClosing = false;
      animate(tip, [{ opacity: 0 }, { opacity: 1 }], {
        duration: 150,
        easing: "linear",
      });
    }, 250);
  };
  card.addEventListener("pointerenter", show);
  card.addEventListener("focus", show);
  card.addEventListener("pointerleave", () => closeRangeTip(item));
  card.addEventListener("blur", () => closeRangeTip(item));
  card.addEventListener("pointermove", () => {
    if (item.scrollClosing && tip.hidden) {
      item.scrollClosing = false;
      show();
    }
  });
}
function closeRangeTip(item, scroll = false) {
  clearTimeout(item.timer);
  if (item.tip.hidden) return;
  if (item.scrollClosing && scroll) return;
  const generation = ++item.generation;
  item.scrollClosing = scroll;
  const opacity = getComputedStyle(item.tip).opacity;
  animate(item.tip, [{ opacity }, { opacity: 0 }], {
    duration: 120,
    easing: "cubic-bezier(0,.667,.333,1)",
  }).then(() => {
    if (generation === item.generation) item.tip.hidden = true;
  });
}
function closeAllRangeTips(scroll = false) {
  for (const item of tipStates) closeRangeTip(item, scroll);
}
precision = new DesktopPrecision(stopScrollMotion);
updateSidebar();
applyTheme();
refresh();
