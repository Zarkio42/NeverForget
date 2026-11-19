'use strict';

/* ================= Utilities ================= */
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];

// Icon paths are static strings authored here; user content never goes through innerHTML.
const ICONS = {
  plus: '<path d="M12 5v14M5 12h14"/>',
  search: '<circle cx="11" cy="11" r="6.5"/><path d="m20 20-4.2-4.2"/>',
  board: '<rect x="3.5" y="4.5" width="5" height="15" rx="1.5"/><rect x="10" y="4.5" width="5" height="9" rx="1.5"/><rect x="16.5" y="4.5" width="4" height="12" rx="1.5"/>',
  note: '<path d="M5 4.5h14a.5.5 0 0 1 .5.5v9.5l-5 5H5a.5.5 0 0 1-.5-.5V5a.5.5 0 0 1 .5-.5Z"/><path d="M14.5 19.5V15a.5.5 0 0 1 .5-.5h4.5"/>',
  calendar: '<rect x="3.5" y="5" width="17" height="15.5" rx="2"/><path d="M3.5 10h17M8 3v4M16 3v4"/>',
  check: '<path d="m5 12.5 4.5 4.5L19 7.5"/>',
  undo: '<path d="M9 14 4 9l5-5"/><path d="M4 9h10.5a5.5 5.5 0 0 1 0 11H11"/>',
  trash: '<path d="M4 7h16M10 11v6M14 11v6M6 7l1 12.5a1.5 1.5 0 0 0 1.5 1.5h7a1.5 1.5 0 0 0 1.5-1.5L18 7M9 7V4.5h6V7"/>',
  x: '<path d="M6 6l12 12M18 6 6 18"/>',
  more: '<circle cx="5.5" cy="12" r="1.2" fill="currentColor"/><circle cx="12" cy="12" r="1.2" fill="currentColor"/><circle cx="18.5" cy="12" r="1.2" fill="currentColor"/>',
  flag: '<path d="M5.5 21V4M5.5 4.5h11l-2.2 4 2.2 4h-11"/>',
  archive: '<rect x="3" y="4" width="18" height="4.5" rx="1"/><path d="M5 8.5V19a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V8.5M10 12.5h4"/>',
  clock: '<circle cx="12" cy="12" r="8.5"/><path d="M12 7.5V12l3 2"/>',
  bolt: '<path d="M13 3 5 13.5h6L10 21l8-10.5h-6L13 3Z"/>',
  play: '<path d="M8 5.5v13l10-6.5-10-6.5Z"/>',
  arrow: '<path d="M5 12h14M13 6l6 6-6 6"/>',
};
const icon = (n) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${ICONS[n] || ''}</svg>`;
function hydrateIcons(root = document) {
  $$('i[data-icon]', root).forEach((i) => { if (!i.firstChild) i.innerHTML = icon(i.dataset.icon); });
}

const COLORS = ['yellow', 'green', 'blue', 'pink', 'purple', 'gray'];
const STATUSES = ['todo', 'doing', 'done'];
const colName = (s) => t(`col.${s}`);
const colorName = (c) => t(`color.${c}`);

const pad = (n) => String(n).padStart(2, '0');
const ymd = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const today = () => ymd(new Date());
const addDays = (n) => { const d = new Date(); d.setDate(d.getDate() + n); return ymd(d); };
const parseYmd = (s) => { const [y, m, d] = s.split('-').map(Number); return new Date(y, m - 1, d); };
const localStamp = (d = new Date()) => `${ymd(d)}T${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
const norm = (s) => (s || '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

function shortDate(d) {
  const y = d.getFullYear() !== new Date().getFullYear() ? d.getFullYear() : null;
  return t('date.short', d.getDate(), t('months')[d.getMonth()], y);
}

function dueInfo(due, status) {
  if (!due) return null;
  const diff = Math.round((parseYmd(due) - parseYmd(today())) / 864e5);
  let text = shortDate(parseYmd(due));
  if (diff === 0) text = t('date.today');
  else if (diff === 1) text = t('date.tomorrow');
  else if (diff === -1) text = t('date.yesterday');
  let cls = '';
  if (status !== 'done') { if (diff < 0) cls = 'late'; else if (diff === 0) cls = 'today'; }
  return { text, cls, late: diff < 0 };
}

function stamp(s) {
  if (!s) return '';
  const d = new Date(s.replace(' ', 'T'));
  if (isNaN(d)) return '';
  const time = `${pad(d.getHours())}:${pad(d.getMinutes())}`;
  return ymd(d) === today() ? t('stamp.today', time) : t('stamp.other', shortDate(d), time);
}

/* ================= Bridge to the native host ================= */
// Inside the app, messages go to the C# host (WebView2 postMessage).
// In a regular browser there is no host, so a localStorage mock is used (preview and screenshots).
// tools/screenshots sets __NF_FORCE_MOCK to render the demo data inside WebView2.
const native = !window.__NF_FORCE_MOCK && window.chrome && window.chrome.webview;
const pending = new Map();
let seq = 0;

function call(cmd, args = {}) {
  if (!native) return Promise.resolve().then(() => mock(cmd, args));
  return new Promise((resolve, reject) => {
    const id = ++seq;
    pending.set(id, { resolve, reject });
    native.postMessage(JSON.stringify({ id, cmd, args }));
  });
}

if (native) {
  native.addEventListener('message', (e) => {
    let m;
    try { m = JSON.parse(e.data); } catch { return; }
    if (m.event) return onNativeEvent(m.event, m.data);
    const p = pending.get(m.id);
    if (!p) return;
    pending.delete(m.id);
    m.ok ? p.resolve(m.data) : p.reject(new Error(m.error || 'Error'));
  });
}

/* ---------- Browser mock (mirrors src/Store.cs; keep both in sync) ---------- */
const params = new URLSearchParams(location.search);
const DEMO = !native && params.has('demo');
const MOCK_KEY = DEMO ? 'neverforget-demo' : 'neverforget-mock';

function mockLoad() {
  try { return JSON.parse(localStorage.getItem(MOCK_KEY)) || null; } catch { return null; }
}

function mock(cmd, a) {
  const db = mockLoad() || { items: [], seq: 0, rules: [], ruleSeq: 0, settings: {} };
  const now = localStamp();
  const get = (id) => db.items.find((i) => i.id === Number(id));
  let r = true;
  switch (cmd) {
    case 'ready': r = { autostart: !!db.settings.autostart, lang: db.settings.lang || null, autoChanged: 0 }; break;
    case 'list': r = db.items.filter((i) => !i.archived); break;
    case 'create': {
      const st = a.status || 'todo';
      const same = db.items.filter((i) => i.status === st && !i.archived);
      const minS = same.length ? Math.min(...same.map((i) => i.sort)) : 2000;
      const z = db.items.reduce((m, i) => Math.max(m, i.z), 0) + 1;
      r = { id: ++db.seq, title: a.title || '', body: a.body || '', color: a.color || 'yellow', status: st, priority: a.priority ? 1 : 0,
        due: a.due || null, sort: minS - 1000, x: a.x ?? 40, y: a.y ?? 40, z, archived: 0,
        created_at: now, updated_at: now, status_at: now, done_at: st === 'done' ? now : null, archived_at: null };
      db.items.push(r); break;
    }
    case 'update': {
      r = get(a.id);
      for (const k of Object.keys(a)) if (k !== 'id') r[k] = a[k];
      if ('status' in a) r.done_at = a.status === 'done' ? (r.done_at || now) : null;
      r.updated_at = now; break;
    }
    case 'move': {
      r = get(a.id);
      const sb = a.beforeId != null ? get(a.beforeId).sort : null;
      const sa = a.afterId != null ? get(a.afterId).sort : null;
      r.sort = sb != null && sa != null ? (sb + sa) / 2 : sb != null ? sb + 1000 : sa != null ? sa - 1000 : 1000;
      if (r.status !== a.status) r.status_at = now;
      r.status = a.status;
      r.done_at = a.status === 'done' ? (r.done_at || now) : null;
      r.updated_at = now; break;
    }
    case 'delete': db.items = db.items.filter((i) => i.id !== Number(a.id)); break;
    case 'archiveDone': db.items.forEach((i) => { if (i.status === 'done' && !i.archived) { i.archived = 1; i.archived_at = now; } }); break;
    case 'listArchived': r = db.items.filter((i) => i.archived).sort((x, y) => (y.archived_at || '').localeCompare(x.archived_at || '')); break;
    case 'unarchive': r = get(a.id); r.archived = 0; r.archived_at = null; r.status_at = now; r.updated_at = now; break;
    case 'restore': db.items = db.items.filter((i) => i.id !== a.item.id); r = { ...a.item, archived: 0 }; db.items.push(r); break;
    case 'listRules': r = db.rules.map((x) => ({ ...x, pending: mockApply(db, x, true) })); break;
    case 'saveRule': {
      const v = a.action === 'move' || a.action === 'color' ? a.value : null;
      r = { id: ++db.ruleSeq, cond: a.cond, status: a.status || 'any', days: Number(a.days) || 0, action: a.action, value: v,
        enabled: 1, created_at: now, last_run: null, last_count: 0 };
      db.rules.push(r); r = { ...r, pending: mockApply(db, r, true) }; break;
    }
    case 'deleteRule': db.rules = db.rules.filter((x) => x.id !== Number(a.id)); break;
    case 'toggleRule': db.rules.forEach((x) => { if (x.id === Number(a.id)) x.enabled = a.on ? 1 : 0; }); break;
    case 'runRule': {
      const rule = db.rules.find((x) => x.id === Number(a.id));
      r = mockApply(db, rule, false);
      if (r) { rule.last_run = now; rule.last_count = r; }
      break;
    }
    case 'previewRule': r = mockApply(db, a, true); break;
    case 'getSetting': r = db.settings[a.key] ?? null; break;
    case 'setSetting': db.settings[a.key] = a.value; break;
    case 'setAutostart': db.settings.autostart = !!a.on; r = !!a.on; break;
  }
  localStorage.setItem(MOCK_KEY, JSON.stringify(db));
  return r && typeof r === 'object' ? JSON.parse(JSON.stringify(r)) : r;
}

function mockApply(db, r, dry) {
  const days = Number(r.days) || 0;
  const cut = localStamp(new Date(Date.now() - days * 864e5));
  const shift = (n) => { const d = parseYmd(today()); d.setDate(d.getDate() + n); return ymd(d); };
  const hit = db.items.filter((i) => {
    if (i.archived) return false;
    if (r.cond === 'inStatus' && !((i.status_at || i.updated_at) <= cut)) return false;
    if (r.cond === 'idle' && !(i.updated_at <= cut)) return false;
    if (r.cond === 'overdue' && !(i.due && i.due < shift(-days) && i.status !== 'done')) return false;
    if (r.cond === 'dueSoon' && !(i.due && i.due <= shift(days) && i.status !== 'done')) return false;
    if (r.status && r.status !== 'any' && i.status !== r.status) return false;
    if (r.action === 'move' && i.status === r.value) return false;
    if (r.action === 'priority' && Number(i.priority) > 0) return false;
    if (r.action === 'color' && i.color === r.value) return false;
    return true;
  });
  if (!dry) {
    const now = localStamp();
    hit.forEach((i) => {
      if (r.action === 'move') { i.status = r.value; i.status_at = now; i.sort = -Date.now(); }
      if (r.action === 'archive') { i.archived = 1; i.archived_at = now; }
      if (r.action === 'priority') i.priority = 1;
      if (r.action === 'color') i.color = r.value;
      i.updated_at = now;
    });
  }
  return hit.length;
}

/* ---------- Demo data (browser only, ?demo) used for the README screenshots ---------- */
const DEMO_NOTES = {
  en: [
    ['Draft the Q3 roadmap', 'Collect input from support and sales before Friday.', 'yellow', 'todo', 1, 0],
    ['Reply to the supplier about delivery dates', '', 'blue', 'todo', 0, -2],
    ['Book a dentist appointment', '', 'pink', 'todo', 0, null],
    ['Ideas for the team offsite', 'Hiking? Cooking class? Ask in the next stand-up.', 'gray', 'todo', 0, null],
    ['Prepare the client demo', 'Use the staging data set. Rehearse the checkout flow twice.', 'green', 'doing', 0, 1],
    ['Refactor the export job', 'Split it into smaller steps and add retries.', 'purple', 'doing', 0, 4],
    ['Send the weekly report', '', 'yellow', 'done', 0, null],
    ['Renew the domain', '', 'blue', 'done', 0, null],
  ],
  pt: [
    ['Rascunho do roadmap do 3º trimestre', 'Coletar ideias do suporte e de vendas antes de sexta.', 'yellow', 'todo', 1, 0],
    ['Responder fornecedor sobre prazos de entrega', '', 'blue', 'todo', 0, -2],
    ['Marcar dentista', '', 'pink', 'todo', 0, null],
    ['Ideias para o encontro do time', 'Trilha? Aula de culinária? Perguntar na próxima daily.', 'gray', 'todo', 0, null],
    ['Preparar a demonstração para o cliente', 'Usar a base de homologação. Ensaiar o fluxo de compra duas vezes.', 'green', 'doing', 0, 1],
    ['Refatorar a rotina de exportação', 'Dividir em etapas menores e adicionar novas tentativas.', 'purple', 'doing', 0, 4],
    ['Enviar o relatório semanal', '', 'yellow', 'done', 0, null],
    ['Renovar o domínio', '', 'blue', 'done', 0, null],
  ],
};

function seedDemo(lang) {
  const spots = [[40, 36], [300, 70], [560, 30], [820, 84], [80, 300], [350, 330], [620, 290], [880, 340]];
  const old = localStamp(new Date(Date.now() - 8 * 864e5));
  const now = localStamp();
  const db = { items: [], seq: 0, rules: [], ruleSeq: 0, settings: { lang, view: params.get('view') || 'board' } };
  DEMO_NOTES[lang].forEach(([title, body, color, status, priority, dueIn], i) => {
    db.items.push({ id: ++db.seq, title, body, color, status, priority, due: dueIn == null ? null : addDays(dueIn),
      sort: i * 1000, x: spots[i][0], y: spots[i][1], z: i + 1, archived: 0,
      created_at: old, updated_at: i === 6 ? old : now, status_at: i === 6 ? old : now,
      done_at: status === 'done' ? old : null, archived_at: null });
  });
  db.rules.push({ id: ++db.ruleSeq, cond: 'inStatus', status: 'done', days: 5, action: 'archive', value: null, enabled: 1, created_at: old, last_run: null, last_count: 0 });
  db.rules.push({ id: ++db.ruleSeq, cond: 'overdue', status: 'any', days: 0, action: 'priority', value: null, enabled: 1, created_at: old, last_run: now, last_count: 2 });
  localStorage.setItem(MOCK_KEY, JSON.stringify(db));
}

/* ================= State ================= */
const state = {
  items: [],
  view: 'board',
  q: '',
  todayOnly: false,
  editing: null,
  panel: null,
  autostart: false,
  day: today(),
};
const find = (id) => state.items.find((i) => i.id === Number(id));
function upsert(item) {
  const i = state.items.findIndex((x) => x.id === item.id);
  if (i >= 0) state.items[i] = item; else state.items.push(item);
  return item;
}

function matches(it) {
  if (state.todayOnly) {
    const td = today();
    const open = it.status !== 'done' && it.due && it.due <= td;
    const doneToday = it.status === 'done' && it.done_at && it.done_at.slice(0, 10) === td;
    if (!open && !doneToday) return false;
  }
  if (state.q && !norm(`${it.title} ${it.body}`).includes(norm(state.q))) return false;
  return true;
}

function failed(err) {
  console.error(err);
  toast(t('toast.saveFail', err.message));
}

/* ================= Cards ================= */
function cardEl(it, mode) {
  const el = document.createElement('article');
  el.className = `card c-${COLORS.includes(it.color) ? it.color : 'yellow'}${it.status === 'done' ? ' done' : ''}${state.editing === it.id ? ' selected' : ''}`;
  el.dataset.id = it.id;
  el.tabIndex = 0;
  const title = (it.title || '').trim();
  const body = (it.body || '').trim();
  el.setAttribute('aria-label', title || body.slice(0, 60) || t('card.empty'));

  if (title || !body) {
    const tl = document.createElement('div');
    tl.className = 'title' + (title ? '' : ' untitled');
    tl.textContent = title || t('card.empty');
    el.append(tl);
  }
  if (body) {
    const b = document.createElement('div');
    b.className = 'body' + (title ? '' : ' no-title');
    b.textContent = body;
    el.append(b);
  }

  const meta = document.createElement('div');
  meta.className = 'meta';
  const due = dueInfo(it.due, it.status);
  if (due) {
    const late = due.late && it.status !== 'done';
    const tag = document.createElement('span');
    tag.className = `tag ${due.cls}`;
    tag.innerHTML = icon(late ? 'clock' : 'calendar');
    tag.append(late ? t('tag.late', due.text) : due.text);
    tag.title = t('tag.dueTitle', parseYmd(it.due).toLocaleDateString(document.documentElement.lang));
    meta.append(tag);
  }
  if (Number(it.priority) > 0) {
    const tag = document.createElement('span');
    tag.className = 'tag high';
    tag.innerHTML = icon('flag');
    tag.append(t('tag.high'));
    meta.append(tag);
  }
  el.append(meta);

  const actions = document.createElement('div');
  actions.className = 'actions';
  const mk = (act, ic, label) => {
    const b = document.createElement('button');
    b.dataset.act = act;
    b.title = label;
    b.setAttribute('aria-label', label);
    b.innerHTML = icon(ic);
    return b;
  };
  actions.append(
    it.status !== 'done' ? mk('done', 'check', t('card.done')) : mk('reopen', 'undo', t('card.reopen')),
    mk('delete', 'trash', t('card.delete')));
  el.append(actions);

  if (mode === 'wall') {
    el.style.left = `${Number(it.x) || 0}px`;
    el.style.top = `${Number(it.y) || 0}px`;
    el.style.zIndex = Number(it.z) || 0;
  }
  return el;
}

/* ================= Rendering ================= */
function render() {
  if (state.view === 'board') renderBoard(); else renderWall();
  renderTodayCount();
}

function renderBoard() {
  for (const st of STATUSES) {
    const list = $(`.cards[data-status="${st}"]`);
    const all = state.items.filter((i) => i.status === st).sort((a, b) => a.sort - b.sort || a.id - b.id);
    const vis = all.filter(matches);
    list.replaceChildren(...vis.map((i) => cardEl(i, 'board')));
    if (!vis.length) {
      const e = document.createElement('div');
      e.className = 'empty';
      e.textContent = all.length ? t('empty.filter') : t(`empty.${st}`);
      list.append(e);
    }
    $(`.col[data-status="${st}"] .count`).textContent = vis.length;
  }
}

function renderWall() {
  const canvas = $('#wallCanvas');
  const vis = state.items.filter(matches).sort((a, b) => a.z - b.z);
  canvas.replaceChildren(...vis.map((i) => cardEl(i, 'wall')));
  sizeWall();
  $('#wallHint').hidden = state.items.length > 2;
}

function sizeWall() {
  const canvas = $('#wallCanvas');
  let w = 0, h = 0;
  for (const it of state.items) { w = Math.max(w, it.x + 300); h = Math.max(h, it.y + 260); }
  canvas.style.width = `${w}px`;
  canvas.style.height = `${h}px`;
}

function renderTodayCount() {
  const td = today();
  const n = state.items.filter((i) => i.status !== 'done' && i.due && i.due <= td).length;
  $('#todayCount').textContent = n ? String(n) : '';
  $('#todayChip').title = n ? t('today.titleN', n) : t('today.title');
}

function setView(v, persist = true) {
  state.view = v;
  $('#board').hidden = v !== 'board';
  $('#wall').hidden = v !== 'wall';
  $$('.topbar .seg button').forEach((b) => b.setAttribute('aria-selected', String(b.dataset.view === v)));
  render();
  if (persist) call('setSetting', { key: 'view', value: v }).catch(() => {});
}

/* Re-renders every translated piece after a language change. */
function setLanguage(lang, persist = true) {
  LANG = lang === 'pt' ? 'pt' : 'en';
  applyI18n();
  $$('.add-in-col').forEach((b) => {
    const label = t('col.add', colName(b.closest('.col').dataset.status));
    b.title = label;
    b.setAttribute('aria-label', label);
  });
  $$('#moreMenu [data-lang]').forEach((b) => b.setAttribute('aria-checked', String(b.dataset.lang === LANG)));
  buildSwatches();
  buildActionOptions();
  syncForm();
  render();
  if (state.editing) fillEditor(find(state.editing));
  if (state.panel === 'auto') renderRules();
  if (state.panel === 'arch') loadArchived();
  if (persist) call('setSetting', { key: 'lang', value: LANG }).catch(() => {});
}

/* ================= Actions ================= */
function nextWallSpot() {
  const wall = $('#wall');
  const n = state.items.length;
  if (state.view === 'wall' && wall.clientWidth) {
    const jitter = (n % 5) * 24;
    return {
      x: Math.max(16, Math.round(wall.scrollLeft + wall.clientWidth / 2 - 110 + jitter - 48)),
      y: Math.max(16, Math.round(wall.scrollTop + 60 + jitter)),
    };
  }
  return { x: 32 + (n % 5) * 244, y: 32 + (Math.floor(n / 5) % 8) * 196 };
}

async function newNote(opts = {}) {
  const spot = opts.x != null ? { x: opts.x, y: opts.y } : nextWallSpot();
  try {
    const it = upsert(await call('create', {
      title: opts.title || '',
      status: opts.status || 'todo',
      color: opts.color || 'yellow',
      due: opts.due || (state.todayOnly ? today() : null),
      x: spot.x, y: spot.y,
    }));
    render();
    flashCard(it.id);
    if (opts.open !== false) openEditor(it.id, true);
    return it;
  } catch (err) { failed(err); }
}

function flashCard(id) {
  const el = document.querySelector(`.card[data-id="${Number(id)}"]:not(.ghost)`);
  if (el) { el.classList.add('enter'); el.scrollIntoView({ block: 'nearest', inline: 'nearest' }); }
}

async function patch(id, fields) {
  try { return upsert(await call('update', { id, ...fields })); }
  catch (err) { failed(err); }
}

async function moveTo(id, status, beforeId = null, afterId = null) {
  try {
    const it = upsert(await call('move', { id, status, beforeId, afterId }));
    render();
    if (state.editing === id) fillEditor(it);
    return it;
  } catch (err) { failed(err); }
}

function firstIdIn(status, exceptId) {
  const first = state.items.filter((i) => i.status === status && i.id !== exceptId).sort((a, b) => a.sort - b.sort)[0];
  return first ? first.id : null;
}

const setStatus = (id, status) => moveTo(id, status, null, firstIdIn(status, id));

async function deleteItem(id) {
  const it = find(id);
  if (!it) return;
  if (state.editing === id) closeEditor(true);
  try {
    await call('delete', { id });
    state.items = state.items.filter((i) => i.id !== id);
    render();
    toast(t('toast.deleted'), t('undo'), async () => {
      upsert(await call('restore', { item: it }));
      render();
      flashCard(it.id);
    });
  } catch (err) { failed(err); }
}

/* ================= Drag and drop ================= */
let drag = null;

document.addEventListener('pointerdown', (e) => {
  if (e.button !== 0) return;
  const card = e.target.closest('.card');
  if (!card || card.classList.contains('ghost') || e.target.closest('button')) return;
  const mode = card.closest('.wall') ? 'wall' : 'board';
  const it = find(card.dataset.id);
  if (!it) return;
  drag = { card, it, mode, sx: e.clientX, sy: e.clientY, started: false };
  if (mode === 'wall') { drag.ox = it.x; drag.oy = it.y; }
});

document.addEventListener('pointermove', (e) => {
  if (!drag) return;
  const dx = e.clientX - drag.sx, dy = e.clientY - drag.sy;
  if (!drag.started) {
    if (Math.hypot(dx, dy) < 5) return;
    startDrag();
  }
  e.preventDefault();
  if (drag.mode === 'board') moveBoardDrag(e); else moveWallDrag(dx, dy);
});

document.addEventListener('pointerup', (e) => {
  if (!drag) return;
  const d = drag;
  drag = null;
  if (!d.started) {
    if (!e.target.closest('button')) openEditor(d.it.id);
    return;
  }
  document.body.style.cursor = '';
  if (d.mode === 'board') endBoardDrag(d); else endWallDrag(d);
});

document.addEventListener('pointercancel', () => {
  if (drag && drag.started) { drag.ghost?.remove(); render(); }
  drag = null;
});

function startDrag() {
  drag.started = true;
  document.body.style.cursor = 'grabbing';
  getSelection()?.removeAllRanges();
  if (drag.mode === 'board') {
    const r = drag.card.getBoundingClientRect();
    const ghost = drag.card.cloneNode(true);
    ghost.classList.add('ghost');
    ghost.classList.remove('selected', 'enter');
    ghost.style.width = `${r.width}px`;
    ghost.style.left = `${r.left}px`;
    ghost.style.top = `${r.top}px`;
    document.body.append(ghost);
    drag.ghost = ghost;
    drag.offX = drag.sx - r.left;
    drag.offY = drag.sy - r.top;
    drag.card.classList.add('placeholder');
  } else {
    const z = state.items.reduce((m, i) => Math.max(m, i.z), 0) + 1;
    drag.it.z = z;
    drag.card.style.zIndex = z;
    drag.card.classList.add('dragging');
  }
}

function moveBoardDrag(e) {
  drag.ghost.style.left = `${e.clientX - drag.offX}px`;
  drag.ghost.style.top = `${e.clientY - drag.offY}px`;
  const under = document.elementFromPoint(e.clientX, e.clientY);
  const col = under && under.closest('.col');
  $$('.col.drop-target').forEach((c) => { if (c !== col) c.classList.remove('drop-target'); });
  if (!col) return;
  col.classList.add('drop-target');
  const list = $('.cards', col);
  $$('.empty', list).forEach((x) => (x.hidden = true));
  const others = $$('.card:not(.placeholder)', list);
  const after = others.find((c) => { const r = c.getBoundingClientRect(); return e.clientY < r.top + r.height / 2; });
  if (after) { if (after.previousElementSibling !== drag.card) list.insertBefore(drag.card, after); }
  else if (list.lastElementChild !== drag.card) list.append(drag.card);

  // auto-scroll near the edges
  const lr = list.getBoundingClientRect();
  if (e.clientY < lr.top + 40) list.scrollTop -= 12;
  else if (e.clientY > lr.bottom - 40) list.scrollTop += 12;
  const board = $('#board');
  const br = board.getBoundingClientRect();
  if (e.clientX < br.left + 30) board.scrollLeft -= 14;
  else if (e.clientX > br.right - 30) board.scrollLeft += 14;
}

function endBoardDrag(d) {
  d.ghost.remove();
  $$('.col.drop-target').forEach((c) => c.classList.remove('drop-target'));
  const list = d.card.parentElement;
  const prev = d.card.previousElementSibling;
  const next = d.card.nextElementSibling;
  const beforeId = prev && prev.classList.contains('card') ? Number(prev.dataset.id) : null;
  const afterId = next && next.classList.contains('card') ? Number(next.dataset.id) : null;
  d.card.classList.remove('placeholder');
  moveTo(d.it.id, list.dataset.status, beforeId, afterId);
}

function moveWallDrag(dx, dy) {
  drag.nx = Math.max(0, Math.round(drag.ox + dx));
  drag.ny = Math.max(0, Math.round(drag.oy + dy));
  drag.card.style.left = `${drag.nx}px`;
  drag.card.style.top = `${drag.ny}px`;
  const canvas = $('#wallCanvas');
  canvas.style.width = `${Math.max(parseFloat(canvas.style.width) || 0, drag.nx + 300)}px`;
  canvas.style.height = `${Math.max(parseFloat(canvas.style.height) || 0, drag.ny + 260)}px`;
}

async function endWallDrag(d) {
  d.card.classList.remove('dragging');
  const x = d.nx ?? d.it.x, y = d.ny ?? d.it.y;
  d.it.x = x; d.it.y = y;
  await patch(d.it.id, { x, y, z: d.it.z });
  sizeWall();
}

/* Quick actions on cards */
document.addEventListener('click', (e) => {
  const b = e.target.closest('.card .actions button');
  if (!b) return;
  const id = Number(b.closest('.card').dataset.id);
  if (b.dataset.act === 'done') setStatus(id, 'done');
  else if (b.dataset.act === 'reopen') setStatus(id, 'todo');
  else if (b.dataset.act === 'delete') deleteItem(id);
});

document.addEventListener('keydown', (e) => {
  const card = e.target.closest && e.target.closest('.card');
  if (!card || e.target.matches('input, textarea')) return;
  const id = Number(card.dataset.id);
  if (e.key === 'Enter') { e.preventDefault(); openEditor(id); }
  else if (e.key === 'Delete') { e.preventDefault(); deleteItem(id); }
});

/* Wall: double-click sticks a note there */
$('#wall').addEventListener('dblclick', (e) => {
  if (e.target.closest('.card')) return;
  const r = $('#wallCanvas').getBoundingClientRect();
  newNote({ x: Math.max(0, Math.round(e.clientX - r.left - 110)), y: Math.max(0, Math.round(e.clientY - r.top - 30)) });
});

/* ================= Editor ================= */
const ed = {
  root: $('#editor'),
  paper: $('#editorPaper'),
  title: $('#edTitle'),
  body: $('#edBody'),
  due: $('#edDue'),
  priority: $('#edPriority'),
  saved: $('#savedState'),
  meta: $('#edMeta'),
  timer: 0,
  dirty: {},
};

function buildSwatches() {
  const box = $('#edColors');
  box.replaceChildren();
  for (const key of COLORS) {
    const b = document.createElement('button');
    b.className = `swatch c-${key}`;
    b.dataset.color = key;
    b.setAttribute('role', 'radio');
    b.title = colorName(key);
    b.setAttribute('aria-label', colorName(key));
    b.innerHTML = icon('check');
    box.append(b);
  }
}

function openEditor(id, focusTitle = false) {
  const it = find(id);
  if (!it) return;
  if (state.editing && state.editing !== id) flushSave();
  closePanels();
  state.editing = id;
  ed.root.hidden = false;
  document.body.classList.add('editing');
  fillEditor(it);
  $$('.card.selected').forEach((c) => c.classList.remove('selected'));
  $$(`.card[data-id="${Number(id)}"]`).forEach((c) => c.classList.add('selected'));
  ed.saved.textContent = '';
  requestAnimationFrame(() => {
    const target = focusTitle || !it.title ? ed.title : ed.body;
    target.focus();
    target.setSelectionRange(target.value.length, target.value.length);
  });
}

function fillEditor(it) {
  if (!it) return;
  if (document.activeElement !== ed.title) ed.title.value = it.title || '';
  if (document.activeElement !== ed.body) ed.body.value = it.body || '';
  autosize(ed.title);
  ed.paper.className = `editor-paper c-${COLORS.includes(it.color) ? it.color : 'yellow'}`;
  $$('.swatch').forEach((s) => s.setAttribute('aria-checked', String(s.dataset.color === it.color)));
  $$('#edStatus button').forEach((b) => b.setAttribute('aria-checked', String(b.dataset.v === it.status)));
  ed.due.value = it.due || '';
  ed.priority.checked = Number(it.priority) > 0;
  const parts = [];
  if (it.created_at) parts.push(t('meta.created', stamp(it.created_at)));
  if (it.status === 'done' && it.done_at) parts.push(t('meta.done', stamp(it.done_at)));
  ed.meta.textContent = parts.join(' · ');
}

function closeEditor(skipSave = false) {
  if (!state.editing) return;
  const id = state.editing;
  if (!skipSave) flushSave();
  state.editing = null;
  ed.root.hidden = true;
  if (!state.panel) document.body.classList.remove('editing');
  $$('.card.selected').forEach((c) => c.classList.remove('selected'));
  const it = find(id);
  // a note created and left blank is discarded silently
  if (!skipSave && it && !it.title.trim() && !it.body.trim()) {
    call('delete', { id }).then(() => { state.items = state.items.filter((i) => i.id !== id); render(); }).catch(failed);
    return;
  }
  const card = document.querySelector(`.card[data-id="${Number(id)}"]`);
  if (card) card.focus({ preventScroll: true });
}

function queueSave(fields) {
  Object.assign(ed.dirty, fields);
  const it = find(state.editing);
  if (it) Object.assign(it, fields);
  ed.saved.textContent = t('saving');
  ed.saved.classList.remove('ok');
  clearTimeout(ed.timer);
  ed.timer = setTimeout(flushSave, 400);
}

async function flushSave() {
  clearTimeout(ed.timer);
  const id = state.editing;
  const fields = ed.dirty;
  ed.dirty = {};
  if (!id || !Object.keys(fields).length) return;
  const it = await patch(id, fields);
  if (it) {
    if (state.editing === id) { ed.saved.textContent = t('saved'); ed.saved.classList.add('ok'); }
    render();
  }
}

function autosize(el) { el.style.height = 'auto'; el.style.height = `${el.scrollHeight}px`; }

ed.title.addEventListener('input', () => { autosize(ed.title); queueSave({ title: ed.title.value }); });
ed.title.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); ed.body.focus(); } });
ed.body.addEventListener('input', () => queueSave({ body: ed.body.value }));

$('#edColors').addEventListener('click', (e) => {
  const s = e.target.closest('.swatch');
  if (!s || !state.editing) return;
  queueSave({ color: s.dataset.color });
  fillEditor(find(state.editing));
  flushSave();
});

$('#edStatus').addEventListener('click', (e) => {
  const b = e.target.closest('button');
  if (!b || !state.editing) return;
  flushSave().then(() => setStatus(state.editing, b.dataset.v));
});

ed.due.addEventListener('change', () => { queueSave({ due: ed.due.value || null }); flushSave(); });
$$('.due-row [data-due]').forEach((b) => b.addEventListener('click', () => {
  const v = b.dataset.due === '' ? null : addDays(Number(b.dataset.due));
  ed.due.value = v || '';
  queueSave({ due: v });
  flushSave();
}));
ed.priority.addEventListener('change', () => { queueSave({ priority: ed.priority.checked ? 1 : 0 }); flushSave(); });
$('#edDelete').addEventListener('click', () => state.editing && deleteItem(state.editing));
$('#closeEditor').addEventListener('click', () => closeEditor());

/* ================= Toast with undo ================= */
let toastTimer = 0;
function toast(text, actionLabel, action, ms = 6000) {
  const el = $('#toast');
  $('#toastText').textContent = text;
  const btn = $('#toastAction');
  btn.hidden = !actionLabel;
  btn.textContent = actionLabel || '';
  btn.onclick = action ? () => { el.hidden = true; action(); } : null;
  el.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => (el.hidden = true), ms);
}

/* ================= Top bar ================= */
$$('.topbar .seg button').forEach((b) => b.addEventListener('click', () => setView(b.dataset.view)));

$('#todayChip').addEventListener('click', toggleToday);
function toggleToday() {
  state.todayOnly = !state.todayOnly;
  $('#todayChip').setAttribute('aria-pressed', String(state.todayOnly));
  render();
}

const search = $('#search');
search.addEventListener('input', () => { state.q = search.value.trim(); render(); });

$('#newBtn').addEventListener('click', () => newNote());
$$('.add-in-col').forEach((b) => b.addEventListener('click', () => newNote({ status: b.closest('.col').dataset.status })));

$('.quick-add').addEventListener('submit', async (e) => {
  e.preventDefault();
  const input = e.target.elements.t;
  const title = input.value.trim();
  if (!title) return;
  input.value = '';
  await newNote({ title, status: 'todo', open: false });
  input.focus();
});

const menu = $('#moreMenu');
const moreBtn = $('#moreBtn');
function toggleMenu(open = menu.hidden) {
  menu.hidden = !open;
  moreBtn.setAttribute('aria-expanded', String(open));
  if (open) {
    const r = moreBtn.getBoundingClientRect();
    menu.style.top = `${r.bottom + 6}px`;
    menu.style.right = `${Math.max(8, innerWidth - r.right)}px`;
    menu.querySelector('button').focus();
  }
}
moreBtn.addEventListener('click', (e) => { e.stopPropagation(); toggleMenu(); });
document.addEventListener('click', (e) => { if (!menu.hidden && !e.target.closest('#moreMenu')) toggleMenu(false); });

$$('#moreMenu [data-lang]').forEach((b) => b.addEventListener('click', () => { toggleMenu(false); setLanguage(b.dataset.lang); }));

$('#archiveDoneBtn').addEventListener('click', async () => {
  toggleMenu(false);
  const n = state.items.filter((i) => i.status === 'done').length;
  if (!n) { toast(t('toast.noDone')); return; }
  try {
    await call('archiveDone');
    if (state.editing && find(state.editing)?.status === 'done') closeEditor(true);
    state.items = state.items.filter((i) => i.status !== 'done');
    render();
    toast(t('toast.archived', n));
  } catch (err) { failed(err); }
});

$('#autostartBtn').addEventListener('click', async () => {
  try {
    const on = await call('setAutostart', { on: !state.autostart });
    setAutostartUi(on);
    toast(on ? t('toast.autostartOn') : t('toast.autostartOff'));
  } catch (err) { failed(err); }
  toggleMenu(false);
});
function setAutostartUi(on) {
  state.autostart = !!on;
  $('#autostartBtn').setAttribute('aria-checked', String(state.autostart));
}

/* ================= Side panels: automations and archive ================= */
const panels = { auto: $('#autoPanel'), arch: $('#archPanel') };

function openPanel(name) {
  if (!name) return closePanels();
  if (state.editing) closeEditor();
  for (const [k, el] of Object.entries(panels)) el.hidden = k !== name;
  state.panel = name;
  document.body.classList.add('editing');
  if (name === 'auto') loadRules(); else loadArchived();
  requestAnimationFrame(() => panels[name].querySelector('[data-close]').focus());
}

function closePanels() {
  if (!state.panel) return;
  Object.values(panels).forEach((el) => (el.hidden = true));
  state.panel = null;
  if (!state.editing) document.body.classList.remove('editing');
}
$$('[data-close]').forEach((b) => b.addEventListener('click', closePanels));

async function reloadItems(changed) {
  try {
    state.items = (await call('list')) || [];
    if (state.editing && !find(state.editing)) closeEditor(true);
    else if (state.editing) fillEditor(find(state.editing));
    render();
    if (state.panel === 'auto') loadRules();
    if (changed) toast(t('toast.auto', changed));
  } catch (err) { failed(err); }
}

/* ---- Rule sentences (only fixed translations reach innerHTML) ---- */
function condText(r) {
  const n = Math.max(0, Math.min(365, Number(r.days) || 0));
  const d = t('cond.days', n);
  const col = STATUSES.includes(r.status) ? colName(r.status) : null;
  const where = col ? t('cond.where', col) : '';
  switch (r.cond) {
    case 'inStatus': return t('cond.inStatus', col || '?', d);
    case 'idle': return t('cond.idle', where, d);
    case 'overdue': return n === 0 ? t('cond.overdue0', where) : t('cond.overdue', where, d);
    case 'dueSoon': return n === 0 ? t('cond.dueSoon0', where) : t('cond.dueSoon', where, d);
  }
  return '';
}
function actionText(r) {
  switch (r.action) {
    case 'move': return STATUSES.includes(r.value) ? t('act.move', colName(r.value)) : '';
    case 'archive': return t('act.archive');
    case 'priority': return t('act.priority');
    case 'color': return COLORS.includes(r.value) ? t('act.color', colorName(r.value)) : '';
  }
  return '';
}
const ruleKey = (r) => [r.cond, r.status || 'any', Number(r.days) || 0, r.action, r.value || ''].join('|');

const SUGGESTIONS = [
  { label: 'sugg.archiveDone', cond: 'inStatus', status: 'done', days: 5, action: 'archive' },
  { label: 'sugg.stale', cond: 'inStatus', status: 'todo', days: 5, action: 'move', value: 'doing' },
  { label: 'sugg.overdue', cond: 'overdue', status: 'any', days: 0, action: 'priority' },
  { label: 'sugg.dueToday', cond: 'dueSoon', status: 'todo', days: 0, action: 'move', value: 'doing' },
  { label: 'sugg.untouched', cond: 'idle', status: 'any', days: 14, action: 'color', value: 'gray' },
];

/* ---- Rule list ---- */
let rulesCache = [];

async function loadRules() {
  try {
    rulesCache = (await call('listRules')) || [];
    renderRules();
    updateRulePreview();
  } catch (err) { failed(err); }
}

function renderRules() {
  const list = $('#ruleList');
  list.replaceChildren();
  if (!rulesCache.length) {
    const p = document.createElement('li');
    p.className = 'muted-empty';
    p.textContent = t('auto.none');
    list.append(p);
  }
  for (const r of rulesCache) {
    const on = Number(r.enabled) > 0;
    const li = document.createElement('li');
    li.className = 'rule' + (on ? '' : ' off');
    li.innerHTML = `
      <div class="rule-text">${condText(r)}<span class="then">${icon('arrow')}<span></span></span></div>
      <label class="switch"><input type="checkbox"><span class="track"></span></label>
      <div class="rule-foot">
        <span class="stat${r.pending > 0 ? ' hot' : ''}"></span>
        <button class="btn ghost sm" data-run>${icon('play')}<span></span></button>
        <button class="icon-btn sm" data-del>${icon('trash')}</button>
      </div>`;
    $('.then span', li).textContent = actionText(r);
    const sw = $('.switch', li);
    sw.title = on ? t('rule.active') : t('rule.paused');
    const input = $('input', sw);
    input.checked = on;
    input.setAttribute('aria-label', t('rule.toggleAria'));
    const run = $('[data-run]', li);
    run.title = t('rule.runTitle');
    $('span', run).textContent = t('rule.run');
    const del = $('[data-del]', li);
    del.title = t('rule.delete');
    del.setAttribute('aria-label', t('rule.delete'));
    const stat = [r.pending > 0 ? t('rule.pending', r.pending) : t('rule.nothing')];
    if (r.last_run) stat.push(t('rule.acted', stamp(r.last_run), Number(r.last_count) || 0));
    $('.stat', li).textContent = stat.join(' · ');

    input.addEventListener('change', async () => {
      try {
        await call('toggleRule', { id: r.id, on: input.checked });
        r.enabled = input.checked ? 1 : 0;
        li.classList.toggle('off', !input.checked);
        sw.title = input.checked ? t('rule.active') : t('rule.paused');
      } catch (err) { failed(err); }
    });
    run.addEventListener('click', async () => {
      try {
        const n = await call('runRule', { id: r.id });
        toast(n ? t('toast.ruleApplied', n) : t('toast.ruleNone'));
        if (n) await reloadItems(); else loadRules();
      } catch (err) { failed(err); }
    });
    del.addEventListener('click', async () => {
      try {
        await call('deleteRule', { id: r.id });
        rulesCache = rulesCache.filter((x) => x.id !== r.id);
        renderRules();
        toast(t('toast.ruleDeleted'), t('undo'), async () => { await call('saveRule', r); loadRules(); });
      } catch (err) { failed(err); }
    });
    list.append(li);
  }
  renderSuggestions();
}

function renderSuggestions() {
  const have = new Set(rulesCache.map(ruleKey));
  const box = $('#suggestions');
  box.replaceChildren();
  for (const s of SUGGESTIONS) {
    if (have.has(ruleKey(s))) continue;
    const b = document.createElement('button');
    b.className = 'chip';
    b.innerHTML = icon('plus');
    b.append(t(s.label));
    b.addEventListener('click', () => createRule(s));
    box.append(b);
  }
  $('#suggestBox').hidden = !box.children.length;
}

async function createRule(r) {
  if (rulesCache.some((x) => ruleKey(x) === ruleKey(r))) { toast(t('toast.ruleExists')); return; }
  try {
    const { label, ...rule } = r; // eslint-disable-line no-unused-vars
    const saved = await call('saveRule', rule);
    rulesCache.push(saved);
    renderRules();
    if (saved.pending > 0) {
      toast(t('toast.ruleCreatedN', saved.pending), t('runNow'), async () => {
        const n = await call('runRule', { id: saved.id });
        toast(t('toast.ruleApplied', n));
        reloadItems();
      });
    } else toast(t('toast.ruleCreated'));
  } catch (err) { failed(err); }
}

/* ---- New rule form ---- */
const ruleForm = $('#ruleForm');

function buildActionOptions() {
  const sel = ruleForm.elements.action;
  const keep = sel.value || 'move:doing';
  sel.replaceChildren();
  const add = (value, label) => { const o = document.createElement('option'); o.value = value; o.textContent = label; sel.append(o); };
  add('move:doing', t('act.move', colName('doing')));
  add('move:todo', t('act.move', colName('todo')));
  add('move:done', t('act.move', colName('done')));
  add('archive', t('act.archive'));
  add('priority', t('act.priority'));
  for (const c of COLORS) add(`color:${c}`, t('act.color', colorName(c)));
  sel.value = keep;
}

function formRule() {
  const f = ruleForm.elements;
  const [action, value] = f.action.value.split(':');
  return { cond: f.cond.value, status: f.status.value, days: Math.max(0, Math.min(365, Number(f.days.value) || 0)), action, value: value || null };
}

function syncForm() {
  const f = ruleForm.elements;
  const anyOpt = f.status.querySelector('option[value="any"]');
  anyOpt.disabled = f.cond.value === 'inStatus';
  if (anyOpt.disabled && f.status.value === 'any') f.status.value = 'todo';
  $('[data-days-label]', ruleForm).textContent = f.cond.value === 'dueSoon' ? t('form.within') : t('form.moreThan');
  updateRulePreview();
}

let previewTimer = 0;
function updateRulePreview() {
  clearTimeout(previewTimer);
  previewTimer = setTimeout(async () => {
    const r = formRule();
    const out = $('#rulePreview');
    try {
      const n = await call('previewRule', r);
      const sentence = document.createElement('div');
      sentence.innerHTML = condText(r);
      sentence.append(` → ${actionText(r)}.`);
      out.replaceChildren(sentence, t('preview', n));
    } catch { out.textContent = ''; }
  }, 120);
}
ruleForm.addEventListener('change', syncForm);
ruleForm.elements.days.addEventListener('input', updateRulePreview);
ruleForm.addEventListener('submit', (e) => { e.preventDefault(); createRule(formRule()); });

$('#autoBtn').addEventListener('click', () => openPanel(state.panel === 'auto' ? null : 'auto'));
$('#menuAutoBtn').addEventListener('click', () => { toggleMenu(false); openPanel('auto'); });

/* ---- Archive ---- */
async function loadArchived() {
  const list = $('#archList');
  try {
    const items = (await call('listArchived')) || [];
    list.replaceChildren();
    if (!items.length) {
      const li = document.createElement('li');
      li.className = 'muted-empty';
      li.textContent = t('arch.none');
      list.append(li);
    }
    for (const it of items) {
      const li = document.createElement('li');
      li.className = `arch-item c-${COLORS.includes(it.color) ? it.color : 'yellow'}`;
      li.innerHTML = '<span class="t"></span><span class="d"></span><button class="btn"></button>';
      $('.t', li).textContent = (it.title || '').trim() || (it.body || '').trim().slice(0, 60) || t('card.empty');
      $('.d', li).textContent = stamp(it.archived_at || it.updated_at);
      const btn = $('button', li);
      btn.textContent = t('arch.restore');
      btn.addEventListener('click', async () => {
        try {
          upsert(await call('unarchive', { id: it.id }));
          li.remove();
          if (!list.children.length) loadArchived();
          render();
          toast(t('toast.restored'));
        } catch (err) { failed(err); }
      });
      list.append(li);
    }
  } catch (err) { failed(err); }
}
$('#showArchivedBtn').addEventListener('click', () => { toggleMenu(false); openPanel('arch'); });

/* ================= Keyboard ================= */
document.addEventListener('keydown', (e) => {
  const typing = e.target.matches && e.target.matches('input, textarea, select');
  const k = e.key.toLowerCase();

  if (e.key === 'Escape') {
    if (!menu.hidden) { toggleMenu(false); moreBtn.focus(); return; }
    if (state.editing) { closeEditor(); return; }
    if (state.panel) { closePanels(); return; }
    if (typing && e.target === search && search.value) { search.value = ''; state.q = ''; render(); return; }
    if (typing) e.target.blur();
    return;
  }
  if (e.ctrlKey && !e.altKey && k === 'f') { e.preventDefault(); search.focus(); search.select(); return; }
  if (e.ctrlKey && !e.altKey && k === 'n') { e.preventDefault(); newNote(); return; }
  if (typing || e.ctrlKey || e.altKey || e.metaKey) return;
  if (k === 'n') { e.preventDefault(); newNote(); }
  else if (k === '1') setView('board');
  else if (k === '2') setView('wall');
  else if (k === 'h') toggleToday();
  else if (k === 'a') openPanel(state.panel === 'auto' ? null : 'auto');
  else if (k === '/') { e.preventDefault(); search.focus(); }
});

/* No browser context menu outside text fields */
document.addEventListener('contextmenu', (e) => { if (!e.target.matches('input, textarea')) e.preventDefault(); });

/* ================= Events from the native host ================= */
function onNativeEvent(name, data) {
  if (name === 'newNote') newNote();
  else if (name === 'refresh') reloadItems(Number(data) || 0);
  else if (name === 'autostartOn') setAutostartUi(true);
  else if (name === 'autostartOff') setAutostartUi(false);
}

/* Day change: refresh "Today"/"Overdue" labels */
setInterval(() => { if (state.day !== today()) { state.day = today(); render(); } }, 60_000);
window.addEventListener('focus', () => { if (state.day !== today()) { state.day = today(); render(); } });

/* ================= Start-up ================= */
(async function init() {
  hydrateIcons();
  if (DEMO) seedDemo(params.get('lang') === 'pt' ? 'pt' : 'en');
  try {
    const info = (await call('ready')) || {};
    setAutostartUi(info.autostart);
    const [items, view, lang] = await Promise.all([call('list'), call('getSetting', { key: 'view' }), call('getSetting', { key: 'lang' })]);
    state.items = items || [];
    setLanguage(lang || info.lang || LANG, false);
    setView(view === 'wall' ? 'wall' : 'board', false);
    if (info.autoChanged > 0) toast(t('toast.auto', info.autoChanged));
  } catch (err) {
    failed(err);
  }

  // Screenshot helpers (browser demo only): ?demo&lang=en&view=wall&edit=1&panel=auto
  if (DEMO) {
    if (params.get('edit')) openEditor(Number(params.get('edit')));
    if (params.get('panel')) openPanel(params.get('panel') === 'arch' ? 'arch' : 'auto');
    if (params.has('today')) toggleToday();
  }
})();
