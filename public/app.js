import * as A from "./api.js?v=6";
import * as R from "./recs.js?v=6";
import { createFx } from "./fx.js?v=6";
import { downloadOptions, download } from "./download.js?v=6";

const $ = (id) => document.getElementById(id);
const root = document.documentElement;
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
const fmt = (s) => (s > 0 ? `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, "0")}` : "—");
const cv = (u, i = 0) => (u ? `<img src="${esc(u)}" alt="" loading="lazy" decoding="async" onerror="this.outerHTML='<span class=&quot;cv${i % 8}&quot; style=&quot;display:block;width:100%;height:100%&quot;></span>'">` : `<span class="cv${i % 8}" style="display:block;width:100%;height:100%"></span>`);
const store = { get(k, d) { try { const v = localStorage.getItem(k); return v === null ? d : JSON.parse(v); } catch { return d; } }, set(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); } catch {} } };

const THEMES = [
  ["Монолит", "#050505", "#f2f2f2"], ["Бумага", "#f5f5f4", "#121212"], ["Брутализм", "#ffffff", "#000000"], ["Плёнка", "#141414", "#e6e6e6"],
  ["Созвездие", "#000000", "#fafafa"], ["Осциллограф", "#0a0a0a", "#ededed"], ["Снег", "#0e0e0f", "#f0f0f0"], ["Сетка", "#ffffff", "#0a0a0a"],
  ["Винил", "#101010", "#eeeeee"], ["Стекло", "#161616", "#f4f4f4"],
];
const GENRE_LIST = ["Electronic", "Hip-Hop/Rap", "Lo-Fi", "House", "Deep House", "Techno", "Drum & Bass", "Ambient", "Pop", "Rock", "Alternative", "Jazz", "R&B/Soul", "Classical", "Acoustic", "Trap", "Downtempo", "Synthwave"];
const MOOD_LIST = ["Peaceful", "Easygoing", "Cool", "Melancholy", "Energizing", "Upbeat", "Fiery", "Romantic", "Brooding", "Sophisticated"];

// ---------------- toasts ----------------
function toast(text, { progress = false, ms = 3800 } = {}) {
  const el = document.createElement("div");
  el.className = "toast";
  el.innerHTML = `<div>${esc(text)}</div>${progress ? '<div class="pg"><i></i></div>' : ""}`;
  $("toasts").appendChild(el);
  const api = { set(p) { const i = el.querySelector(".pg i"); if (i) i.style.width = Math.round(p * 100) + "%"; }, text(t) { el.firstChild.textContent = t; }, close(after = 0) { setTimeout(() => el.remove(), after); } };
  if (!progress) api.close(ms);
  return api;
}

// ---------------- theme + fx ----------------
const fx = createFx($("fx"), () => $("wave"));
function setTheme(v) {
  root.dataset.v = v; store.set("echo.theme", String(v));
  document.querySelectorAll(".theme").forEach((b) => b.setAttribute("aria-pressed", String(+b.dataset.v === +v)));
  requestAnimationFrame(() => fx.setTheme(+v));
}

// ---------------- auth ----------------
let me = null;
async function refreshMe() {
  try {
    const d = await A.api("/api/me");
    me = d.user; A.setJamendo(d.jamendo);
  } catch { me = null; }
  $("loginLink").hidden = !!me; $("acctIn").hidden = !me;
  if (me) { $("acctEmail").textContent = me.email; $("acctRole").textContent = me.role === "admin" ? "администратор" : "слушатель"; }
  $("navUpload").hidden = me?.role !== "admin";
  if (me) await pullProfile();
}
async function pullProfile() {
  try {
    const d = await A.api("/api/profile");
    if (d.data) R.mergeProfile(d.data);
    pushProfile();
  } catch {}
}
let pushT;
let syncWarned = false;
function pushProfile() {
  if (!me) return;
  clearTimeout(pushT);
  pushT = setTimeout(() => A.api("/api/profile", { method: "PUT", body: { data: R.P } })
    .then(() => { syncWarned = false; })
    .catch((e) => { if (!syncWarned) { syncWarned = true; toast(`Не удалось сохранить вкус в аккаунт: ${e.message}`); } }), 2500);
}
R.onProfileChange(pushProfile);

let lgStep = "email";
$("lgForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const email = $("lgEmail").value.trim(), note = $("lgNote"), go = $("lgGo");
  go.disabled = true;
  try {
    if (lgStep === "email") {
      note.textContent = "Отправляем код…";
      await A.api("/api/auth/code", { method: "POST", body: { email } });
      lgStep = "code"; $("lgCodeWrap").hidden = false; $("lgEmail").readOnly = true; $("lgBack").hidden = false;
      go.textContent = "Войти"; note.textContent = `Письмо отправлено на ${email}. Нажмите в нём «Войти» или введите код. Проверьте и папку «Спам».`; $("lgCode").focus();
    } else {
      const d = await A.api("/api/auth/verify", { method: "POST", body: { email, code: $("lgCode").value } });
      toast(d.user.role === "admin" ? "Вы вошли как администратор" : "Вы вошли");
      resetLogin(); await refreshMe(); location.hash = R.hasTaste() ? "#home" : "#welcome";
    }
  } catch (err) { note.textContent = err.message; }
  go.disabled = false;
});
function resetLogin() { lgStep = "email"; $("lgCodeWrap").hidden = true; $("lgEmail").readOnly = false; $("lgBack").hidden = true; $("lgGo").textContent = "Получить код"; $("lgNote").textContent = ""; $("lgCode").value = ""; }
$("lgBack").onclick = resetLogin;
$("logout").onclick = async () => {
  await A.api("/api/auth/logout", { method: "POST" }).catch(() => {});
  clearTimeout(pushT); me = null; R.clearProfile(); resetPool();
  await refreshMe(); toast("Вы вышли"); location.hash = "#home"; route();
};

// Magic link from the email: #ml-<hex email>-<token>
async function magicLogin() {
  const m = /^#ml-([0-9a-f]{6,400})-([A-Za-z0-9_-]{20,100})$/.exec(location.hash);
  if (!m) return false;
  history.replaceState(null, "", location.pathname + "#home");
  let email = "";
  try { email = new TextDecoder().decode(new Uint8Array(m[1].match(/../g).map((h) => parseInt(h, 16)))); } catch {}
  try {
    const d = await A.api("/api/auth/verify", { method: "POST", body: { email, link: m[2] } });
    toast(d.user.role === "admin" ? "Вы вошли как администратор" : "Вы вошли");
    await refreshMe();
    location.hash = R.hasTaste() ? "#home" : "#welcome";
  } catch (e) {
    toast(e.message || "Ссылка устарела. Запросите новое письмо.");
    location.hash = "#login";
  }
  return true;
}

// ---------------- player ----------------
const audio = $("audio");
audio.volume = store.get("echo.vol", 80) / 100; $("vol").value = Math.round(audio.volume * 100);
const pl = { queue: [], i: -1, mode: "list", mood: null, counted: false, startedAt: 0 };
let waitingNext = false;
const cur = () => pl.queue[pl.i];

function playList(list, start = 0, mode = "list") {
  if (!list.length) return toast("Здесь пока нечего играть");
  pl.queue = list.map(A.refresh); pl.i = start; pl.mode = mode; load(true);
}
function load(autoplay) {
  const t = cur(); if (!t) return;
  pl.counted = false;
  R.started(t);
  audio.src = t.stream;
  if (autoplay) audio.play().catch(() => {});
  sync();
  if ("mediaSession" in navigator) {
    navigator.mediaSession.metadata = new MediaMetadata({ title: t.title, artist: t.artist, album: t.genre || "Эхо", artwork: t.cover ? [{ src: t.cover, sizes: "480x480" }] : [] });
  }
  if (pl.mode === "wave" && pl.queue.length - pl.i < 4) refillWave();
  // warm the next track on the server so skipping starts instantly
  const nx = pl.queue[pl.i + 1];
  if (nx?.src === "audius") setTimeout(() => fetch(A.refresh(nx).stream, { headers: { Range: "bytes=0-1" } }).catch(() => {}), 1500);
}
function next(reason = "skip") {
  const t = cur();
  if (t && reason === "skip" && audio.currentTime < 30) R.skipped(t, audio.currentTime);
  if (pl.i < pl.queue.length - 1) { pl.i++; load(true); }
  else if (pl.mode === "wave") { if (waitingNext) return; waitingNext = true; refillWave().then(() => { if (pl.i < pl.queue.length - 1) { pl.i++; load(true); } }).finally(() => { waitingNext = false; }); }
  else { audio.pause(); sync(); }
}
function prev() { if (audio.currentTime > 4 || pl.i <= 0) { audio.currentTime = 0; } else { pl.i--; load(true); } }
function toggle() { if (!cur()) return startWave(); audio.paused ? audio.play().catch(() => {}) : audio.pause(); }

audio.addEventListener("play", sync); audio.addEventListener("pause", sync);
audio.addEventListener("timeupdate", () => {
  const t = cur(); if (!t) return;
  const d = audio.duration || t.duration || 0, p = d ? (audio.currentTime / d) * 100 : 0;
  $("fill").style.width = p + "%"; $("mini").style.width = p + "%"; $("cur").textContent = fmt(audio.currentTime); $("dur").textContent = fmt(d);
  $("seek").setAttribute("aria-valuenow", Math.round(p));
  if (!pl.counted && audio.currentTime > 30) { pl.counted = true; R.listened(t); }
});
audio.addEventListener("ended", () => { const t = cur(); if (t) R.finished(t); next("end"); });
let errRun = 0;
audio.addEventListener("playing", () => { errRun = 0; });
audio.addEventListener("error", () => {
  if (!cur() || !audio.getAttribute("src")) return;
  errRun++;
  if (errRun >= 4) { errRun = 0; audio.pause(); sync(); return toast("Несколько треков подряд не загрузились. Проверьте соединение и нажмите плей ещё раз."); }
  toast("Трек не загрузился, включаю следующий"); setTimeout(() => next("error"), 600);
});
$("play").onclick = toggle; $("next").onclick = () => next(); $("prev").onclick = prev;
$("shuf").onclick = (e) => {
  const on = e.currentTarget.getAttribute("aria-pressed") !== "true"; e.currentTarget.setAttribute("aria-pressed", String(on));
  if (on && pl.queue.length > pl.i + 2) { const rest = pl.queue.splice(pl.i + 1); rest.sort(() => Math.random() - 0.5); pl.queue.push(...rest); }
};
$("seek").onclick = (e) => { const r = e.currentTarget.getBoundingClientRect(); if (audio.duration) audio.currentTime = ((e.clientX - r.left) / r.width) * audio.duration; };
$("seek").onkeydown = (e) => { if (e.key === "ArrowRight") audio.currentTime += 5; if (e.key === "ArrowLeft") audio.currentTime -= 5; };
$("vol").oninput = (e) => { audio.volume = e.target.value / 100; store.set("echo.vol", +e.target.value); };
const doLike = (t) => { if (!t) return; const on = R.like(t); toast(on ? "Добавлено в любимое" : "Убрано из любимого"); sync(); if (!$("v-welcome").hidden) $("wlSummary").textContent = `Песен в любимом: ${Object.keys(R.P.likes).length}`; };
$("npLike").onclick = () => doLike(cur()); $("wLike").onclick = () => doLike(cur());
$("wBan").onclick = () => { const t = cur(); if (!t) return; R.ban(t); toast("Больше не покажу похожее так часто"); next("ban"); };
$("npDl").onclick = (e) => openDlMenu(cur(), e.currentTarget);
if ("mediaSession" in navigator) {
  navigator.mediaSession.setActionHandler("play", () => audio.play());
  navigator.mediaSession.setActionHandler("pause", () => audio.pause());
  navigator.mediaSession.setActionHandler("nexttrack", () => next());
  navigator.mediaSession.setActionHandler("previoustrack", prev);
}
document.addEventListener("keydown", (e) => {
  if (e.target.matches("input,textarea") || e.target.closest("button,[role=slider],.tr")) return;
  if (e.code === "Space") { e.preventDefault(); toggle(); }
  if (e.key === "/") { e.preventDefault(); location.hash = "#search"; setTimeout(() => $("q").focus(), 0); }
});

function sync() {
  const t = cur(), playing = !!t && !audio.paused;
  fx.energy(playing);
  $("player").classList.toggle("playing", playing);
  $("wave").classList.toggle("playing", playing && pl.mode === "wave");
  $("playIcon").setAttribute("href", playing ? "#i-pause" : "#i-play");
  $("play").setAttribute("aria-label", playing ? "Пауза" : "Воспроизвести");
  if (t) {
    $("npT").innerHTML = `${esc(t.title)}<span class="q">${esc(t.quality)}</span>`; $("npA").textContent = t.artist;
    $("npC").innerHTML = cv(t.coverSm, 1);
    $("npLike").setAttribute("aria-pressed", String(R.isLiked(t)));
    $("npDl").disabled = !t.downloadable; $("npDl").title = t.downloadable ? "Скачать" : "Автор не разрешил скачивание";
  }
  const waveOn = pl.mode === "wave" && t;
  $("vibeIcon").setAttribute("href", playing && pl.mode === "wave" ? "#i-pause" : "#i-play");
  $("vibeSvg").setAttribute("class", playing && pl.mode === "wave" ? "" : "play");
  $("vibeL").textContent = waveOn ? (playing ? "Сейчас играет" : "На паузе") : "Моя волна";
  $("vibeT").textContent = waveOn ? t.title : "Нажмите, чтобы начать";
  $("vibeA").textContent = waveOn ? `${t.artist}${t.genre ? " · " + t.genre : ""}` : (pl.mood ? `Настроение: ${pl.mood}` : "Подстроится под ваш вкус");
  $("vibeActs").hidden = !waveOn;
  if (waveOn) $("wLike").setAttribute("aria-pressed", String(R.isLiked(t)));
  $("bigArt").innerHTML = waveOn ? cv(t.cover, 3) : `<span class="cv3" style="display:block;width:100%;height:100%"></span>`;
  const nx = pl.mode === "wave" ? pl.queue[pl.i + 1] : null;
  $("nextT").textContent = nx ? nx.title : "—"; $("nextA").textContent = nx ? nx.artist : "";
  document.querySelectorAll(".tr").forEach((r) => { const now = t && r.dataset.id === t.id; r.classList.toggle("now", !!now); r.classList.toggle("playing", !!now && playing); });
  document.querySelectorAll(".tr .like").forEach((b) => b.setAttribute("aria-pressed", String(!!R.P.likes[b.dataset.id])));
}

// ---------------- wave ----------------
let pool = [], poolKey = "", poolAt = 0, poolRun = null, refilling = null, waveGen = 0;
let W = { ...R.WAVE_DEFAULTS, ...store.get("echo.wave", {}) };
const waveCtx = (extra = {}) => ({ mood: pl.mood, cfg: W, ...extra });

function poolJobs(mood) {
  const pre = mood && R.MOOD_PRESETS[mood];
  const jobs = [];
  if (pre?.tags) {
    // genre waves (Phonk…) live in search results more than in the genre charts
    pre.queries.forEach((q) => jobs.push(A.auSearch(q, { limit: 40 })));
    pre.genres.slice(0, 2).forEach((g) => jobs.push(A.auTrending({ genre: g, limit: 30 })));
  } else {
    jobs.push(A.auTrending({ time: "week", limit: 60 }));
    for (const g of R.topGenres(3)) jobs.push(A.auTrending({ genre: g, limit: 30 }));
    jobs.push(A.auTrending({ time: "month", limit: 40 }), A.auUnderground(30));
    for (const a of R.topArtists(3)) if (a.startsWith("au:")) jobs.push(A.auArtist(a));
    for (const tag of R.topTags(2)) jobs.push(A.auSearch(tag, { limit: 20 }));
    // similar to what you love: tags and genre of a few liked tracks
    for (const seed of R.likeSeeds(3)) {
      const tags = (seed.tags || []).slice(0, 2).join(" ");
      if (tags) jobs.push(A.auSearch(tags, { limit: 20 }));
      if (seed.genre) jobs.push(A.auTrending({ genre: seed.genre, limit: 30 }));
    }
    // exploration: one genre the listener has never played
    const explore = R.unexploredGenre();
    if (explore) jobs.push(A.auTrending({ genre: explore, limit: 15 }));
    if (pre) { pre.genres.slice(0, 3).forEach((g) => jobs.push(A.auTrending({ genre: g, limit: 30 }))); pre.queries.forEach((q) => jobs.push(A.auSearch(q, { limit: 25 }))); }
  }
  jobs.push(A.localTracks());
  if (W.novelty < 60) jobs.push(Promise.resolve(Object.values(R.P.likes).map(A.refresh)));
  if (A.hasJamendo()) {
    const tags = (pre?.tags || (pre ? pre.genres : R.topGenres(3)).map(A.jTag)).filter(Boolean).slice(0, 3).join("+");
    jobs.push(A.jaPopular({ tags: tags || undefined, limit: 40, instrumental: W.vocals === "instrumental" }));
  }
  return jobs;
}
// Resolves `first` as soon as there is enough to start playing; `all` when every source has answered.
function ensurePool(mood, quick = false) {
  const key = (mood || "") + "|" + W.novelty;
  if (poolKey === key && poolAt && Date.now() - poolAt < 10 * 60e3) return Promise.resolve(pool);
  if (!poolRun || poolRun.key !== key) {
    pool = []; poolAt = 0; poolKey = key;
    let release; const first = new Promise((r) => (release = r));
    const run = { key };
    const add = (list) => { if (poolRun !== run) return; pool = A.dedupe([...pool, ...list]); if (R.pick(pool, 6, waveCtx({ explore: 0 })).length >= 6) release(pool); };
    run.all = Promise.allSettled(poolJobs(mood).map((j) => j.then(add))).then(() => { if (poolRun === run) poolAt = Date.now(); release(pool); return pool; });
    run.first = first;
    poolRun = run;
  }
  return quick ? poolRun.first : poolRun.all;
}
function resetPool() { pool = []; poolAt = 0; poolRun = null; poolKey = ""; refilling = null; waveGen++; }
function refillWave() {
  if (refilling) return refilling;
  const gen = waveGen;
  refilling = (async () => {
    const p = await ensurePool(pl.mood, true);
    if (gen !== waveGen) return;
    const inQ = new Set(pl.queue.map((t) => t.id));
    const add = R.pick(p.filter((t) => !inQ.has(t.id)), 8, waveCtx({ prev: pl.queue.at(-1), recent: pl.queue.slice(-8) }));
    pl.queue.push(...add); sync();
  })().finally(() => { refilling = null; });
  return refilling;
}
async function startWave() {
  if (pl.mode === "wave" && cur()) return toggle();
  if (startWave.busy) return;
  startWave.busy = true;
  $("vibeT").textContent = "Подбираю треки…";
  try {
    const p = await ensurePool(pl.mood, true);
    const q = R.pick(p, 6, waveCtx({ prev: cur(), recent: pl.queue.slice(Math.max(0, pl.i - 7), pl.i + 1) }));
    if (!q.length) throw new Error("empty");
    playList(q, 0, "wave");
  } catch {
    $("vibeT").textContent = W.onlyDl ? "Не нашлось треков со скачиванием" : "Каталог не ответил";
    $("vibeA").textContent = W.onlyDl ? "Ослабьте настройки волны" : "Нажмите плей ещё раз через пару секунд";
    resetPool();
  } finally { startWave.busy = false; }
}
async function retuneWave() {
  resetPool();
  if (pl.mode === "wave" && cur()) {
    pl.queue.splice(pl.i + 1); sync();
    try { await refillWave(); } catch { toast("Не удалось подобрать треки под эти настройки"); }
  }
}

// wave settings panel
function renderWaveCfg() {
  $("wcNovelty").value = W.novelty; $("wcPop").value = W.popularity; $("wcDl").checked = W.onlyDl;
  document.querySelectorAll("#wcTempo button").forEach((b) => b.setAttribute("aria-pressed", String(b.dataset.v === W.tempo)));
  document.querySelectorAll("#wcVocals button").forEach((b) => b.setAttribute("aria-pressed", String(b.dataset.v === W.vocals)));
  $("wcNoveltyV").textContent = W.novelty < 35 ? "больше любимого" : W.novelty > 65 ? "больше нового" : "поровну";
  $("wcPopV").textContent = W.popularity < 35 ? "андеграунд" : W.popularity > 65 ? "хиты" : "всё подряд";
}
let wcTimer;
function setWave(patch) {
  W = { ...W, ...patch }; store.set("echo.wave", W); renderWaveCfg();
  clearTimeout(wcTimer); wcTimer = setTimeout(retuneWave, 450);
}
$("wcToggle").onclick = () => { const p = $("waveCfg"); p.hidden = !p.hidden; $("wcToggle").setAttribute("aria-expanded", String(!p.hidden)); if (!p.hidden) renderWaveCfg(); };
$("wcNovelty").oninput = (e) => setWave({ novelty: +e.target.value });
$("wcPop").oninput = (e) => setWave({ popularity: +e.target.value });
$("wcDl").onchange = (e) => setWave({ onlyDl: e.target.checked });
$("wcTempo").onclick = (e) => { const b = e.target.closest("button"); if (b) setWave({ tempo: b.dataset.v }); };
$("wcVocals").onclick = (e) => { const b = e.target.closest("button"); if (b) setWave({ vocals: b.dataset.v }); };
$("wcReset").onclick = () => setWave({ ...R.WAVE_DEFAULTS });
$("vibeBtn").onclick = () => (pl.mode === "wave" && cur() ? toggle() : startWave());
$("moods").innerHTML = Object.keys(R.MOOD_PRESETS).map((m) => `<button class="mood" type="button" aria-pressed="false" data-m="${m}">${m}</button>`).join("");
$("moods").onclick = async (e) => {
  const b = e.target.closest(".mood"); if (!b) return;
  pl.mood = pl.mood === b.dataset.m ? null : b.dataset.m;
  document.querySelectorAll("#moods .mood").forEach((x) => x.setAttribute("aria-pressed", String(x.dataset.m === pl.mood)));
  $("wave-eyebrow").textContent = pl.mood ? `Моя волна · ${pl.mood}` : "Подобрано под вас";
  if (pl.mode === "wave" && cur()) await retuneWave();
  else { resetPool(); await startWave(); }
};

// ---------------- rendering lists ----------------
const lists = new Map(); // container id -> tracks
function rows(el, tracks, empty = "Пока пусто") {
  lists.set(el.id, tracks);
  if (!tracks.length) { el.innerHTML = `<div class="status">${esc(empty)}</div>`; return; }
  el.innerHTML = tracks.map((t, n) => `
    <div class="tr" role="listitem" tabindex="0" data-id="${esc(t.id)}" data-n="${n}">
      <span class="n"><span>${n + 1}</span><span class="eq"><i></i><i></i><i></i></span></span>
      <span class="c">${cv(t.coverSm, n)}</span>
      <span class="tt"><b>${esc(t.title)}</b><span>${esc(t.artist)}</span></span>
      <span class="al">${esc(t.genre || "")}<span class="q">${esc(t.quality)}</span></span>
      <span class="d">${fmt(t.duration)}</span>
      <button class="like" type="button" data-id="${esc(t.id)}" aria-label="Нравится" aria-pressed="${R.isLiked(t)}"><svg class="i"><use href="#i-heart"/></svg></button>
      <button class="dl" type="button" aria-label="Скачать" ${t.downloadable ? 'title="Скачать"' : 'disabled title="Автор не разрешил скачивание"'}><svg class="i"><use href="#i-dl"/></svg></button>
    </div>`).join("");
  sync();
}
function loading(el, n = 6) { el.innerHTML = Array.from({ length: n }, () => `<div class="tr"><span></span><span class="c skel"></span><span class="tt"><b class="skel" style="width:60%;height:14px"></b></span></div>`).join(""); }
document.addEventListener("click", (e) => {
  const row = e.target.closest(".tr[data-id]"); if (!row) return;
  const list = lists.get(row.parentElement.id) || [], t = list[+row.dataset.n]; if (!t) return;
  if (e.target.closest(".like")) return doLike(t);
  if (e.target.closest(".dl")) return openDlMenu(t, e.target.closest(".dl"));
  if (e.target.closest(".del")) return delUpload(t);
  if (cur()?.id === t.id) return toggle();
  playList(list, +row.dataset.n, "list");
});
document.addEventListener("keydown", (e) => { if (e.key === "Enter" && e.target.classList?.contains("tr")) e.target.click(); });

// ---------------- download menu ----------------
let menu;
function closeMenu() { menu?.remove(); menu = null; }
function openDlMenu(t, anchor) {
  closeMenu(); if (!t) return;
  const opts = downloadOptions(t);
  if (!opts.length) return toast("Автор этого трека не разрешил скачивание");
  menu = document.createElement("div"); menu.className = "menu"; menu.setAttribute("role", "menu");
  menu.innerHTML = opts.map((o, i) => `<button type="button" role="menuitem" data-i="${i}"><b>${esc(o.label)}</b><small>${esc(o.note)}</small></button>`).join("");
  document.body.appendChild(menu);
  const r = anchor.getBoundingClientRect(), mw = menu.offsetWidth, mh = menu.offsetHeight;
  menu.style.left = Math.max(8, Math.min(innerWidth - mw - 8, r.right - mw)) + "px";
  menu.style.top = (r.top - mh - 8 > 8 ? r.top - mh - 8 : r.bottom + 8) + "px";
  menu.onclick = async (e) => {
    const b = e.target.closest("button"); if (!b) return;
    const o = opts[+b.dataset.i]; closeMenu();
    const tt = toast(`Готовлю ${o.label}: ${t.title}`, { progress: true });
    try {
      const res = await download(t, o, (p) => tt.set(p));
      tt.text(res === "opened" ? "Открыл файл в новой вкладке" : `Скачано: ${o.label}`); tt.set(1); tt.close(2500);
    } catch (err) { tt.text(err.message === "too_big" ? "Файл слишком большой для WAV в браузере. Скачайте оригинал." : "Не получилось скачать. Попробуйте другой формат."); tt.close(5000); }
  };
  setTimeout(() => menu?.querySelector("button")?.focus(), 0);
}
document.addEventListener("pointerdown", (e) => { if (menu && !menu.contains(e.target) && !e.target.closest(".dl,#npDl")) closeMenu(); });
document.addEventListener("keydown", (e) => { if (e.key === "Escape") closeMenu(); });

// ---------------- home ----------------
// ---- Spotify-like home sections ----
const greeting = () => { const h = new Date().getHours(); return h < 5 ? "Доброй ночи" : h < 12 ? "Доброе утро" : h < 18 ? "Добрый день" : "Добрый вечер"; };
const homeLists = {};
function hcards(el, tracks, round = false) {
  homeLists[el.id] = tracks;
  el.innerHTML = tracks.map((t, i) => `<button class="hc${round ? " round" : ""}" type="button" data-i="${i}"><div class="cover">${cv(t.cover, i)}</div><b>${esc(round ? t.artist : t.title)}</b><span>${esc(round ? (t.genre || "артист") : t.artist)}</span></button>`).join("");
}
document.addEventListener("click", (e) => {
  const c = e.target.closest(".hc"); if (!c) return;
  const row = c.parentElement, list = homeLists[row.id] || [], i = +c.dataset.i;
  if (row.id === "favArtists") { const t = list[i]; if (t?.artistId?.startsWith("au:")) A.auArtist(t.artistId, 25).then((l) => playList(l, 0, "list")).catch(() => toast("Не удалось загрузить артиста")); return; }
  playList(list, i, "list");
});
function renderQuick() {
  $("greet").textContent = greeting();
  const tiles = [];
  const liked = Object.values(R.P.likes);
  if (liked.length) tiles.push({ t: "Любимое", cover: liked.find((x) => x.cover)?.cover, go: () => playList(liked.map(A.refresh).sort(() => Math.random() - 0.5), 0, "list") });
  tiles.push({ t: "Моя волна", cover: "", go: () => { scrollTo({ top: 0, behavior: "smooth" }); startWave(); } });
  for (const pl0 of R.P.playlists.slice(0, 2)) tiles.push({ t: pl0.title, cover: pl0.tracks.find((x) => x.cover)?.cover, go: () => playList(pl0.tracks.map(A.refresh), 0, "list") });
  const seen = new Set();
  for (const h of R.P.hist) { if (tiles.length >= 6) break; if (seen.has(h.id)) continue; seen.add(h.id); const tr = A.refresh({ ...h }); tiles.push({ t: tr.title, cover: tr.cover, go: () => playList([tr], 0, "list") }); }
  for (const m of ["Фокус", "Энергия", "Ночь", "Phonk"]) { if (tiles.length >= 6) break; tiles.push({ t: `Волна: ${m}`, cover: "", go: () => document.querySelector(`#moods [data-m="${m}"]`)?.click() }); }
  quickTiles = tiles.slice(0, 6);
  $("quick").innerHTML = quickTiles.map((q, i) => `<button class="qt" type="button" data-i="${i}"><span class="c">${cv(q.cover, i + 2)}</span><b>${esc(q.t)}</b></button>`).join("");
}
let quickTiles = [];
$("quick").onclick = (e) => { const b = e.target.closest(".qt"); if (b) quickTiles[+b.dataset.i]?.go(); };
async function renderHomeExtras() {
  renderQuick();
  const hist = R.P.hist.slice(0, 12).map((h) => A.refresh({ ...h }));
  $("recentSec").hidden = !hist.length; if (hist.length) hcards($("recent"), hist);
  // favourite artists: explicit favourites first, then the strongest artists in taste
  const ids = [...Object.keys(R.P.fav || {}), ...R.topArtists(8)].filter((v, i, arr) => arr.indexOf(v) === i && v.startsWith("au:")).slice(0, 10);
  $("artSec").hidden = !ids.length;
  if (ids.length) {
    const arts = (await Promise.all(ids.map((id) => A.auArtist(id, 3).then((l) => l[0]).catch(() => null)))).filter(Boolean);
    hcards($("favArtists"), arts, true);
  }
  A.api(`/api/charts/${store.get("echo.chart", "global")}`).then((c) => {
    $("cht-h").textContent = `Чарты · ${c.name}`;
    $("chTeaser").innerHTML = c.items.slice(0, 5).map((it) => `<a class="tr" href="#charts" style="text-decoration:none;color:inherit"><span class="n">${it.pos}</span><span class="c">${cv("", it.pos)}</span><span class="tt"><b>${esc(it.title)}</b><span>${esc(it.artist)}</span></span><span class="al">${it.streams ? it.streams.toLocaleString("ru-RU") + " прослушиваний" : ""}</span></a>`).join("");
  }).catch(() => { $("chTeaser").innerHTML = `<div class="status">Чарт сейчас недоступен</div>`; });
  A.auTrending({ time: "week", limit: 60 }).then((l) => {
    const fresh = l.filter((t) => t.releaseDate).sort((a, b) => Date.parse(b.releaseDate) - Date.parse(a.releaseDate)).slice(0, 14);
    hcards($("newRow"), fresh.length ? fresh : l.slice(0, 14));
  }).catch(() => {});
  A.localTracks().then((l) => { $("ownSec").hidden = !l.length; if (l.length) hcards($("ownRow"), l); });
  const tiles = [...Object.keys(R.MOOD_PRESETS).map((m) => ({ t: m, sub: "волна", mood: m })), ...R.topGenres(6).map((g) => ({ t: g, sub: "жанр", genre: g }))];
  for (const g of ["Lo-Fi", "House", "Hip-Hop/Rap", "Ambient", "Techno", "Drum & Bass"]) if (tiles.length < 16 && !tiles.some((x) => x.genre === g)) tiles.push({ t: g, sub: "жанр", genre: g });
  genreTiles = tiles;
  $("gTiles").innerHTML = tiles.map((x, i) => `<button class="gt" type="button" data-i="${i}">${esc(x.t)}<small>${esc(x.sub)}</small></button>`).join("");
}
let genreTiles = [];
$("gTiles").onclick = async (e) => {
  const b = e.target.closest(".gt"); if (!b) return;
  const x = genreTiles[+b.dataset.i];
  if (x.mood) { scrollTo({ top: 0, behavior: "smooth" }); if (pl.mood !== x.mood) document.querySelector(`#moods [data-m="${x.mood}"]`)?.click(); else startWave(); return; }
  try { const l = await A.auTrending({ genre: x.genre, limit: 40 }); playList(R.pick(l, 25, { cfg: W, explore: 1.5 }), 0, "list"); toast(`Играет: ${x.genre}`); }
  catch { toast("Жанр не загрузился"); }
};

async function renderHome() {
  renderHomeExtras();
  $("onb").hidden = R.hasTaste();
  $("onbGenres").innerHTML = R.STARTER_GENRES.map((g) => `<button class="mood" type="button" data-g="${esc(g)}" aria-pressed="${(R.P.g[g] || 0) > 0}">${esc(g)}</button>`).join("");
  loading($("trending"), 8);
  try { const tr = await A.auTrending({ time: "week", limit: 30 }); rows($("trending"), tr.slice(0, 20)); $("trNote").textContent = "Audius, полные версии"; }
  catch { $("trending").innerHTML = `<div class="status err">Каталог не ответил. <button type="button" id="retryTr">Повторить</button></div>`; $("retryTr").onclick = renderHome; }
  renderMixes();
}
$("onbGenres").onclick = (e) => {
  const b = e.target.closest(".mood"); if (!b) return;
  const on = b.getAttribute("aria-pressed") !== "true"; b.setAttribute("aria-pressed", String(on)); R.seedGenre(b.dataset.g, on); resetPool(); renderMixes();
};
const mixDefs = () => {
  const d = [];
  const [g1, g2] = R.topGenres(2);
  if (g1) d.push({ t: `В духе ${g1}`, s: "популярное за месяц", load: () => A.auTrending({ genre: g1, time: "month", limit: 30 }) });
  const a = R.topArtists(1)[0];
  if (a && a.startsWith("au:")) d.push({ t: `Похоже на ${R.P.an[a] || "любимого артиста"}`, s: "артист и его жанр", load: async () => { const own = await A.auArtist(a, 10); const g = own[0]?.genre; const more = g ? await A.auTrending({ genre: g, limit: 25 }) : []; return R.pick(A.dedupe([...own, ...more]), 25, {}, 4); } });
  if (g2) d.push({ t: `Немного ${g2}`, s: "за неделю", load: () => A.auTrending({ genre: g2, limit: 30 }) });
  d.push({ t: "Андеграунд", s: "новые имена, которые набирают", load: () => A.auUnderground(30) });
  d.push({ t: "Под учёбу", s: "без слов, спокойно", load: async () => R.pick(A.dedupe([...(await A.auTrending({ genre: "Lo-Fi", limit: 30 })), ...(await A.auSearch("study", { limit: 20 }))]), 25, { mood: "Фокус" }) });
  d.push({ t: "Хиты месяца", s: "что слушают все", load: () => A.auTrending({ time: "month", limit: 30 }) });
  return d.slice(0, 6);
};
let mixCache = [];
async function renderMixes() {
  const defs = mixDefs();
  $("cards").innerHTML = defs.map((m, i) => `<button class="card" type="button" data-i="${i}"><div class="cover skel" id="mix${i}"></div><b>${esc(m.t)}</b><span>${esc(m.s)}</span></button>`).join("");
  mixCache = defs.map(() => null);
  defs.forEach(async (m, i) => {
    try { const list = await m.load(); mixCache[i] = list; const c = $("mix" + i); if (c) { c.classList.remove("skel"); c.innerHTML = cv(list.find((t) => t.cover)?.cover, i); } }
    catch { const c = $("mix" + i); if (c) { c.classList.remove("skel"); c.innerHTML = cv("", i); } }
  });
}
$("cards").onclick = async (e) => {
  const c = e.target.closest(".card"); if (!c) return;
  const i = +c.dataset.i; let list = mixCache[i];
  if (!list) { try { list = await mixDefs()[i].load(); } catch { return toast("Подборка не загрузилась"); } }
  playList(list, 0, "list");
};

// ---------------- search ----------------
let srcFilter = "all", onlyDl = false, qTimer;
async function runSearch() {
  const q = $("q").value.trim();
  if (!q) { $("sr-h").textContent = "Поиск"; $("srNote").textContent = ""; rows($("results"), [], "Введите название трека, имя артиста или жанр"); return; }
  $("sr-h").textContent = `«${q}»`; loading($("results"));
  const { list, failed } = await A.searchAll(q, { src: srcFilter, onlyDl });
  if (failed) { $("results").innerHTML = `<div class="status err">Поиск не ответил. Проверьте интернет.</div>`; return; }
  $("srNote").textContent = `найдено: ${list.length}`;
  rows($("results"), list, onlyDl ? "Нет треков со скачиванием по этому запросу" : `По запросу «${q}» ничего нет. Попробуйте на английском или другой жанр.`);
}
$("q").addEventListener("input", () => { clearTimeout(qTimer); qTimer = setTimeout(runSearch, 350); });
$("fDl").onclick = (e) => { onlyDl = !onlyDl; e.currentTarget.setAttribute("aria-pressed", String(onlyDl)); runSearch(); };
document.querySelectorAll("[data-src]").forEach((b) => (b.onclick = () => {
  if (b.dataset.src === "jamendo" && !A.hasJamendo()) return toast("Jamendo не подключён: добавьте JAMENDO_CLIENT_ID на сервере");
  srcFilter = b.dataset.src; document.querySelectorAll("[data-src]").forEach((x) => x.setAttribute("aria-pressed", String(x === b))); runSearch();
}));

// ---------------- AI playlists ----------------
const AI_EXAMPLES = ["Под учёбу, без слов", "На пробежку", "Ночная дорога", "Перед сном", "Вечеринка дома", "Когда грустно"];
$("aiChips").innerHTML = AI_EXAMPLES.map((x) => `<button class="mood" type="button">${esc(x)}</button>`).join("");
$("aiChips").onclick = (e) => { const b = e.target.closest(".mood"); if (b) { $("aiQ").value = b.textContent; $("aiForm").requestSubmit(); } };
let aiResult = null;
$("aiForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const prompt = $("aiQ").value.trim(); if (!prompt) return $("aiQ").focus();
  const box = document.querySelector(".ai"); box.classList.add("busy"); $("aiGo").disabled = true; $("aiGo").textContent = "Собираю…";
  $("aiOut").hidden = false; $("aiT").textContent = "Думаю над подборкой…"; $("aiNote").textContent = ""; loading($("aiList"), 8);
  try {
    const { plan } = await A.api("/api/ai/playlist", { method: "POST", body: { prompt } });
    const jobs = plan.queries.map((q) => A.auSearch(q, { limit: 25 }));
    plan.genres.forEach((g) => jobs.push(A.auTrending({ genre: g, limit: 30 })));
    if (A.hasJamendo()) { plan.queries.slice(0, 2).forEach((q) => jobs.push(A.jaSearch(q, 20))); if (plan.genres.length) jobs.push(A.jaPopular({ tags: plan.genres.map(A.jTag).join("+"), instrumental: plan.instrumental })); }
    jobs.push(A.localTracks().then((l) => l.filter((t) => plan.genres.includes(t.genre) || plan.moods.includes(t.mood))));
    const res = await Promise.allSettled(jobs);
    const cands = A.dedupe(res.flatMap((r) => (r.status === "fulfilled" ? r.value : [])));
    const list = R.pick(cands, plan.size || 25, { plan, explore: 1.2 }, 2);
    aiResult = { title: plan.title, desc: plan.description, tracks: list };
    $("aiT").textContent = plan.title;
    const mins = Math.round(list.reduce((s, t) => s + (t.duration || 0), 0) / 60);
    $("aiNote").textContent = `${list.length} треков · ${mins} мин · ${plan.source === "rules" ? "подбор по правилам" : "подобрал ИИ"}`;
    rows($("aiList"), list, "Не нашлось подходящих треков. Переформулируйте запрос.");
  } catch (err) {
    $("aiT").textContent = err.status === 401 ? "Нужно войти" : "Не получилось собрать";
    $("aiList").innerHTML = err.status === 401
      ? `<div class="status err">ИИшка работает для вошедших. <a href="#login">Войти по почте</a></div>`
      : `<div class="status err">${esc(err.message)}. Попробуйте ещё раз.</div>`;
  }
  box.classList.remove("busy"); $("aiGo").disabled = false; $("aiGo").textContent = "Собрать";
});
$("aiPlay").onclick = () => aiResult && playList(aiResult.tracks, 0, "list");
$("aiSave").onclick = () => {
  if (!aiResult?.tracks.length) return;
  R.P.playlists = [{ id: Date.now().toString(36), title: aiResult.title, desc: aiResult.desc, tracks: aiResult.tracks.slice(0, 50).map(R.slim), at: Date.now() }, ...R.P.playlists].slice(0, 40);
  R.save(); toast(`Плейлист «${aiResult.title}» сохранён в коллекцию`);
};

// ---------------- charts (Spotify daily charts, matched against Audius) ----------------
let chCountry = store.get("echo.chart", "global"), chData = null;
const chMatch = new Map(); // "cc:pos" -> { list, kind }
const norm = (x) => (x || "").toLowerCase().replace(/\(.*?\)|\[.*?\]/g, " ").replace(/feat\.?.*$/i, " ").replace(/[^a-z0-9а-яёäöüéèáíóúñç ]/gi, " ").replace(/\s+/g, " ").trim();
async function matchChart(it) {
  const key = `${chCountry}:${it.pos}`;
  if (chMatch.has(key)) return chMatch.get(key);
  let list = [];
  try { list = await A.auSearch(`${it.artist} ${it.title}`, { limit: 15 }); } catch {}
  const nt = norm(it.title), na = norm(it.artist);
  const scored = list.map((t) => {
    const tt = norm(t.title), ta = norm(t.artist);
    let sc = 0;
    if (nt && tt.includes(nt)) sc += 4;
    if (ta === na) sc += 5; else if (na && (tt.includes(na) || ta.includes(na))) sc += 2;
    return { t, sc, orig: ta === na && nt && tt.includes(nt) };
  }).filter((x) => x.sc >= 4).sort((a, b) => b.orig - a.orig || b.sc - a.sc);
  const res = { list: scored.map((x) => x.t), kind: !scored.length ? "none" : scored[0].orig ? "orig" : "ver" };
  chMatch.set(key, res);
  return res;
}
const KIND = { orig: "Оригинал", ver: "Версия", none: "Нет на Audius" };
function markRow(it, res) {
  const b = document.querySelector(`.crow[data-pos="${it.pos}"] .find`);
  if (!b) return;
  b.className = "find " + res.kind; b.textContent = KIND[res.kind];
}
async function renderCharts() {
  const cs = await A.api("/api/charts").catch(() => ({ charts: [] }));
  $("chCountries").innerHTML = cs.charts.map((c) => `<button class="mood" type="button" data-c="${esc(c.id)}" aria-pressed="${c.id === chCountry}">${esc(c.name)}</button>`).join("");
  $("chList").innerHTML = Array.from({ length: 8 }, () => `<div class="crow"><div class="crow-in"><span></span><span></span><span class="tt"><b class="skel" style="width:50%;height:14px"></b></span></div></div>`).join("");
  $("chNote").textContent = ""; $("chProg").textContent = "";
  try {
    chData = await A.api(`/api/charts/${chCountry}`);
  } catch (e) { $("chList").innerHTML = `<div class="status err">${esc(e.message)}</div>`; return; }
  $("chNote").textContent = `${chData.name} · ${chData.date} · топ ${chData.items.length}`;
  $("chList").innerHTML = chData.items.map((it) => {
    const res = chMatch.get(`${chCountry}:${it.pos}`);
    return `<div class="crow" role="listitem" data-pos="${it.pos}"><div class="crow-in">
      <span class="pos">${it.pos}</span><span class="chg">${esc(it.change)}</span>
      <span class="tt"><b>${esc(it.title)}</b><span>${esc([it.artist, ...it.with].join(", "))}</span></span>
      <span class="st">${it.streams ? it.streams.toLocaleString("ru-RU") : ""}</span>
      <button class="find ${res ? res.kind : ""}" type="button">${res ? KIND[res.kind] : "Найти на Audius"}</button>
      <a class="ext" href="https://open.spotify.com/track/${encodeURIComponent(it.spotify)}" target="_blank" rel="noopener" aria-label="Открыть в Spotify" title="Открыть в Spotify"><svg class="i"><use href="#i-ext"/></svg></a>
    </div><div class="matches" hidden></div></div>`;
  }).join("");
  // check the top of the chart in the background, a few at a time
  const top = chData.items.slice(0, 20), cc = chCountry;
  for (let i = 0; i < top.length; i += 4) {
    if (cc !== chCountry || $("v-charts").hidden) return;
    await Promise.all(top.slice(i, i + 4).map(async (it) => markRow(it, await matchChart(it))));
  }
}
$("chCountries").onclick = (e) => { const b = e.target.closest(".mood"); if (!b) return; chCountry = b.dataset.c; store.set("echo.chart", chCountry); renderCharts(); };
$("chList").addEventListener("click", async (e) => {
  const b = e.target.closest(".find"); if (!b || !chData) return;
  const row = b.closest(".crow"), it = chData.items.find((x) => x.pos === +row.dataset.pos), box = row.querySelector(".matches");
  if (!box.hidden) { box.hidden = true; return; }
  box.hidden = false; box.innerHTML = `<div class="tracks" id="chm-${it.pos}" role="list"></div>`;
  loading($(`chm-${it.pos}`), 3);
  const res = await matchChart(it); markRow(it, res);
  rows($(`chm-${it.pos}`), res.list.slice(0, 6), "На Audius этого трека нет. Оригинал есть только в Spotify.");
});
$("chPlay").onclick = async () => {
  if (!chData) return;
  const btn = $("chPlay"); btn.disabled = true;
  const queue = [], items = chData.items.slice(0, 50);
  let started = false, done = 0;
  for (let i = 0; i < items.length; i += 5) {
    const got = await Promise.all(items.slice(i, i + 5).map(async (it) => { const r = await matchChart(it); markRow(it, r); return r.list[0]; }));
    done += got.length;
    for (const t of got) if (t && !queue.some((q) => q.id === t.id)) queue.push(t);
    $("chProg").textContent = `проверено ${done} из ${items.length}, нашлось ${queue.length}`;
    if (!started && queue.length >= 3) { started = true; playList(queue.slice(), 0, "list"); }
    else if (started) { const known = new Set(pl.queue.map((q) => q.id)); pl.queue.push(...queue.filter((q) => !known.has(q.id))); }
  }
  if (!started && queue.length) playList(queue, 0, "list");
  if (!queue.length) toast("Из этого чарта на Audius ничего не нашлось");
  btn.disabled = false;
};

// ---------------- library ----------------
function renderLibrary() {
  const liked = Object.values(R.P.likes).map(A.refresh).sort((a, b) => b.likedAt - a.likedAt);
  $("lkNote").textContent = liked.length ? `${liked.length} треков` : "";
  rows($("liked"), liked, "Нажмите на сердечко у трека, и он появится здесь");
  rows($("history"), R.P.hist.slice(0, 40).map(A.refresh), "История появится после первых прослушиваний");
  $("playlists").innerHTML = R.P.playlists.length
    ? R.P.playlists.map((p, i) => `<button class="card" type="button" data-i="${i}"><div class="cover">${cv(p.tracks.find((t) => t.cover)?.cover, i)}</div><b>${esc(p.title)}</b><span>${p.tracks.length} треков</span></button>`).join("")
    : `<div class="status">Попросите ИИшку в поиске собрать плейлист и сохраните его</div>`;
}
$("playlists").onclick = (e) => { const c = e.target.closest(".card"); if (c) playList(R.P.playlists[+c.dataset.i].tracks, 0, "list"); };

// ---------------- upload (admin) ----------------
$("genreList").innerHTML = GENRE_LIST.map((g) => `<option value="${esc(g)}">`).join("");
$("moodList").innerHTML = MOOD_LIST.map((g) => `<option value="${esc(g)}">`).join("");
let upFile = null;
function pickFile(f) {
  if (!f) return; upFile = f; $("dropT").textContent = `${f.name} · ${(f.size / 1048576).toFixed(1)} МБ`;
  if (!$("upTitle").value) {
    const base = f.name.replace(/\.[^.]+$/, ""); const m = base.split(" - ");
    if (m.length > 1) { $("upArtist").value ||= m[0].trim(); $("upTitle").value = m.slice(1).join(" - ").trim(); } else $("upTitle").value = base;
  }
}
$("upFile").onchange = (e) => pickFile(e.target.files[0]);
const drop = $("drop");
["dragenter", "dragover"].forEach((ev) => drop.addEventListener(ev, (e) => { e.preventDefault(); drop.classList.add("over"); }));
["dragleave", "drop"].forEach((ev) => drop.addEventListener(ev, (e) => { e.preventDefault(); drop.classList.remove("over"); }));
drop.addEventListener("drop", (e) => pickFile(e.dataTransfer.files[0]));
function durationOf(file) {
  return new Promise((res) => { const a = new Audio(); a.preload = "metadata"; a.src = URL.createObjectURL(file); a.onloadedmetadata = () => { res(a.duration); URL.revokeObjectURL(a.src); }; a.onerror = () => res(0); });
}
function xhrUpload(url, body, type, onProgress) {
  return new Promise((res, rej) => {
    const x = new XMLHttpRequest(); x.open("POST", url); x.setRequestHeader("X-Echo", "1"); x.setRequestHeader("Content-Type", type || "application/octet-stream");
    x.upload.onprogress = (e) => e.lengthComputable && onProgress(e.loaded / e.total);
    x.onload = () => { let d = {}; try { d = JSON.parse(x.responseText); } catch {} x.status < 300 ? res(d) : rej(new Error(d.error || "Ошибка загрузки")); };
    x.onerror = () => rej(new Error("Сеть недоступна")); x.send(body);
  });
}
$("upForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  if (!upFile) return ($("upNote").textContent = "Выберите аудиофайл");
  const note = $("upNote"); $("upGo").disabled = true;
  try {
    const dur = await durationOf(upFile);
    const q = new URLSearchParams({ title: $("upTitle").value, artist: $("upArtist").value, genre: $("upGenre").value, mood: $("upMood").value, bpm: $("upBpm").value, filename: upFile.name, duration: dur ? dur.toFixed(1) : "" });
    const { id } = await xhrUpload(`/api/uploads?${q}`, upFile, upFile.type, (p) => (note.textContent = `Загружаю… ${Math.round(p * 100)}%`));
    const cover = $("upCover").files[0];
    let coverFailed = false;
    if (cover) {
      try { await xhrUpload(`/api/uploads/${id}/cover`, cover, cover.type, () => (note.textContent = "Загружаю обложку…")); }
      catch { coverFailed = true; }
    }
    note.textContent = coverFailed ? "Трек загружен, а обложка нет. Попробуйте файл JPG или PNG до 8 МБ." : "Готово";
    toast("Трек загружен и уже доступен в поиске и волне");
    $("upForm").reset(); upFile = null; $("dropT").textContent = "Перетащите аудиофайл или нажмите"; resetPool();
    renderUploads();
  } catch (err) { note.textContent = err.message; }
  $("upGo").disabled = false;
});
async function renderUploads() {
  const l = await A.localTracks();
  rows($("upList"), l, "Вы ещё ничего не загрузили");
  document.querySelectorAll("#upList .tr").forEach((r) => r.insertAdjacentHTML("beforeend", ""));
  document.querySelectorAll("#upList .dl").forEach((b) => { b.classList.add("del"); b.classList.remove("dl"); b.disabled = false; b.title = "Удалить"; b.setAttribute("aria-label", "Удалить трек"); b.innerHTML = '<svg class="i"><use href="#i-ban"/></svg>'; });
}
let delArm = null;
async function delUpload(t) {
  if (delArm !== t.id) { delArm = t.id; toast(`Нажмите ещё раз, чтобы удалить «${t.title}»`); setTimeout(() => (delArm = null), 4000); return; }
  delArm = null;
  try { await A.api(`/api/uploads/${t.rawId}`, { method: "DELETE" }); toast("Трек удалён"); renderUploads(); resetPool(); } catch (err) { toast(err.message); }
}

// ---------------- taste setup (after sign-up, and from settings) ----------------
function renderWelcome() {
  $("wlGenres").innerHTML = R.STARTER_GENRES.map((g) => `<button class="mood" type="button" data-g="${esc(g)}" aria-pressed="${(R.P.g[g] || 0) > 0}">${esc(g)}</button>`).join("");
  const favs = Object.entries(R.P.fav || {});
  $("wlArtists").innerHTML = favs.map(([id, n]) => `<span class="chip-x">${esc(n)}<button type="button" data-id="${esc(id)}" aria-label="Убрать ${esc(n)}">×</button></span>`).join("");
  const liked = Object.keys(R.P.likes).length;
  $("wlSummary").textContent = `Жанров: ${R.topGenres(20).length} · артистов: ${favs.length} · песен в любимом: ${liked}`;
  if (!$("wlSong").value) rows($("wlSongs"), Object.values(R.P.likes).map(A.refresh).slice(0, 8), "Найдите песню, которую любите, и нажмите на сердце");
}
$("wlGenres").onclick = (e) => {
  const b = e.target.closest(".mood"); if (!b) return;
  const on = b.getAttribute("aria-pressed") !== "true"; b.setAttribute("aria-pressed", String(on)); R.seedGenre(b.dataset.g, on); resetPool(); renderWelcome();
};
$("wlArtistForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const names = $("wlArtist").value.split(",").map((x) => x.trim()).filter(Boolean).slice(0, 5);
  if (!names.length) return $("wlArtist").focus();
  $("wlArtistNote").textContent = "Ищу…";
  const missed = [];
  await Promise.all(names.map(async (name) => {
    try {
      const list = await A.auSearch(name, { limit: 30 });
      const low = name.toLowerCase();
      const hit = list.find((t) => t.artist.toLowerCase() === low) || list.find((t) => t.artist.toLowerCase().includes(low));
      if (hit) R.addFavArtist(hit.artistId, hit.artist, hit.genre); else missed.push(name);
    } catch { missed.push(name); }
  }));
  $("wlArtist").value = "";
  $("wlArtistNote").textContent = missed.length ? `Не нашёл в каталоге: ${missed.join(", ")}. Похоже, их музыки нет на Audius.` : "Добавлено";
  resetPool(); renderWelcome();
});
$("wlArtists").onclick = (e) => { const b = e.target.closest("button[data-id]"); if (b) { R.removeFavArtist(b.dataset.id); resetPool(); renderWelcome(); } };
let wlTimer;
$("wlSong").addEventListener("input", () => {
  clearTimeout(wlTimer);
  wlTimer = setTimeout(async () => {
    const q = $("wlSong").value.trim();
    if (!q) return renderWelcome();
    loading($("wlSongs"), 4);
    const { list, failed } = await A.searchAll(q);
    if (failed) { $("wlSongs").innerHTML = `<div class="status err">Поиск не ответил. Попробуйте ещё раз.</div>`; return; }
    rows($("wlSongs"), list.slice(0, 10), `«${q}» не нашлось. Попробуйте написать по-английски или только исполнителя.`);
  }, 350);
});

// ---------------- prefs ----------------
function renderPrefs() {
  $("themes").innerHTML = THEMES.map(([n, bg, fg], i) => `<button class="theme" type="button" data-v="${i + 1}" aria-pressed="${+root.dataset.v === i + 1}">
    <span class="sw" style="background:${bg}"><i style="background:${fg}"></i><i style="background:${fg};opacity:.4"></i></span><b>${i + 1}. ${n}</b><span>${i === 9 ? "по умолчанию" : "&nbsp;"}</span></button>`).join("");
  const bars = (title, map, n) => {
    const b = R.tasteBars(map, n); const max = b[0]?.[1] || 1;
    const label = (k) => (map === "a" ? R.P.an[k] || "артист" : k);
    return `<div><h3>${title}</h3><div class="bars">${b.length ? b.map(([k, v]) => `<div><span>${esc(label(k))}</span><i style="width:${Math.max(6, (v / max) * 100)}%"></i><em>${v.toFixed(1)}</em></div>`).join("") : '<span class="src">Пока пусто: послушайте и полайкайте пару треков</span>'}</div></div>`;
  };
  $("taste").innerHTML = bars("Жанры", "g", 6) + bars("Настроения", "m", 6) + bars("Артисты", "a", 6);
  $("prefGenres").innerHTML = R.STARTER_GENRES.map((g) => `<button class="mood" type="button" data-g="${esc(g)}" aria-pressed="${(R.P.g[g] || 0) > 0}">${esc(g)}</button>`).join("");
  $("srcs").innerHTML = `
    <div><b>Audius</b><span>Подключён. Полные треки, MP3 320 kbps. Скачивание там, где автор разрешил.</span></div>
    <div><b>Jamendo</b><span>${A.hasJamendo() ? "Подключён. Скачивание FLAC без потерь." : "Не подключён. Нужен бесплатный Client ID в настройках сервера."}</span></div>
    <div><b>Ваша музыка</b><span>Файлы, которые загружает администратор. Хранятся без перекодирования.</span></div>`;
}
$("themes").onclick = (e) => { const b = e.target.closest(".theme"); if (b) setTheme(+b.dataset.v); };
$("prefGenres").onclick = (e) => { const b = e.target.closest(".mood"); if (!b) return; const on = b.getAttribute("aria-pressed") !== "true"; b.setAttribute("aria-pressed", String(on)); R.seedGenre(b.dataset.g, on); resetPool(); renderPrefs(); };
$("resetTaste").onclick = (e) => {
  const b = e.currentTarget;
  if (b.dataset.arm !== "1") { b.dataset.arm = "1"; b.textContent = "Точно сбросить?"; setTimeout(() => { b.dataset.arm = ""; b.textContent = "Сбросить вкус"; }, 4000); return; }
  R.resetProfile(); resetPool(); b.dataset.arm = ""; b.textContent = "Сбросить вкус"; $("resetNote").textContent = "Вкус сброшен. Любимое и плейлисты остались."; renderPrefs();
};

// ---------------- router ----------------
const VIEWS = ["home", "search", "charts", "library", "login", "upload", "prefs", "welcome"];
function route() {
  let v = location.hash.slice(1) || "home";
  if (!VIEWS.includes(v)) v = "home";
  if (v === "upload" && me?.role !== "admin") v = me ? "home" : "login";
  VIEWS.forEach((x) => ($("v-" + x).hidden = x !== v));
  document.querySelectorAll(".nav a").forEach((a) => a.classList.toggle("on", a.dataset.view === v));
  if (v === "home") renderHome();
  if (v === "search") runSearch();
  if (v === "library") renderLibrary();
  if (v === "prefs") renderPrefs();
  if (v === "upload") renderUploads();
  if (v === "welcome") renderWelcome();
  if (v === "charts") renderCharts();
  if (v === "login" && me) location.hash = "#home";
  scrollTo(0, 0);
}
addEventListener("hashchange", route);

// ---------------- boot ----------------
setTheme(+(store.get("echo.theme", "10")) || 10);
if (!(await magicLogin())) await refreshMe();
route();
sync();
