import * as A from "./api.js?v=24";
import * as R from "./recs.js?v=24";
import { createFx } from "./fx.js?v=24";
import { downloadOptions, download } from "./download.js?v=24";
import * as E from "./engine.js?v=24";

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
    me = d.user; A.setJamendo(d.jamendo); A.setYouTube(d.youtube); A.setSpotify(d.spotify);
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
// ---- YouTube official IFrame player: used only for src "youtube" tracks ----
const yt = { player: null, ready: null, playing: false, time: 0, dur: 0 };
function ytLoad() {
  if (yt.ready) return yt.ready;
  yt.ready = new Promise((resolve, reject) => {
    window.onYouTubeIframeAPIReady = () => {
      yt.player = new YT.Player("ytplayer", {
        host: "https://www.youtube-nocookie.com", width: "100%", height: "100%",
        playerVars: { playsinline: 1, rel: 0, modestbranding: 1 },
        events: {
          onReady: () => resolve(yt.player),
          onStateChange: (e) => {
            yt.playing = e.data === YT.PlayerState.PLAYING;
            if (yt.playing) ytFails = 0;
            if (e.data === YT.PlayerState.PLAYING) errRun = 0;
            if (e.data === YT.PlayerState.ENDED) { const t = cur(); if (t) R.finished(t); next("end"); }
            sync();
          },
          onError: (e) => {
            const t = cur();
            if (t?.src !== "youtube") return;
            if (e.data === 101 || e.data === 150 || e.data === 100) A.reportBadYouTube(t.rawId);
            ytFail(true);
          },
        },
      });
    };
    const sc = document.createElement("script");
    sc.src = "https://www.youtube.com/iframe_api"; sc.onerror = () => reject(new Error("YouTube недоступен"));
    document.head.appendChild(sc);
  });
  return yt.ready;
}
setInterval(() => {
  if (!yt.player?.getCurrentTime || cur()?.src !== "youtube") return;
  yt.time = yt.player.getCurrentTime() || 0; yt.dur = yt.player.getDuration() || 0;
  audio.dispatchEvent(new Event("timeupdate"));
}, 500);
const isYT = () => cur()?.src === "youtube";
let ytFails = 0;
function ytFail(videoOnly = false, why = "") {
  ytFails++;
  const t = cur();
  if (!videoOnly || ytFails >= 3) {
    if (!ytBroken) toast(why === "stall" ? "YouTube не запускается: похоже, он замедлен или заблокирован в вашей сети. Дальше играю полные версии из других источников."
      : "YouTube сейчас не открывается (в России он часто замедлен). Дальше играю полные версии из других источников.");
    ytBroken = true; resetPool();
  } else toast("Это видео YouTube не даёт встроить, ищу другую версию");
  if (yt.player?.stopVideo) try { yt.player.stopVideo(); } catch {}
  // the same song from Audius, Jamendo or the uploads, if there is one; otherwise the next track
  const run = ++resolveRun;
  altVersion(t).then((alt) => {
    if (run !== resolveRun || cur() !== t) return;
    if (alt) { pl.queue[pl.i] = alt; load(true); } else next("error");
  }).catch(() => next("error"));
}
// A full version of the same song that does not need YouTube (uploads → Audius → Jamendo).
async function altVersion(t) {
  if (!t) return null;
  const artist = (t.artist || "").split(",")[0], na = nkey(artist), nt = nkey(t.title);
  if (!nt) return null;
  const keep = { title: t.title, artist: t.artist, extLink: t.extLink || t.link, extName: t.extName || "YouTube", via: t.via, reason: t.reason };
  try {
    const up = (await A.localTracks()).find((u) => nkey(u.title) === nt && (!na || nkey(u.artist).includes(na)));
    if (up) return { ...up, ...keep, title: up.title, artist: up.artist };
  } catch {}
  let best = null, bestS = 0;
  const consider = (list) => {
    for (const c of list) {
      const tt = nkey(c.title), ta = nkey(c.artist);
      if (!tt.includes(nt) && !nt.includes(tt)) continue;
      const orig = na && ta === na;
      const sc = (orig ? 10 : 0) + (na && (tt.includes(na) || ta.includes(na)) ? 3 : 0) + (tt === nt ? 2 : 0)
        + (t.duration && c.duration ? Math.max(0, 3 - Math.abs(t.duration - c.duration) / 10) : 0);
      if (sc > bestS) { bestS = sc; best = c; }
    }
  };
  try { consider(await A.auSearch(`${artist} ${t.title}`.trim(), { limit: 15 })); } catch {}
  if (bestS < 3 && A.hasJamendo()) { try { consider(await A.jaSearch(`${artist} ${t.title}`.trim(), 10)); } catch {} }
  if (!best || bestS < 3) return null;
  const orig = na && (nkey(best.artist) === na || nkey(best.artist).includes(na));
  if (!orig || /remix|cover|кавер|sped|slowed|nightcore|karaoke|instrumental/i.test(best.title)) return null; // a remix or cover is not the same song
  return { ...best, reason: t.reason, extLink: keep.extLink, extName: keep.extName };
}
// Apple Music / Spotify result → a playable full version, cached per track.
const resolved = new Map();
async function resolveCatalog(t) {
  if (resolved.has(t.id)) return resolved.get(t.id);
  const job = (async () => {
    let m = null;
    if (!ytBroken) { try { m = await matchImported({ artist: t.artist, title: t.title, duration: t.duration }, await A.localTracks()); } catch {} }
    let r = m?.t || null;
    if (m?.kind === "version" && A.hasYouTube() && !ytBroken) {
      try { const vids = await A.ytSearch(`${(t.artist || "").split(",")[0]} ${t.title}`); if (vids[0]) r = A.fromYouTube(vids[0], { title: t.title, artist: t.artist }); } catch {}
    }
    if (!r || (r.src === "youtube" && ytBroken)) r = await altVersion(t);
    if (!r) return null;
    const out = { ...r, extLink: t.link, extName: t.extName, via: t.src };
    if (r.src === "youtube") Object.assign(out, { title: t.title, artist: t.artist, cover: t.cover || r.cover, coverSm: t.coverSm || r.coverSm, duration: t.duration || r.duration });
    else if (m?.kind !== "orig" && r.src !== "local") out.reason = "версия трека";
    return out;
  })();
  resolved.set(t.id, job);
  const v = await job;
  if (!v) resolved.delete(t.id);
  return v;
}
const eng = {
  playing: () => (isYT() ? yt.playing : !!cur() && !audio.paused),
  time: () => (isYT() ? yt.time : audio.currentTime),
  dur: () => (isYT() ? yt.dur : audio.duration),
  seek: (s) => { if (isYT()) yt.player?.seekTo(s, true); else audio.currentTime = s; },
  play: () => (isYT() ? yt.player?.playVideo() : audio.play().catch(() => {})),
  pause: () => (isYT() ? yt.player?.pauseVideo() : audio.pause()),
};
let waitingNext = false;
const cur = () => pl.queue[pl.i];

function playList(list, start = 0, mode = "list") {
  if (!list.length) return toast("Здесь пока нечего играть");
  pl.queue = list.map(A.refresh); pl.i = start; pl.mode = mode; load(true);
}
let resolveRun = 0;
function load(autoplay) {
  const t = cur(); if (!t) return;
  if (t.catalog) {
    const run = ++resolveRun;
    $("npT").innerHTML = `${esc(t.title)}<span class="q">ищу полную версию…</span>`; $("npA").textContent = t.artist; $("npC").innerHTML = cv(t.coverSm, 1);
    resolveCatalog(t).then((r) => {
      if (run !== resolveRun || cur() !== t) return;
      if (r) { pl.queue[pl.i] = r; load(autoplay); return; }
      toast(`Полной версии «${t.title}» не нашлось, включаю следующий`);
      pl.queue.splice(pl.i, 1);
      if (pl.i < pl.queue.length) load(autoplay); else { eng.pause(); sync(); }
    });
    return;
  }
  pl.counted = false;
  R.started(t);
  if (t.src === "youtube") {
    audio.pause(); audio.removeAttribute("src");
    $("ytbox").hidden = false; $("ytChannel").textContent = t.channel || t.artist; $("ytLink").href = t.link;
    $("ytSpot").href = t.extLink || A.spotifySearchUrl(t); $("ytSpot").textContent = t.extName || "Spotify";
    const run = ++resolveRun;
    ytLoad().then((p) => {
      p.setVolume(Math.round(audio.volume * 100)); autoplay ? p.loadVideoById(t.rawId) : p.cueVideoById(t.rawId);
      // YouTube is often throttled (Russia and other regions): if the video does not start, play another full version
      if (autoplay) setTimeout(() => { if (run === resolveRun && cur() === t && !yt.playing && !(yt.time > 0)) ytFail(false, "stall"); }, 12000);
    }).catch(() => { ytFail(); });
  } else {
    if (yt.player?.stopVideo) yt.player.stopVideo();
    $("ytbox").hidden = true; yt.playing = false;
    audio.src = t.stream;
    if (autoplay) audio.play().catch(() => {});
  }
  sync();
  if ("mediaSession" in navigator) {
    navigator.mediaSession.metadata = new MediaMetadata({ title: t.title, artist: t.artist, album: t.genre || "Эхо", artwork: t.cover ? [{ src: t.cover, sizes: "480x480" }] : [] });
  }
  if (pl.mode === "wave" && pl.queue.length - pl.i < 4) refillWave();
  if (typeof fsTrack === "function") setTimeout(fsTrack, 0);
  // warm the next track on the server so skipping starts instantly
  const nx = pl.queue[pl.i + 1];
  if (nx?.catalog) setTimeout(() => resolveCatalog(nx).catch(() => {}), 2500);
  if (nx?.src === "audius") setTimeout(() => fetch(A.refresh(nx).stream, { headers: { Range: "bytes=0-1" } }).catch(() => {}), 1500);
}
function next(reason = "skip") {
  const t = cur();
  if (t && reason === "skip" && eng.time() < 30) R.skipped(t, eng.time());
  if (pl.i < pl.queue.length - 1) { pl.i++; load(true); }
  else if (pl.mode === "wave") { if (waitingNext) return; waitingNext = true; refillWave().then(() => { if (pl.i < pl.queue.length - 1) { pl.i++; load(true); } }).finally(() => { waitingNext = false; }); }
  else { eng.pause(); sync(); }
}
function prev() { if (eng.time() > 4 || pl.i <= 0) { eng.seek(0); } else { pl.i--; load(true); } }
function toggle() {
  if (!cur()) return startWave();
  if (eng.playing()) return eng.pause();
  eng.play();
}

audio.addEventListener("play", sync); audio.addEventListener("pause", sync);
audio.addEventListener("timeupdate", () => {
  const t = cur(); if (!t) return;
  const d = eng.dur() || t.duration || 0, p = d ? (eng.time() / d) * 100 : 0;
  $("fill").style.width = p + "%"; $("mini").style.width = p + "%"; $("cur").textContent = fmt(eng.time()); $("dur").textContent = fmt(d);
  $("seek").setAttribute("aria-valuenow", Math.round(p));
  if (!pl.counted && eng.time() > 30) { pl.counted = true; R.listened(t); }
});
audio.addEventListener("ended", () => { const t = cur(); if (t) R.finished(t); next("end"); });
let errRun = 0;
audio.addEventListener("playing", () => { errRun = 0; });
audio.addEventListener("error", () => {
  if (!cur() || !audio.getAttribute("src")) return;
  errRun++;
  if (errRun >= 4) { errRun = 0; eng.pause(); sync(); return toast("Несколько треков подряд не загрузились. Проверьте соединение и нажмите плей ещё раз."); }
  toast("Трек не загрузился, включаю следующий"); setTimeout(() => next("error"), 600);
});
$("play").onclick = toggle; $("next").onclick = () => next(); $("prev").onclick = prev;
$("shuf").onclick = (e) => {
  const on = e.currentTarget.getAttribute("aria-pressed") !== "true"; e.currentTarget.setAttribute("aria-pressed", String(on));
  if (on && pl.queue.length > pl.i + 2) { const rest = pl.queue.splice(pl.i + 1); rest.sort(() => Math.random() - 0.5); pl.queue.push(...rest); }
};
$("seek").onclick = (e) => { const r = e.currentTarget.getBoundingClientRect(); if (eng.dur()) eng.seek(((e.clientX - r.left) / r.width) * eng.dur()); };
$("seek").onkeydown = (e) => { if (e.key === "ArrowRight") eng.seek(eng.time() + 5); if (e.key === "ArrowLeft") eng.seek(Math.max(0, eng.time() - 5)); };
$("vol").oninput = (e) => { audio.volume = e.target.value / 100; yt.player?.setVolume?.(+e.target.value); store.set("echo.vol", +e.target.value); };
const doLike = (t) => { if (!t) return; const on = R.like(t); toast(on ? "Добавлено в любимое" : "Убрано из любимого"); sync(); if (!$("v-welcome").hidden) $("wlSummary").textContent = `Песен в любимом: ${Object.keys(R.P.likes).length}`; };
$("npLike").onclick = () => doLike(cur()); $("wLike").onclick = () => doLike(cur());
$("wBan").onclick = () => { const t = cur(); if (!t) return; R.ban(t); toast("Больше не покажу похожее так часто"); next("ban"); };
$("npDl").onclick = (e) => openDlMenu(cur(), e.currentTarget);
if ("mediaSession" in navigator) {
  navigator.mediaSession.setActionHandler("play", () => eng.play());
  navigator.mediaSession.setActionHandler("pause", () => eng.pause());
  navigator.mediaSession.setActionHandler("nexttrack", () => next());
  navigator.mediaSession.setActionHandler("previoustrack", prev);
}
document.addEventListener("keydown", (e) => {
  if (e.target.matches("input,textarea") || e.target.closest("button,[role=slider],.tr")) return;
  if (e.code === "Space") { e.preventDefault(); toggle(); }
  if (e.key === "/") { e.preventDefault(); location.hash = "#search"; setTimeout(() => $("q").focus(), 0); }
});

function sync() {
  const t = cur(), playing = !!t && eng.playing();
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
  $("vibeL").textContent = waveOn ? (playing ? (t.reason ? `Сейчас играет · ${t.reason}` : "Сейчас играет") : "На паузе") : "Моя волна";
  $("vibeT").textContent = waveOn ? t.title : "Нажмите, чтобы начать";
  $("vibeA").textContent = waveOn ? `${t.artist}${t.src === "youtube" ? " · источник: YouTube" : t.genre ? " · " + t.genre : ""}` : (pl.mood ? `Настроение: ${pl.mood}` : "Подстроится под ваш вкус");
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
const waveCtx = (extra = {}) => ({ mood: pl.mood, cfg: waveCfg(), ...extra });

// candidate sources live in engine.js («Моя волна 3»): ~15 randomised sources incl. YouTube hits
let ytBroken = false;
const waveCfg = () => ({ ...W, yt: W.yt !== false && !ytBroken });
function poolJobs(mood) { return E.sources(mood, waveCfg()); }
// Resolves `first` as soon as there is enough to start playing; `all` when every source has answered.
function ensurePool(mood, quick = false) {
  const key = (mood || "") + "|" + W.novelty;
  if (poolKey === key && poolAt && Date.now() - poolAt < 10 * 60e3) return Promise.resolve(pool);
  if (!poolRun || poolRun.key !== key) {
    pool = []; poolAt = 0; poolKey = key;
    let release; const first = new Promise((r) => (release = r));
    const run = { key };
    const add = (list) => { if (poolRun !== run) return; pool = A.dedupe([...pool, ...list]); if (pool.length >= 60 || E.pick(pool, 6, waveCtx({ explore: 0, markServed: false })).length >= 6) release(pool); };
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
    const add = E.pick(p.filter((t) => !inQ.has(t.id)), 8, waveCtx({ prev: pl.queue.at(-1), recent: pl.queue.slice(-8) }));
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
    const q = E.pick(p, 6, waveCtx({ prev: cur(), recent: pl.queue.slice(Math.max(0, pl.i - 7), pl.i + 1) }));
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
  const srcOn = { yt: W.yt !== false, sp: W.sp !== false, ap: W.ap !== false, indie: W.indie === true };
  document.querySelectorAll("#wcSrc [data-k]").forEach((b) => b.setAttribute("aria-pressed", String(srcOn[b.dataset.k])));
  document.querySelectorAll("#wcTempo button").forEach((b) => b.setAttribute("aria-pressed", String(b.dataset.v === W.tempo)));
  document.querySelectorAll("#wcVocals button").forEach((b) => b.setAttribute("aria-pressed", String(b.dataset.v === W.vocals)));
  $("wcNoveltyV").textContent = W.novelty < 35 ? "больше любимого" : W.novelty > 65 ? "больше нового" : "поровну";
  $("wcPopV").textContent = W.popularity < 35 ? "андеграунд" : W.popularity > 65 ? "хиты" : "всё подряд";
  const val = W.valence ?? 50, en = W.energy ?? 50;
  $("wcVal").value = val; $("wcEn").value = en;
  $("wcValV").textContent = val <= 20 ? "грустное" : val < 43 ? "скорее грустное" : val <= 57 ? "любое" : val < 80 ? "скорее весёлое" : "весёлое";
  $("wcEnV").textContent = en <= 20 ? "спокойное" : en < 43 ? "скорее спокойное" : en <= 57 ? "любое" : en < 80 ? "скорее бодрое" : "бодрое";
  document.querySelectorAll("#waveCfg .rng").forEach((r) => r.style.setProperty("--p", `${r.value}%`));
}
let wcTimer;
function setWave(patch) {
  W = { ...W, ...patch }; store.set("echo.wave", W); renderWaveCfg();
  clearTimeout(wcTimer); wcTimer = setTimeout(retuneWave, 450);
}
$("wcToggle").onclick = () => { const p = $("waveCfg"); p.hidden = !p.hidden; $("wcToggle").setAttribute("aria-expanded", String(!p.hidden)); if (!p.hidden) renderWaveCfg(); };
$("wcNovelty").oninput = (e) => setWave({ novelty: +e.target.value });
$("wcPop").oninput = (e) => setWave({ popularity: +e.target.value });
$("wcVal").oninput = (e) => setWave({ valence: +e.target.value });
$("wcEn").oninput = (e) => setWave({ energy: +e.target.value });
$("wcDl").onchange = (e) => setWave({ onlyDl: e.target.checked, ...(e.target.checked ? { indie: true } : {}) }); // downloads live on Audius / Jamendo
$("wcSrc").onclick = (e) => {
  const b = e.target.closest("[data-k]"); if (!b) return;
  const k = b.dataset.k, on = b.getAttribute("aria-pressed") !== "true";
  const next = { yt: W.yt !== false, sp: W.sp !== false, ap: W.ap !== false, indie: W.indie === true, [k]: on };
  if (!Object.values(next).some(Boolean)) return toast("Оставьте хотя бы один источник");
  if (k === "yt") ytBroken = false;
  setWave({ [k]: on });
};
document.querySelectorAll("#waveCfg .rng").forEach((r) => r.addEventListener("input", () => r.style.setProperty("--p", `${r.value}%`)));
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
      <button class="addpl" type="button" aria-label="Добавить в плейлист" title="Добавить в плейлист">+</button>
      <button class="dl" type="button" aria-label="Скачать" ${t.downloadable ? 'title="Скачать"' : 'disabled title="' + (t.src === "youtube" ? "Только прослушивание через YouTube" : t.catalog ? "Только прослушивание" : "Автор не разрешил скачивание") + '"'}><svg class="i"><use href="#i-dl"/></svg></button>
    </div>`).join("");
  sync();
}
function loading(el, n = 6) { el.innerHTML = Array.from({ length: n }, () => `<div class="tr"><span></span><span class="c skel"></span><span class="tt"><b class="skel" style="width:60%;height:14px"></b></span></div>`).join(""); }
document.addEventListener("click", (e) => {
  const row = e.target.closest(".tr[data-id]"); if (!row) return;
  const list = lists.get(row.parentElement.id) || [], t = list[+row.dataset.n]; if (!t) return;
  if (e.target.closest(".like")) return doLike(t);
  if (e.target.closest(".addpl")) return openPlMenu(t, e.target.closest(".addpl"));
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
  if (!opts.length) return toast(t.src === "youtube" ? "С YouTube можно только слушать, скачивания нет" : "Автор этого трека не разрешил скачивание");
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
  if (srcFilter === "youtube" || srcFilter === "apple" || srcFilter === "spotify") return runSourceSearch(q, srcFilter);
  const run = ++srcRun;
  const ytOn = srcFilter === "all" && !onlyDl && A.hasYouTube() && W.yt !== false;
  const words = nkey(q).split(" ").filter(Boolean);
  const ytLocal = ytOn ? buildYtIndex().then((idx) => [...idx.values()].filter((t) => { const s = nkey(`${t.artist} ${t.title}`); return words.every((w) => s.includes(w)); }).slice(0, 12)).catch(() => []) : Promise.resolve([]);
  const [{ list: base, failed }, hits] = await Promise.all([A.searchAll(q, { src: srcFilter, onlyDl }), ytLocal]);
  if (run !== srcRun || $("q").value.trim() !== q) return;
  if (failed && !hits.length) { $("results").innerHTML = `<div class="status err">Поиск не ответил. Проверьте интернет.</div>`; return; }
  const show = (ytList) => {
    const uploads = base.filter((t) => t.src === "local");
    let rest = base.filter((t) => t.src !== "local");
    if (srcFilter === "all" && ytList.length) {
      // free catalogs only where they really match, so hits are not buried under filler
      rest = rest.filter((t) => { const s = nkey(`${t.artist} ${t.title}`); return words.every((w) => s.includes(w)) && ((t.plays || 0) >= 300 || nkey(t.artist) === nkey(q) || nkey(t.title) === nkey(q)); }).slice(0, 15);
    }
    const seen = new Set();
    const list = [...uploads, ...ytList, ...rest].filter((t) => { const k = nkey(t.artist.split(",")[0]) + "|" + nkey(t.title); if (seen.has(t.id) || (t.src !== "local" && seen.has(k))) return false; seen.add(t.id); seen.add(k); return true; });
    rows($("results"), list, onlyDl ? "Нет треков со скачиванием по этому запросу" : `По запросу «${q}» ничего нет. Попробуйте на английском или другой жанр.`);
    const ytN = list.filter((t) => t.src === "youtube").length;
    $("srNote").textContent = `найдено: ${list.length}` + (ytN ? ` · YouTube Music: ${ytN}` : "");
    return list;
  };
  show(hits);
  if (ytOn && q.length >= 3) {
    try {
      const vids = await A.ytSearch(q);
      if (run !== srcRun || $("q").value.trim() !== q) return;
      const have = new Set(hits.map((t) => t.rawId));
      show([...hits, ...vids.filter((v) => !have.has(v.videoId)).map((v) => A.fromYouTube(v))]);
    } catch {}
  }
}
let srcRun = 0;
async function runSourceSearch(q, src) {
  const run = ++srcRun, res = $("results");
  const stale = () => run !== srcRun || $("q").value.trim() !== q || srcFilter !== src;
  if (src === "spotify" && !A.hasSpotify()) {
    res.innerHTML = `<div class="status">Поиск по Spotify ещё не подключён на сервере: нужен бесплатный ключ Spotify для разработчиков. Пока ищите через Apple Music, там почти тот же каталог.</div>`;
    $("srNote").textContent = ""; return;
  }
  try {
    if (src === "youtube") {
      // free part first: hits from the public YouTube music charts, then the YouTube search (daily quota)
      const words = nkey(q).split(" ").filter(Boolean);
      const idx = await buildYtIndex();
      const local = [...idx.values()].filter((t) => { const s = nkey(`${t.artist} ${t.title}`); return words.every((w) => s.includes(w)); }).slice(0, 20);
      if (stale()) return;
      if (local.length) rows(res, local);
      $("srNote").textContent = ytBroken ? "YouTube в этой сети не открывается: при запуске найду версию из других источников" : `YouTube · только прослушивание`;
      if (!A.hasYouTube() || q.length < 3) { if (!local.length) rows(res, [], "Ничего не нашлось"); return; }
      const vids = await A.ytSearch(q);
      if (stale()) return;
      const seen = new Set(local.map((t) => t.rawId));
      const list = [...local, ...vids.filter((v) => !seen.has(v.videoId)).map((v) => A.fromYouTube(v))];
      rows(res, list, "На YouTube ничего не нашлось");
      $("srNote").textContent = `YouTube: ${list.length} · только прослушивание, скачивания нет`;
      return;
    }
    const list = await A.catalogSearch(src, q);
    if (stale()) return;
    rows(res, list, `В ${A.CATALOG_NAME[src]} по запросу «${q}» ничего нет`);
    $("srNote").textContent = `${A.CATALOG_NAME[src]}: ${list.length} · играет полная версия с YouTube, Audius или Jamendo`;
  } catch (err) {
    if (stale()) return;
    res.innerHTML = `<div class="status err">${esc(err.message || "Поиск не ответил")}</div>`;
  }
}
document.addEventListener("click", async (e) => {
  if (e.target.id !== "ytFind") return;
  const q = $("q").value.trim(); if (!q) return;
  e.target.textContent = "ищу…";
  try {
    const vids = await A.ytSearch(q);
    const list = vids.map((v) => A.fromYouTube(v));
    const cur0 = lists.get("results") || [];
    rows($("results"), [...list, ...cur0], "Ничего не нашлось");
    $("srNote").textContent = `YouTube: ${list.length} · всего ${list.length + cur0.length}`;
  } catch (err) { toast(err.message); e.target.textContent = "искать на YouTube"; }
});
$("q").addEventListener("input", () => { clearTimeout(qTimer); qTimer = setTimeout(runSearch, srcFilter === "youtube" || srcFilter === "all" ? 700 : 450); });
$("q").addEventListener("keydown", (e) => { if (e.key === "Enter") { clearTimeout(qTimer); runSearch(); } });
$("fDl").onclick = (e) => { onlyDl = !onlyDl; e.currentTarget.setAttribute("aria-pressed", String(onlyDl)); runSearch(); };
document.querySelectorAll("[data-src]").forEach((b) => (b.onclick = () => {
  if (b.dataset.src === "jamendo" && !A.hasJamendo()) return toast("Jamendo не подключён: добавьте JAMENDO_CLIENT_ID на сервере");
  if (b.dataset.src === "youtube" && !A.hasYouTube()) return toast("YouTube не подключён на сервере");
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
      ${A.hasYouTube() ? `<button class="yt-btn" type="button" data-yt="${it.pos}" title="Оригинал через официальный плеер YouTube">▶ YouTube</button>` : "<span></span>"}
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
  const yb = e.target.closest("[data-yt]");
  if (yb && chData) {
    const it = chData.items.find((x) => x.pos === +yb.dataset.yt);
    yb.disabled = true;
    try {
      const vids = await A.ytSearch(`${it.artist} ${it.title}`);
      if (!vids.length) toast("На YouTube не нашлось");
      else playList([A.fromYouTube(vids[0], { title: it.title, artist: [it.artist, ...it.with].join(", ") })], 0, "list");
    } catch (err) { toast(err.message); }
    yb.disabled = false;
    return;
  }
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

// ---------------- add to playlist menu ----------------
function openPlMenu(t, anchor) {
  closeMenu();
  menu = document.createElement("div"); menu.className = "menu"; menu.setAttribute("role", "menu");
  const pls = R.P.playlists.slice(0, 12);
  menu.innerHTML = `<button type="button" role="menuitem" data-new="1"><b>Новый плейлист…</b><small>с этим треком</small></button>`
    + pls.map((p, i) => `<button type="button" role="menuitem" data-i="${i}"><b>${esc(p.title)}</b><small>${p.tracks.length}</small></button>`).join("");
  document.body.appendChild(menu);
  const r = anchor.getBoundingClientRect(), mw = menu.offsetWidth, mh = menu.offsetHeight;
  menu.style.left = Math.max(8, Math.min(innerWidth - mw - 8, r.right - mw)) + "px";
  menu.style.top = (r.top - mh - 8 > 8 ? r.top - mh - 8 : r.bottom + 8) + "px";
  menu.onclick = (e) => {
    const b = e.target.closest("button"); if (!b) return;
    closeMenu();
    if (b.dataset.new) {
      const name = `Плейлист ${R.P.playlists.length + 1}`;
      R.createPlaylist(name, [t]); toast(`Создан «${name}». Переименовать можно в Коллекции.`);
    } else {
      const p = pls[+b.dataset.i];
      toast(R.addToPlaylist(p.id, t) ? `Добавлено в «${p.title}»` : `Уже есть в «${p.title}»`);
    }
    if (!$("v-library").hidden) renderLibrary();
  };
}

// ---------------- import by link (Spotify, Apple Music, Deezer, YouTube) ----------------
const imp = { run: 0 };
const nkey = (s) => (s || "").toLowerCase().replace(/\(.*?\)|\[.*?\]/g, " ").replace(/feat\.?.*$|ft\..*$/i, " ").replace(/[^a-z0-9а-яё]+/gi, " ").trim();
let ytIndex = null;
async function buildYtIndex() {
  if (ytIndex) return ytIndex;
  ytIndex = new Map();
  for (const name of ["daily", "trending", "top"]) {
    try {
      for (const t of await E.ytCatalog(name)) {
        const k = nkey(t.artist.split(",")[0]) + "|" + nkey(t.title);
        if (!ytIndex.has(k)) ytIndex.set(k, t);
      }
    } catch {}
  }
  return ytIndex;
}
// Finds a playable version of an imported song. Order: uploads → YouTube video id from the link → YouTube hits
// catalog → Audius original → Audius version → YouTube search (only if a key is set). Returns {t, kind} or null.
async function matchImported(it, uploads) {
  const na = nkey((it.artist || "").split(",")[0]), nt = nkey(it.title);
  const up = uploads.find((u) => nkey(u.title) === nt && (!na || nkey(u.artist).includes(na)));
  if (up) return { t: up, kind: "orig" };
  if (it.videoId) return { t: A.fromYouTubeCatalog({ videoId: it.videoId, artist: it.artist, title: it.title }, "import"), kind: "youtube" };
  const idx = await buildYtIndex();
  const hit = idx.get(na + "|" + nt);
  if (hit) return { t: { ...hit }, kind: "youtube" };
  try {
    const list = await A.auSearch(`${it.artist} ${it.title}`.trim(), { limit: 12 });
    let best = null, bestS = 0;
    for (const t of list) {
      const tt = nkey(t.title), ta = nkey(t.artist);
      if (!nt || !tt.includes(nt)) continue;
      const orig = na && ta === na;
      const sc = (orig ? 10 : 0) + (na && (tt.includes(na) || ta.includes(na)) ? 3 : 0) + (it.duration && t.duration ? Math.max(0, 3 - Math.abs(it.duration - t.duration) / 10) : 0);
      if (sc > bestS) { bestS = sc; best = { t, kind: orig ? "orig" : "version" }; }
    }
    if (best && bestS >= 3) return best;
  } catch {}
  if (A.hasYouTube()) {
    try {
      const vids = await A.ytSearch(`${it.artist} ${it.title}`);
      if (vids[0]) return { t: A.fromYouTube(vids[0], { title: it.title, artist: it.artist }), kind: "youtube" };
    } catch {}
  }
  return null;
}
function impRender(st) {
  const total = st.total || 1;
  $("impName").textContent = `${st.service}: ${st.name}`;
  $("impCount").textContent = `${st.done} из ${st.total}`;
  $("impOk").style.width = (st.ok / total) * 100 + "%";
  $("impMiss").style.width = (st.miss.length / total) * 100 + "%";
  $("impBar").setAttribute("aria-valuenow", Math.round((st.done / total) * 100));
  $("impOkN").textContent = st.ok; $("impVerN").textContent = st.ver; $("impMissN").textContent = st.miss.length;
  $("impLeft").textContent = st.done < st.total ? `осталось ${st.total - st.done}` : st.stopped ? "остановлено" : "готово";
  $("impMissBox").hidden = !st.miss.length;
  $("impMissList").innerHTML = st.miss.slice(0, 300).map((m) => `<li>${esc(m.artist)} — ${esc(m.title)}</li>`).join("");
  $("impPlay").hidden = !st.found.length;
}
$("impForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const url = $("impUrl").value.trim();
  if (!url) return $("impUrl").focus();
  const run = ++imp.run;
  $("impGo").disabled = true; $("impProg").hidden = false;
  $("impName").textContent = "Читаю плейлист…"; $("impCount").textContent = ""; $("impOk").style.width = $("impMiss").style.width = "0";
  let data;
  try { data = await A.importLink(url); }
  catch (err) { $("impName").textContent = err.message; $("impGo").disabled = false; return; }
  const dest = $("impDest").value;
  const plId = dest === "new" ? R.createPlaylist(data.name, [], `Перенесено из ${data.service}`) : null;
  const uploads = await A.localTracks();
  const st = { service: data.service, name: data.name, total: data.tracks.length, done: 0, ok: 0, ver: 0, miss: [], found: [], stopped: false };
  imp.state = st;
  impRender(st);
  let i = 0;
  const worker = async () => {
    while (i < data.tracks.length && run === imp.run) {
      const it = data.tracks[i++];
      const m = await matchImported(it, uploads);
      if (run !== imp.run) return;
      st.done++;
      if (m) {
        st.ok++; if (m.kind === "version") st.ver++;
        m.t.via = "import";
        st.found.push(m.t);
        if (plId) R.addToPlaylist(plId, m.t); else R.addLike(m.t);
      } else st.miss.push(it);
      impRender(st);
    }
  };
  await Promise.all([worker(), worker(), worker(), worker()]);
  if (run === imp.run) {
    R.save();
    toast(`Перенесено ${st.ok} из ${st.total}${st.miss.length ? `, не найдено ${st.miss.length}` : ""}`);
    renderLibrary();
  }
  $("impGo").disabled = false;
});
$("impStop").onclick = () => { imp.run++; if (imp.state) { imp.state.stopped = true; impRender(imp.state); } $("impGo").disabled = false; R.save(); renderLibrary(); };
$("impPlay").onclick = () => imp.state && playList(imp.state.found.map(A.refresh), 0, "list");

// ---------------- own playlists ----------------
let openPl = null;
function renderPlView() {
  const p = openPl && R.playlistById(openPl);
  $("plView").hidden = !p;
  if (!p) return;
  $("plv-h").textContent = p.title;
  $("plvNote").textContent = `${p.tracks.length} треков${p.desc ? " · " + p.desc : ""}`;
  rows($("plTracks"), p.tracks.map((t) => A.refresh({ ...t })), "Плейлист пуст. Добавляйте треки кнопкой «+» у любой песни.");
}
$("plNew").onclick = () => { openPl = R.createPlaylist(`Плейлист ${R.P.playlists.length + 1}`); renderLibrary(); $("plView").scrollIntoView({ behavior: "smooth" }); };
$("plvPlay").onclick = () => { const p = R.playlistById(openPl); if (p) playList(p.tracks.map((t) => A.refresh({ ...t })), 0, "list"); };
$("plvClose").onclick = () => { openPl = null; renderPlView(); };
$("plvShare").onclick = async () => {
  const p = R.playlistById(openPl); if (!p) return;
  if (!p.tracks.length) return toast("Сначала добавьте в плейлист треки");
  const b = $("plvShare"); b.disabled = true;
  try {
    const r = await A.api("/api/share", { method: "POST", body: { title: p.title, desc: p.desc || "", tracks: p.tracks.slice(0, 500) } });
    let copied = false;
    if (navigator.share && matchMedia("(pointer:coarse)").matches) { try { await navigator.share({ title: p.title, text: `Плейлист «${p.title}» в Эхо`, url: r.url }); copied = true; } catch {} }
    if (!copied) { try { await navigator.clipboard.writeText(r.url); toast("Ссылка на плейлист скопирована"); } catch { prompt("Ссылка на плейлист", r.url); } }
  } catch (err) { toast(err.message || "Не удалось создать ссылку"); }
  b.disabled = false;
};

// ---------------- a shared playlist opened by link: #pl-<id> ----------------
let shared = null;
async function openShared(id) {
  const dlg = $("shareDlg");
  $("shT").textContent = "Загружаю плейлист…"; $("shN").textContent = ""; $("shArt").innerHTML = ""; loading($("shTracks"), 4);
  if (!dlg.open) dlg.showModal();
  try {
    const d = await A.api(`/api/share/${id}`);
    const safe = (u) => (typeof u === "string" && (u.startsWith("/api/") || u.startsWith("https://")) ? u : "");
    const tracks = (d.tracks || []).map((t) => A.refresh({ ...t, link: safe(t.link), extLink: safe(t.extLink), cover: safe(t.cover), coverSm: safe(t.coverSm), stream: safe(t.stream) }));
    shared = { title: d.title, desc: d.desc, tracks };
    $("shT").textContent = d.title;
    const mins = Math.round(tracks.reduce((s, t) => s + (t.duration || 0), 0) / 60);
    $("shN").textContent = `${tracks.length} треков${mins ? ` · ${mins} мин` : ""}`;
    $("shArt").innerHTML = tracks.slice(0, 4).map((t, i) => cv(t.coverSm || t.cover, i)).join("");
    $("shArt").classList.toggle("one", tracks.length < 4);
    rows($("shTracks"), tracks.slice(0, 50));
  } catch (err) {
    shared = null;
    $("shT").textContent = "Плейлист не открылся"; $("shTracks").innerHTML = `<div class="status err">${esc(err.message || "Ссылка не найдена")}</div>`;
  }
}
$("shPlay").onclick = () => { if (!shared?.tracks.length) return; $("shareDlg").close(); playList(shared.tracks.slice(), 0, "list"); };
$("shAdd").onclick = () => {
  if (!shared?.tracks.length) return;
  const id = R.createPlaylist(shared.title, shared.tracks, shared.desc || "");
  $("shareDlg").close(); toast(`Плейлист «${shared.title}» добавлен в коллекцию`);
  openPl = id; location.hash = "#library";
};
$("shClose").onclick = () => $("shareDlg").close();
$("shareDlg").addEventListener("click", (e) => { if (e.target === $("shareDlg")) $("shareDlg").close(); });
$("plvRename").onclick = () => {
  const h = $("plv-h");
  if (h.isContentEditable) { R.renamePlaylist(openPl, h.textContent); h.contentEditable = "false"; $("plvRename").textContent = "Переименовать"; renderLibrary(); return; }
  h.contentEditable = "true"; h.focus(); document.getSelection().selectAllChildren(h); $("plvRename").textContent = "Сохранить";
};
$("plv-h").addEventListener("keydown", (e) => { if (e.key === "Enter") { e.preventDefault(); $("plvRename").click(); } });
$("plvDel").onclick = (e) => {
  const b = e.currentTarget;
  if (b.dataset.arm !== "1") { b.dataset.arm = "1"; b.textContent = "Точно удалить?"; setTimeout(() => { b.dataset.arm = ""; b.textContent = "Удалить"; }, 4000); return; }
  b.dataset.arm = ""; b.textContent = "Удалить";
  R.deletePlaylist(openPl); openPl = null; renderLibrary(); toast("Плейлист удалён");
};

// ---------------- library ----------------
function renderLibrary() {
  setTimeout(renderPlView, 0);
  const liked = Object.values(R.P.likes).map(A.refresh).sort((a, b) => b.likedAt - a.likedAt);
  $("lkNote").textContent = liked.length ? `${liked.length} треков` : "";
  rows($("liked"), liked, "Нажмите на сердечко у трека, и он появится здесь");
  rows($("history"), R.P.hist.slice(0, 40).map(A.refresh), "История появится после первых прослушиваний");
  $("playlists").innerHTML = R.P.playlists.length
    ? R.P.playlists.map((p, i) => `<button class="card" type="button" data-i="${i}"><div class="cover">${cv(p.tracks.find((t) => t.cover)?.cover, i)}</div><b>${esc(p.title)}</b><span>${p.tracks.length} треков</span></button>`).join("")
    : `<div class="status">Попросите ИИшку в поиске собрать плейлист и сохраните его</div>`;
}
$("playlists").onclick = (e) => { const c = e.target.closest(".card"); if (!c) return; openPl = R.P.playlists[+c.dataset.i].id; renderPlView(); $("plView").scrollIntoView({ behavior: "smooth" }); };

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
  const ex = E.explain();
  const arms = (title, list, names = {}) => `<div><h3>${title}</h3><div class="bars">${list.length ? list.slice(0, 6).map((x) => `<div><span>${esc(names[x.k] || x.k)}</span><i style="width:${Math.max(6, x.rate * 100)}%"></i><em>${Math.round(x.rate * 100)}%</em></div>`).join("") : '<span class="src">Волна ещё учится</span>'}</div></div>`;
  $("taste").innerHTML = bars("Жанры", "g", 6) + bars("Настроения", "m", 6) + bars("Артисты", "a", 6)
    + arms("Что заходит (жанры)", ex.genres) + arms("Откуда брать (источники)", ex.sources, E.SOURCE_NAMES)
    + `<div><h3>Память волны</h3><p class="src" style="margin:0">Волна помнит ${ex.served} предложенных треков и не повторяет их от 1 до 14 дней.</p></div>`;
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

// ---------------- full-screen player: cover glow, synced lyrics with dust, audio effects ----------------
const fs = { open: false, lines: [], idx: -1, key: "", rgb: "150,150,150" };
function openFs() {
  if (!cur()) return toast("Сначала включите трек");
  fs.open = true; $("fs").hidden = false; document.body.style.overflow = "hidden";
  fsTrack(); requestAnimationFrame(sizeDust);
}
function closeFs() {
  fs.open = false; $("fs").hidden = true; document.body.style.overflow = "";
}
$("npFs").onclick = (e) => { e.stopPropagation(); openFs(); };
document.querySelector(".np").addEventListener("click", openFs);
$("vibeT").addEventListener("click", () => cur() && openFs());
$("vibeT").style.cursor = "pointer";
$("fsClose").onclick = closeFs;
document.addEventListener("keydown", (e) => { if (e.key === "Escape" && fs.open) closeFs(); });
$("fsPlay").onclick = toggle; $("fsNext").onclick = () => next(); $("fsPrev").onclick = prev;
$("fsLike").onclick = () => doLike(cur());
$("fsDl").onclick = (e) => openDlMenu(cur(), e.currentTarget);
$("fsSeek").onclick = (e) => { const r = e.currentTarget.getBoundingClientRect(); if (eng.dur()) eng.seek(((e.clientX - r.left) / r.width) * eng.dur()); };
$("fsFxBtn").onclick = () => { const on = $("fxPanel").hidden; $("fxPanel").hidden = !on; $("fsFxBtn").setAttribute("aria-pressed", String(on)); fxRender(); };

// dominant colour of the cover → glow around it
function coverColor(url) {
  return new Promise((resolve) => {
    if (!url || !url.startsWith("/")) return resolve(null); // only same-origin covers can be read
    const img = new Image();
    img.onload = () => {
      try {
        const c = document.createElement("canvas"); c.width = c.height = 32;
        const x = c.getContext("2d"); x.drawImage(img, 0, 0, 32, 32);
        const d = x.getImageData(0, 0, 32, 32).data;
        let r = 0, g = 0, b = 0, w = 0;
        for (let i = 0; i < d.length; i += 4) {
          const R = d[i], G = d[i + 1], B = d[i + 2], mx = Math.max(R, G, B), mn = Math.min(R, G, B);
          const sat = mx ? (mx - mn) / mx : 0, wt = 0.15 + sat * sat * 4 * (mx > 40 ? 1 : 0.2);
          r += R * wt; g += G * wt; b += B * wt; w += wt;
        }
        r /= w; g /= w; b /= w;
        const mx = Math.max(r, g, b, 1), k = Math.min(2.2, 230 / mx); // brighten so the glow is visible
        resolve([r * k, g * k, b * k].map((v) => Math.round(Math.min(255, v))).join(","));
      } catch { resolve(null); }
    };
    img.onerror = () => resolve(null);
    img.src = url;
  });
}

function fsTrack() {
  const t = cur(); if (!t || !fs.open) return;
  $("fsT").textContent = t.title; $("fsA").textContent = t.artist;
  const src = t.src === "youtube" ? `<a href="${esc(t.link)}" target="_blank" rel="noopener">Источник: YouTube</a> · <a href="${esc(A.spotifySearchUrl(t))}" target="_blank" rel="noopener">Найти в Spotify</a>`
    : t.src === "audius" && t.link ? `<a href="${esc(t.link)}" target="_blank" rel="noopener">Источник: Audius</a>`
    : t.src === "jamendo" && t.link ? `<a href="${esc(t.link)}" target="_blank" rel="noopener">Источник: Jamendo</a>` : t.src === "local" ? "Ваша музыка" : "";
  const ext = t.extLink && t.extName && t.extName !== "YouTube" ? ` · <a href="${esc(t.extLink)}" target="_blank" rel="noopener">Открыть в ${esc(t.extName)}</a>` : "";
  $("fsSrc").innerHTML = src + ext + (t.reason ? ` · ${esc(t.reason)}` : "");
  $("fsFrom").textContent = pl.mode === "wave" ? (pl.mood ? `Моя волна · ${pl.mood}` : "Моя волна") : t.src === "youtube" ? "Источник: YouTube" : "Сейчас играет";
  $("fs").classList.toggle("video", t.src === "youtube");
  $("fsCover").innerHTML = cv(t.cover && t.src === "audius" ? t.cover.replace(/\/480$/, "/1000") : t.cover, 3);
  $("fsLike").setAttribute("aria-pressed", String(R.isLiked(t)));
  $("fsDl").disabled = !t.downloadable;
  coverColor(t.cover).then((rgb) => { fs.rgb = rgb || "150,150,150"; $("fs").style.setProperty("--glow", fs.rgb); });
  loadLyrics(t);
  fxRender();
}

// ---- lyrics ----
function parseLrc(text) {
  const out = [];
  for (const line of (text || "").split(/\r?\n/)) {
    const stamps = [...line.matchAll(/\[(\d+):(\d+(?:\.\d+)?)\]/g)];
    if (!stamps.length) continue;
    const words = line.replace(/\[[^\]]*\]/g, "").trim();
    for (const m of stamps) out.push({ t: +m[1] * 60 + +m[2], text: words });
  }
  return out.sort((a, b) => a.t - b.t);
}
async function loadLyrics(t) {
  const key = t.id;
  if (fs.key === key) return;
  fs.key = key; fs.lines = []; fs.idx = -1;
  const box = $("lyr");
  box.className = "lyr"; box.innerHTML = `<p class="none">Ищу текст…</p>`; $("lyrSrc").textContent = "";
  try {
    const q = new URLSearchParams({ artist: t.artist || "", title: t.title || "", duration: Math.round(t.duration || 0) });
    const d = await A.api(`/api/lyrics?${q}`);
    if (fs.key !== key) return;
    if (d.synced) {
      fs.lines = parseLrc(d.synced);
      box.innerHTML = fs.lines.map((l, i) => `<p data-i="${i}" class="${l.text ? "" : "blank"}">${esc(l.text) || "♪"}</p>`).join("");
    } else if (d.plain) {
      box.classList.add("plain");
      box.innerHTML = d.plain.split(/\n/).map((l) => `<p>${esc(l) || "&nbsp;"}</p>`).join("");
      box.scrollTop = 0;
    } else {
      box.innerHTML = `<p class="none">${d.instrumental ? "Инструментальный трек, слов нет" : "Текст этой песни не найден"}</p>`;
    }
    $("lyrSrc").textContent = d.source ? `Текст: ${d.source}` : "";
  } catch { if (fs.key === key) box.innerHTML = `<p class="none">Тексты сейчас недоступны</p>`; }
}
$("lyr").addEventListener("click", (e) => { const p = e.target.closest("p[data-i]"); if (p && fs.lines[+p.dataset.i]) eng.seek(fs.lines[+p.dataset.i].t + 0.05); });
let userScrollAt = 0;
$("lyr").addEventListener("wheel", () => { userScrollAt = Date.now(); }, { passive: true });
$("lyr").addEventListener("touchmove", () => { userScrollAt = Date.now(); }, { passive: true });

// ---- dust: the finished line breaks into particles that drift away ----
const dustCv = $("dust"), dctx = dustCv.getContext("2d");
let dust = [], dustRunning = false;
function sizeDust() {
  const r = dustCv.getBoundingClientRect(), dpr = Math.min(2, devicePixelRatio || 1);
  dustCv.width = r.width * dpr; dustCv.height = r.height * dpr; dctx.setTransform(dpr, 0, 0, dpr, 0, 0);
}
addEventListener("resize", () => fs.open && sizeDust());
function shatter(el) {
  if (matchMedia("(prefers-reduced-motion: reduce)").matches || !el.textContent.trim()) return;
  const host = dustCv.getBoundingClientRect(), r = el.getBoundingClientRect();
  if (r.bottom < host.top || r.top > host.bottom) return;
  const cs = getComputedStyle(el), off = document.createElement("canvas");
  const w = Math.ceil(r.width), h = Math.ceil(r.height);
  if (!w || !h) return;
  off.width = w; off.height = h;
  const o = off.getContext("2d");
  o.font = `${cs.fontWeight} ${cs.fontSize} ${cs.fontFamily}`; o.textBaseline = "top"; o.fillStyle = "#fff";
  // re-flow the text into the same lines the browser used
  const words = el.textContent.split(/\s+/), lh = parseFloat(cs.lineHeight) || parseFloat(cs.fontSize) * 1.2;
  let line = "", y = parseFloat(cs.paddingTop) || 0;
  for (const wd of words) {
    const test = line ? line + " " + wd : wd;
    if (o.measureText(test).width > w && line) { o.fillText(line, 0, y); line = wd; y += lh; } else line = test;
  }
  o.fillText(line, 0, y);
  const data = o.getImageData(0, 0, w, h).data, step = Math.max(2, Math.round(parseFloat(cs.fontSize) / 14));
  const fg = getComputedStyle(root).getPropertyValue("--fg").trim() || "#fff";
  for (let yy = 0; yy < h; yy += step) for (let xx = 0; xx < w; xx += step) {
    if (data[(yy * w + xx) * 4 + 3] < 128) continue;
    dust.push({ x: r.left - host.left + xx, y: r.top - host.top + yy, vx: 0.4 + Math.random() * 1.6, vy: -0.6 - Math.random() * 1.4, life: 1, d: 0.008 + Math.random() * 0.014 + (xx / w) * 0.004, s: step * (0.6 + Math.random() * 0.6), c: Math.random() < 0.35 ? `rgb(${fs.rgb})` : fg, delay: (xx / w) * 18 });
  }
  if (dust.length > 9000) dust = dust.slice(-9000);
  if (!dustRunning) { dustRunning = true; requestAnimationFrame(dustLoop); }
}
function dustLoop() {
  const r = dustCv.getBoundingClientRect();
  dctx.clearRect(0, 0, r.width, r.height);
  for (const p of dust) {
    if (p.delay > 0) { p.delay--; dctx.globalAlpha = 1; dctx.fillStyle = p.c; dctx.fillRect(p.x, p.y, p.s, p.s); continue; }
    p.x += p.vx; p.y += p.vy; p.vx *= 1.01; p.vy -= 0.01; p.life -= p.d;
    dctx.globalAlpha = Math.max(0, p.life); dctx.fillStyle = p.c; dctx.fillRect(p.x, p.y, p.s * p.life, p.s * p.life);
  }
  dctx.globalAlpha = 1;
  dust = dust.filter((p) => p.life > 0);
  if (dust.length) requestAnimationFrame(dustLoop); else dustRunning = false;
}
function lyricsTick() {
  if (!fs.open || !fs.lines.length) return;
  const t = eng.time() + 0.15;
  let i = fs.idx;
  if (i < 0 || fs.lines[i]?.t > t || (fs.lines[i + 1] && fs.lines[i + 1].t <= t)) {
    i = -1; for (let k = 0; k < fs.lines.length; k++) { if (fs.lines[k].t <= t) i = k; else break; }
  }
  if (i === fs.idx) return;
  const box = $("lyr"), els = box.querySelectorAll("p[data-i]");
  const prevIdx = fs.idx;
  fs.idx = i;
  els.forEach((el, k) => { el.classList.toggle("on", k === i); el.classList.toggle("gone", k < i); });
  if (prevIdx >= 0 && i === prevIdx + 1 && els[prevIdx]) shatter(els[prevIdx]); // only on natural progress, not on seek
  if (els[i] && Date.now() - userScrollAt > 2500) {
    const target = els[i].offsetTop - box.clientHeight / 2 + els[i].offsetHeight / 2;
    box.scrollTo({ top: target, behavior: "smooth" });
  }
}
// The YouTube clip is one iframe that never moves in the DOM (moving it would restart the video). It is laid over
// the cover of the full-screen player, over the big cover of «Моя волна» on the home screen, or docked small in the
// corner elsewhere. YouTube's rules require the player to stay visible (at least 200×200) while it plays.
function ytPlace() {
  const box = $("ytbox");
  if (box.hidden) return;
  let target = null;
  if (fs.open) target = $("fsCover");
  else if (!$("v-home").hidden) {
    const r = $("bigArt").getBoundingClientRect();
    if (r.width >= 200 && r.height >= 200 && r.top >= 0 && r.bottom <= innerHeight - 80) target = $("bigArt");
  }
  box.classList.toggle("pinned", !!target);
  box.classList.toggle("in-fs", !!target && fs.open);
  $("wave").classList.toggle("yt", !!target && !fs.open);
  if (target) {
    const r = target.getBoundingClientRect();
    Object.assign(box.style, { left: r.left + "px", top: r.top + "px", width: r.width + "px", height: r.height + "px", borderRadius: getComputedStyle(target).borderRadius });
  } else if (box.style.left) box.removeAttribute("style");
}
(function fsLoop() {
  ytPlace();
  if (fs.open) {
    lyricsTick();
    const d = eng.dur() || cur()?.duration || 0, p = d ? (eng.time() / d) * 100 : 0;
    $("fsFill").style.width = p + "%"; $("fsCur").textContent = fmt(eng.time()); $("fsDur").textContent = fmt(d);
    const playing = eng.playing();
    $("fs").classList.toggle("playing", playing);
    $("fsPlayIcon").setAttribute("href", playing ? "#i-pause" : "#i-play");
  }
  requestAnimationFrame(fsLoop);
})();

// ---- audio effects: speed (slowed / sped up), bass, reverb ----
const FX_DEFAULT = { rate: 1, bass: 0, rev: 0, pitch: true };
const FX_PRESETS = { slowed: { rate: 0.85, bass: 4, rev: 45, pitch: true }, speed: { rate: 1.25, bass: 2, rev: 0, pitch: true }, bass: { rate: 1, bass: 12, rev: 0, pitch: true }, vibe: { rate: 0.92, bass: 6, rev: 30, pitch: true }, reset: FX_DEFAULT };
let FX = { ...FX_DEFAULT, ...store.get("echo.fx", {}) };
const graph = { ctx: null };
function impulse(ctx, seconds = 3.2, decay = 2.6) {
  const len = ctx.sampleRate * seconds, buf = ctx.createBuffer(2, len, ctx.sampleRate);
  for (let c = 0; c < 2; c++) { const ch = buf.getChannelData(c); for (let i = 0; i < len; i++) ch[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / len, decay); }
  return buf;
}
function ensureGraph() {
  if (graph.ctx) { if (graph.ctx.state === "suspended") graph.ctx.resume(); return true; }
  try {
    const ctx = new (window.AudioContext || window.webkitAudioContext)();
    const src = ctx.createMediaElementSource(audio);
    const bass = ctx.createBiquadFilter(); bass.type = "lowshelf"; bass.frequency.value = 110;
    const dry = ctx.createGain(), wet = ctx.createGain(), conv = ctx.createConvolver(), master = ctx.createGain();
    conv.buffer = impulse(ctx);
    src.connect(bass); bass.connect(dry); bass.connect(conv); conv.connect(wet); dry.connect(master); wet.connect(master); master.connect(ctx.destination);
    Object.assign(graph, { ctx, bass, dry, wet });
    return true;
  } catch { return false; }
}
function applyFx() {
  audio.playbackRate = FX.rate;
  audio.preservesPitch = audio.mozPreservesPitch = audio.webkitPreservesPitch = !FX.pitch;
  if (yt.player?.setPlaybackRate && isYT()) {
    let rates = [0.5, 0.75, 1, 1.25, 1.5];
    try { const r = yt.player.getAvailablePlaybackRates?.(); if (r?.length) rates = r; } catch {}
    yt.player.setPlaybackRate(rates.reduce((a, b) => (Math.abs(b - FX.rate) < Math.abs(a - FX.rate) ? b : a)));
  }
  const needGraph = FX.bass > 0 || FX.rev > 0 || graph.ctx;
  if (needGraph && ensureGraph()) {
    const now = graph.ctx.currentTime;
    graph.bass.gain.setTargetAtTime(FX.bass, now, 0.05);
    graph.wet.gain.setTargetAtTime(FX.rev / 100 * 0.9, now, 0.05);
    graph.dry.gain.setTargetAtTime(1 - FX.rev / 100 * 0.35, now, 0.05);
  }
}
function fxRender() {
  $("fxRate").value = FX.rate; $("fxBass").value = FX.bass; $("fxRev").value = FX.rev; $("fxPitch").checked = FX.pitch;
  $("fxRateV").textContent = `${Math.round(FX.rate * 100)}%`; $("fxBassV").textContent = `+${FX.bass} дБ`; $("fxRevV").textContent = `${FX.rev}%`;
  const active = Object.entries(FX_PRESETS).find(([, v]) => v.rate === FX.rate && v.bass === FX.bass && v.rev === FX.rev)?.[0];
  document.querySelectorAll(".fx-presets .mood").forEach((b) => b.setAttribute("aria-pressed", String(b.dataset.p === active)));
  $("fxNote").textContent = isYT() ? "Для YouTube доступна только скорость: бас и реверб работают с треками Audius, Jamendo и вашими загрузками." : "";
  $("fsFxBtn").classList.toggle("on", FX.rate !== 1 || FX.bass > 0 || FX.rev > 0);
}
function setFx(patch) { FX = { ...FX, ...patch }; store.set("echo.fx", FX); applyFx(); fxRender(); }
$("fxRate").oninput = (e) => setFx({ rate: +e.target.value });
$("fxBass").oninput = (e) => setFx({ bass: +e.target.value });
$("fxRev").oninput = (e) => setFx({ rev: +e.target.value });
$("fxPitch").onchange = (e) => setFx({ pitch: e.target.checked });
document.querySelector(".fx-presets").onclick = (e) => { const b = e.target.closest("[data-p]"); if (b) setFx({ ...FX_PRESETS[b.dataset.p] }); };
audio.addEventListener("play", () => { if (FX.bass > 0 || FX.rev > 0) applyFx(); else audio.playbackRate = FX.rate; });
audio.addEventListener("loadedmetadata", () => { audio.playbackRate = FX.rate; audio.preservesPitch = !FX.pitch; });

// ---------------- router ----------------
const VIEWS = ["home", "search", "charts", "library", "login", "upload", "prefs", "welcome"];
function route() {
  const sh = /^#pl-([A-Za-z0-9_-]{8,16})$/.exec(location.hash);
  if (sh) { history.replaceState(null, "", location.pathname + location.search + "#home"); openShared(sh[1]); }
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
