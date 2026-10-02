import { deviceIconNames, deviceIconPaths } from "./device-icons.js";
export const $ = (id) => document.getElementById(id);
export const esc = (value) =>
  String(value ?? "").replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
const paths = {
  phone:
    '<rect x="6" y="2" width="12" height="20" rx="3"/><path d="M10 5h4m-3 14h2"/>',
  tablet:
    '<rect x="3" y="2" width="18" height="20" rx="3"/><path d="M11 19h2"/>',
  laptop:
    '<rect x="4" y="3" width="16" height="13" rx="2"/><path d="M2 20h20l-2-4H4z"/>',
  desktop:
    '<rect x="2" y="3" width="20" height="14" rx="2"/><path d="M12 17v4m-4 0h8"/>',
  tv: '<rect x="2" y="4" width="20" height="13" rx="2"/><path d="m7 17-2 4m12-4 2 4"/>',
  game: '<path d="M7 7h10c4 0 5 12 2 12-2 0-3-4-4-4H9c-1 0-2 4-4 4C2 19 3 7 7 7Z"/><path d="M6 11h4m-2-2v4m7-2h.01m3 2h.01"/>',
  router:
    '<rect x="3" y="11" width="18" height="9" rx="3"/><path d="M6 11V4m12 7V4m-11 12h.01M11 16h.01m4 0h2"/>',
  speaker:
    '<rect x="6" y="2" width="12" height="20" rx="4"/><circle cx="12" cy="14" r="3"/><path d="M11 6h2"/>',
  lamp: '<path d="M8 16c0-3-3-3-3-7a7 7 0 0 1 14 0c0 4-3 4-3 7zm1 4h6m-5 2h4"/>',
  plug: '<rect x="4" y="2" width="16" height="20" rx="4"/><path d="M9 7v3m6-3v3m-3 3v3"/>',
  air: '<rect x="2" y="4" width="20" height="10" rx="3"/><path d="M5 10h14M7 17v3m5-3v5m5-5v3"/>',
  sensor:
    '<rect x="4" y="3" width="16" height="18" rx="4"/><path d="M8 8h8m-8 5h3m-3 4h8"/>',
  curtain:
    '<rect x="3" y="3" width="18" height="18" rx="2"/><path d="M8 3v18m8-18v18M12 3v18"/>',
  device:
    '<rect x="4" y="4" width="16" height="16" rx="4"/><path d="M9 10h6m-6 4h6"/>',
  devices:
    '<rect x="2" y="3" width="14" height="12" rx="2"/><path d="M7 15v4H3"/><rect x="16" y="10" width="6" height="11" rx="1.5"/>',
  network:
    '<circle cx="12" cy="12" r="9"/><ellipse cx="12" cy="12" rx="4" ry="9"/><path d="M3 12h18M5 6h14M5 18h14"/>',
  search: '<circle cx="10.5" cy="10.5" r="6.5"/><path d="m16 16 5 5"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v6m0-10h.01"/>',
  back: '<path d="m15 5-7 7 7 7"/>',
  chevron: '<path d="m9 5 7 7-7 7"/>',
  down: '<path d="M12 3v17m-6-6 6 6 6-6"/>',
  up: '<path d="M12 21V4m-6 6 6-6 6 6"/>',
  close: '<path d="m6 6 12 12M18 6 6 18"/>',
  copy: '<rect x="8" y="8" width="12" height="13" rx="2"/><path d="M15 8V3H3v13h5"/>',
  refresh: '<path d="M20 9a8 8 0 1 0 0 6M20 3v6h-6"/>',
};
export function icon(name) {
  return `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.65" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${name === "router" ? deviceIconPaths.router : paths[name] || paths.device}</svg>`;
}
export function deviceIcon(type) {
  return `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${deviceIconPaths[deviceIconNames[type] || "box"]}</svg>`;
}
export function hydrateIcons(root = document) {
  root
    .querySelectorAll("[data-icon]")
    .forEach((el) => (el.innerHTML = icon(el.dataset.icon)));
}
export function text(id, value) {
  const el = $(id),
    s = String(value ?? "—");
  if (el && el.textContent !== s) el.textContent = s;
}
export function bytes(n, speed = false) {
  if (n == null || !Number.isFinite(Number(n))) return { value: "—", unit: "" };
  let v = Math.max(0, Number(n)),
    i = 0;
  const units = ["B", "KB", "MB", "GB", "TB"];
  if (speed) {
    v /= 1024;
    i = 1;
  }
  while (v >= 1024 && i < 4) {
    v /= 1024;
    i++;
  }
  return {
    value:
      v === 0 ? "0" : v.toLocaleString("zh-CN", { maximumFractionDigits: 2 }),
    unit: units[i] + (speed ? "/s" : ""),
  };
}
export const rate = (n) => {
  const f = bytes(n, true);
  return `${f.value} ${f.unit}`;
};
export const total = (n) => {
  const f = bytes(n);
  return `${f.value} ${f.unit}`;
};
export function date(value, options = {}) {
  if (!value || new Date(value).getFullYear() < 2000) return "暂无记录";
  return new Date(value).toLocaleString("zh-CN", { hour12: false, ...options });
}
export const clock = (value) =>
  date(value, { hour: "2-digit", minute: "2-digit", second: "2-digit" });
export function duration(start, end = Date.now()) {
  if (!start) return "暂无时长";
  const m = Math.max(0, Math.floor((new Date(end) - new Date(start)) / 60000));
  if (m < 1) return "不足 1 分钟";
  if (m < 60) return `${m} 分钟`;
  if (m < 1440) return `${Math.floor(m / 60)} 小时 ${m % 60} 分钟`;
  return `${Math.floor(m / 1440)} 天 ${Math.floor((m % 1440) / 60)} 小时`;
}
export function fields(root, rows) {
  const shape = JSON.stringify(rows.map(([label, , copy]) => [label, !!copy]));
  if (root.dataset.shape !== shape) {
    root.dataset.shape = shape;
    root.innerHTML = rows
      .map(
        ([label, , copy]) =>
          `<div class="field-row"><dt>${esc(label)}</dt><dd><span></span>${copy ? `<button class="icon-button copy-button" aria-label="复制${esc(label)}">${icon("copy")}</button>` : ""}</dd></div>`,
      )
      .join("");
  }
  rows.forEach(([, value], index) => {
    const row = root.children[index],
      span = row.querySelector("dd>span"),
      s = String(value || "—");
    if (span.textContent !== s) span.textContent = s;
    const button = row.querySelector("button");
    if (button) {
      button.hidden = !value;
      button.dataset.copy = value || "";
    }
  });
}
export function sessionsMarkup(items) {
  return items.length
    ? items
        .map(
          (s) =>
            `<div class="session-row"><span class="session-track ${!s.offlineAt ? "live" : ""}"></span><div class="session-times"><span><b>上线</b><time>${esc(date(s.onlineAt))}</time></span><span><b>${s.offlineAt ? "离线" : "仍在线"}</b><time>${s.offlineAt ? esc(date(s.offlineAt)) : "本次连接尚未结束"}</time></span></div><span class="session-duration">${duration(s.onlineAt, s.offlineAt || Date.now())}</span></div>`,
        )
        .join("")
    : '<div class="inline-empty">尚无上下线记录</div>';
}
let toastTimer;
export function toast(message) {
  text("toast", message);
  $("toast").hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => ($("toast").hidden = true), 2300);
}
export async function copy(value) {
  try {
    if (navigator.clipboard && isSecureContext)
      await navigator.clipboard.writeText(value);
    else {
      const input = document.createElement("textarea");
      input.value = value;
      input.className = "clipboard-helper";
      document.body.append(input);
      input.select();
      const copied = document.execCommand("copy");
      input.remove();
      if (!copied) throw new Error();
    }
    toast("已复制");
  } catch {
    toast("请长按文字复制");
  }
}
