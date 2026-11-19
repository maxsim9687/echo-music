// «Моя волна 2»: taste profile with time decay, instant session reaction, flow by tempo/key,
// discovery slots and diversity re-ranking. Same algorithm is described in apps/SPEC.md for the native apps.
const KEY = "echo.profile";

// ve: where the preset sits on the mood map [valence 0 = sad … 1 = happy, energy 0 = calm … 1 = intense].
// yt: YouTube searches that really return this kind of music; ytArtists: names that mark a chart hit as matching.
export const MOOD_PRESETS = {
  "Энергия": { genres: ["Electronic", "Drum & Bass", "House", "Trap", "Hip-Hop/Rap", "Pop", "Rock"], moods: ["Energizing", "Upbeat", "Excited", "Fiery", "Empowering", "Rowdy"], bpm: [118, 180], queries: ["energy", "workout", "hype"], ve: [0.75, 0.9],
    yt: ["workout motivation songs", "energetic pop songs", "hype rap songs", "gym music hits"], ytArtists: [] },
  "Спокойствие": { genres: ["Ambient", "Lo-Fi", "Acoustic", "Jazz", "Folk", "Classical"], moods: ["Peaceful", "Easygoing", "Tender", "Cool", "Romantic"], bpm: [50, 100], queries: ["chill", "calm", "relax"], ve: [0.6, 0.18],
    yt: ["calm acoustic songs", "chill indie songs", "relaxing soft songs", "peaceful piano song"], ytArtists: [] },
  "Фокус": { genres: ["Lo-Fi", "Ambient", "Electronic", "Classical", "Downtempo"], moods: ["Peaceful", "Cool", "Sophisticated", "Serious"], bpm: [60, 112], queries: ["lofi study", "focus", "study beats"], ve: [0.5, 0.3], instrumental: true,
    yt: ["lofi hip hop beat", "study music instrumental", "ambient focus music track"], ytArtists: [] },
  "Ночь": { genres: ["Deep House", "Techno", "Downtempo", "R&B/Soul", "Synthwave"], moods: ["Brooding", "Cool", "Sensual", "Melancholy"], bpm: [80, 128], queries: ["night", "late night", "night drive"], ve: [0.4, 0.5],
    yt: ["late night r&b songs", "night drive synthwave", "night vibe songs"], ytArtists: ["the weeknd", "lana del rey"] },
  "Грусть": { genres: ["Alternative", "Acoustic", "Ambient", "Lo-Fi", "Pop", "Folk"], moods: ["Melancholy", "Sentimental", "Yearning", "Brooding"], bpm: [50, 108], queries: ["sad", "melancholy", "heartbreak"], ve: [0.12, 0.28],
    yt: ["sad songs", "sad pop songs", "heartbreak songs", "грустные песни"], ytArtists: ["billie eilish", "lewis capaldi", "olivia rodrigo"] },
  "Phonk": { genres: ["Trap", "Hip-Hop/Rap", "Electronic"], moods: ["Aggressive", "Fiery", "Defiant", "Energizing", "Gritty"], bpm: [115, 175], queries: ["phonk", "memphis phonk", "phonk house", "dark phonk"], tags: ["phonk", "memphis", "cowbell"], ve: [0.3, 0.9],
    yt: ["phonk", "Kordhell phonk", "INTERWORLD phonk", "DVRST phonk", "dark phonk song", "memphis phonk"], ytArtists: ["kordhell", "dvrst", "interworld", "playaphonk", "kslv", "moondeity", "hensonn", "ghostface playa", "lxngvx", "sxmpra", "naeleck", "pharmacist", "dxrk"] },
  "Drift Phonk": { genres: ["Trap", "Electronic", "Hip-Hop/Rap"], moods: ["Aggressive", "Energizing", "Fiery"], bpm: [110, 170], queries: ["drift phonk", "phonk drift", "brazilian phonk", "funk phonk"], tags: ["driftphonk", "drift", "phonk", "brazilianphonk"], ve: [0.4, 0.95],
    yt: ["drift phonk", "brazilian phonk", "funk phonk", "MXZI phonk", "DJ FKU phonk", "phonk drift car"], ytArtists: ["mxzi", "dj fku", "sma$her", "onimxru", "kaito shoma", "dyrtywork", "hxi", "slowboy", "dj samir", "scxr soul", "ariis"] },
};
export const WAVE_DEFAULTS = { novelty: 50, popularity: 50, tempo: "any", vocals: "any", onlyDl: false, yt: true, valence: 50, energy: 50 };
const TEMPO = { slow: [0, 95], mid: [95, 125], fast: [125, 999] };
export const STARTER_GENRES = ["Electronic", "Hip-Hop/Rap", "Lo-Fi", "House", "Rock", "Pop", "Ambient", "Alternative", "Drum & Bass", "Techno", "Jazz", "R&B/Soul", "Trap", "Classical"];
export const ALL_GENRES = [...STARTER_GENRES, "Deep House", "Tech House", "Dubstep", "Trance", "Downtempo", "Experimental", "Soundtrack", "Acoustic", "Funk", "Disco", "Synthwave", "Future Bass", "Metal", "Folk", "Latin", "Reggae", "World", "Hyperpop"];

const DAY = 86400e3;
const HALF_LIFE = 30 * DAY;

function blank() {
  return { g: {}, m: {}, a: {}, an: {}, t: {}, fav: {}, hist: [], skip: {}, ban: {}, banA: {}, banCount: {}, likes: {}, playlists: [], served: {}, servedA: {}, gb: {}, sb: {}, dp: {}, decayAt: Date.now(), updated: 0 };
}
function load() {
  try { return { ...blank(), ...(JSON.parse(localStorage.getItem(KEY)) || {}) }; } catch { return blank(); }
}
export let P = load();
let onChange = () => {};
export const onProfileChange = (fn) => { onChange = fn; };

// Old tastes fade: halve every 30 days, so the wave follows who you are now.
function decay() {
  const now = Date.now(), dt = now - (P.decayAt || now);
  if (dt < 3600e3) return;
  const f = Math.pow(0.5, dt / HALF_LIFE);
  for (const map of ["g", "m", "a", "t"]) for (const k in P[map]) {
    P[map][k] *= f;
    if (Math.abs(P[map][k]) < 0.05) delete P[map][k];
  }
  P.decayAt = now;
}
decay();
pruneServed();

// ---- served memory: everything the wave queued, so it does not offer it again for days ----
function pruneServed() {
  const now = Date.now();
  P.served = P.served || {}; P.servedA = P.servedA || {};
  const ids = Object.entries(P.served);
  for (const [id, at] of ids) if (now - at > 14 * DAY) delete P.served[id];
  if (Object.keys(P.served).length > 4000) {
    for (const [id] of Object.entries(P.served).sort((a, b) => a[1] - b[1]).slice(0, 1000)) delete P.served[id];
  }
  for (const [a, v] of Object.entries(P.servedA)) if (now - v.at > 3 * DAY) delete P.servedA[a];
}
export function markServed(tracks) {
  const now = Date.now();
  for (const t of tracks) {
    P.served[t.id] = now;
    if (t.artistId) {
      const v = P.servedA[t.artistId] || { n: 0, at: now };
      const decayN = v.n * Math.pow(0.5, (now - v.at) / (12 * 3600e3));
      P.servedA[t.artistId] = { n: decayN + 1, at: now };
    }
  }
  pruneServed(); save();
}
const daypartOf = (d = new Date()) => { const h = d.getHours(); return h < 6 ? "night" : h < 11 ? "morning" : h < 17 ? "day" : h < 22 ? "evening" : "night"; };
const bucketOf = (t) => t.genre || (t.src === "youtube" ? "Мейнстрим" : "Другое");
// bandit arms: genre bucket ("gb") and source ("sb"); reward = listened/finished/liked, penalty = skip/ban
function reward(t, w) {
  P.gb = P.gb || {}; P.sb = P.sb || {}; P.dp = P.dp || {};
  const up = (table, key) => {
    const s = table[key] || { a: 0, b: 0 };
    if (w > 0) s.a += Math.min(1.5, w / 2); else s.b += Math.min(1.5, -w / 2);
    // keep arms adaptive: old evidence fades so tastes can change
    const n = s.a + s.b;
    if (n > 60) { s.a *= 60 / n; s.b *= 60 / n; }
    table[key] = s;
  };
  up(P.gb, bucketOf(t));
  if (t.via) up(P.sb, t.via);
  const dp = (P.dp[daypartOf()] = P.dp[daypartOf()] || {});
  dp[bucketOf(t)] = clamp((dp[bucketOf(t)] || 0) + w * 0.5);
}

export function save() {
  P.updated = Date.now();
  try { localStorage.setItem(KEY, JSON.stringify(P)); } catch {}
  onChange(P);
}
export function replaceProfile(data) { P = { ...blank(), ...data }; decay(); try { localStorage.setItem(KEY, JSON.stringify(P)); } catch {} }
export function resetProfile() { const keep = { likes: P.likes, playlists: P.playlists }; P = { ...blank(), ...keep }; session.length = 0; save(); }

const clamp = (v) => Math.max(-25, Math.min(40, v));
function bump(map, k, v) { if (k) P[map][k] = clamp((P[map][k] || 0) + v); }

export function learn(t, w) {
  decay();
  reward(t, w);
  bump("g", t.genre, w);
  bump("m", t.mood, w * 0.7);
  bump("a", t.artistId, w * 0.8);
  if (t.artistId) P.an[t.artistId] = t.artist;
  (t.tags || []).slice(0, 4).forEach((tag) => bump("t", tag, w * 0.3));
  save();
}
const KEEP = ["catalog", "extLink", "extName", "via", "channel", "link", "views", "id", "src", "rawId", "title", "artist", "artistId", "cover", "coverSm", "duration", "genre", "mood", "tags", "bpm", "key", "releaseDate", "quality", "downloadable", "ext", "stream", "dlOrig", "dlMp3", "instrumental"];
export const slim = (t) => Object.fromEntries(KEEP.filter((k) => t[k] !== undefined && t[k] !== null && t[k] !== "").map((k) => [k, k === "tags" ? t.tags.slice(0, 6) : t[k]]));

// ---------- session: the instant reaction ----------
// Last 8 tracks of this listening session with a +/- verdict. Two skips of a genre push it down at once.
const session = [];
let sessionAt = 0;
function sessionPush(t, v) {
  if (Date.now() - sessionAt > 30 * 60e3) session.length = 0;
  sessionAt = Date.now();
  session.push({ id: t.id, genre: t.genre, mood: t.mood, artistId: t.artistId, bpm: t.bpm, key: t.key, v });
  if (session.length > 8) session.shift();
}
function sessionScore(t) {
  if (Date.now() - sessionAt > 30 * 60e3) return 0;
  let s = 0;
  for (const e of session) {
    if (e.genre && e.genre === t.genre) s += e.v;
    if (e.mood && e.mood === t.mood) s += e.v * 0.6;
    if (e.artistId && e.artistId === t.artistId) s += e.v * 0.8;
  }
  return s * 1.5;
}
const playedThisSession = new Set();
export const playedInSession = (id) => playedThisSession.has(id);
export const sessionScoreOf = (t) => sessionScore(t);
export const sessionInfo = () => session.map((e) => ({ ...e }));

// ---------- events ----------
export function like(t) {
  if (P.likes[t.id]) { delete P.likes[t.id]; learn(t, -2); return false; }
  P.likes[t.id] = { ...slim(t), likedAt: Date.now() };
  sessionPush(t, 1.5);
  learn(t, 3);
  return true;
}
export const isLiked = (t) => !!(t && P.likes[t.id]);
// add without toggling (imports): returns false when it was already liked
export function addLike(t) {
  if (P.likes[t.id]) return false;
  P.likes[t.id] = { ...slim(t), likedAt: Date.now() };
  bump("g", t.genre, 1.5); bump("a", t.artistId, 1.5); if (t.artistId) P.an[t.artistId] = t.artist;
  return true;
}
// ---- own playlists ----
export function createPlaylist(title, tracks = [], desc = "") {
  const id = Date.now().toString(36) + Math.random().toString(36).slice(2, 6);
  P.playlists = [{ id, title: (title || "Новый плейлист").slice(0, 80), desc, tracks: tracks.slice(0, 1000).map(slim), at: Date.now() }, ...P.playlists].slice(0, 60);
  save();
  return id;
}
export const playlistById = (id) => P.playlists.find((x) => x.id === id);
export function addToPlaylist(id, t) {
  const pl = playlistById(id); if (!pl) return false;
  if (pl.tracks.some((x) => x.id === t.id)) return false;
  pl.tracks.push(slim(t)); pl.at = Date.now(); save(); return true;
}
export function removeFromPlaylist(id, trackId) {
  const pl = playlistById(id); if (!pl) return;
  pl.tracks = pl.tracks.filter((x) => x.id !== trackId); save();
}
export function renamePlaylist(id, title) { const pl = playlistById(id); if (pl && title.trim()) { pl.title = title.trim().slice(0, 80); save(); } }
export function deletePlaylist(id) { P.playlists = P.playlists.filter((x) => x.id !== id); save(); }
export function ban(t) {
  P.ban[t.id] = 1;
  if (t.artistId) {
    P.banCount[t.artistId] = (P.banCount[t.artistId] || 0) + 1;
    if (P.banCount[t.artistId] >= 2) P.banA[t.artistId] = 1;
  }
  sessionPush(t, -2.5);
  learn(t, -5);
}
// seconds = how long it played before the skip
export function skipped(t, seconds = 0) {
  P.skip[t.id] = Date.now();
  sessionPush(t, -1.5);
  learn(t, seconds < 10 ? -1.5 : -0.8);
}
export function finished(t) { sessionPush(t, 1); learn(t, 1.5); }
export function listened(t) {
  playedThisSession.add(t.id);
  P.hist = [{ ...slim(t), at: Date.now() }, ...P.hist.filter((h) => h.id !== t.id)].slice(0, 100);
  sessionPush(t, 1);
  learn(t, 0.5);
}
export function started(t) { playedThisSession.add(t.id); }
export function seedGenre(g, on) { bump("g", g, on ? 4 : -4); save(); }
export function addFavArtist(id, name, genre) {
  P.fav[id] = name; P.an[id] = name; bump("a", id, 8); if (genre) bump("g", genre, 2); delete P.banA[id]; save();
}
export function removeFavArtist(id) { delete P.fav[id]; bump("a", id, -8); save(); }
export function clearProfile() { P = blank(); session.length = 0; playedThisSession.clear(); try { localStorage.removeItem(KEY); } catch {} }

// Combine the profile stored on the server with the one in this browser: nothing liked or saved is lost.
export function mergeProfile(server) {
  if (!server) return;
  const base = (server.updated || 0) > (P.updated || 0) ? { ...blank(), ...server } : { ...P };
  const other = base === P ? { ...blank(), ...server } : P;
  base.likes = { ...other.likes, ...base.likes };
  base.fav = { ...(other.fav || {}), ...(base.fav || {}) };
  base.an = { ...(other.an || {}), ...(base.an || {}) };
  base.ban = { ...(other.ban || {}), ...(base.ban || {}) };
  base.banA = { ...(other.banA || {}), ...(base.banA || {}) };
  base.served = { ...(other.served || {}), ...(base.served || {}) };
  for (const tb of ["gb", "sb"]) { base[tb] = base[tb] || {}; for (const [k, v] of Object.entries(other[tb] || {})) if (!(k in base[tb])) base[tb][k] = v; }
  const ids = new Set(base.playlists.map((x) => x.id));
  base.playlists = [...base.playlists, ...(other.playlists || []).filter((x) => !ids.has(x.id))].slice(0, 40);
  const seen = new Set();
  base.hist = [...base.hist, ...(other.hist || [])].sort((a, b) => b.at - a.at).filter((h) => (seen.has(h.id) ? false : seen.add(h.id))).slice(0, 120);
  for (const map of ["g", "m", "a", "t"]) for (const [k, v] of Object.entries(other[map] || {})) if (!(k in base[map])) base[map][k] = v;
  P = base; decay(); save();
}

const top = (map, n, min = 0.5) => Object.entries(P[map]).filter(([, v]) => v > min).sort((a, b) => b[1] - a[1]).slice(0, n).map(([k]) => k);
export const topGenres = (n = 3) => top("g", n);
export const topArtists = (n = 3) => top("a", n, 1.5).filter((a) => !P.banA[a]);
export const topTags = (n = 3) => top("t", n, 0.8);
export const topMoods = (n = 3) => top("m", n);
export const hasTaste = () => topGenres(1).length > 0 || Object.keys(P.likes).length > 0 || Object.keys(P.fav || {}).length > 0;
export const tasteBars = (map, n = 6) => Object.entries(P[map]).filter(([, v]) => v > 0).sort((a, b) => b[1] - a[1]).slice(0, n);
// a genre the listener has never touched: one exploration source per pool
export function unexploredGenre() {
  const fresh = ALL_GENRES.filter((g) => !(g in P.g));
  return fresh.length ? fresh[Math.floor(Math.random() * fresh.length)] : null;
}
// liked tracks to build "similar to what you love" sources from
export function likeSeeds(n = 3) {
  const all = Object.values(P.likes);
  for (let i = all.length - 1; i > 0; i--) { const j = Math.floor(Math.random() * (i + 1)); [all[i], all[j]] = [all[j], all[i]]; }
  return all.slice(0, n);
}

// ---------- flow: tempo and harmonic key (Camelot wheel) ----------
const NOTE = { C: 0, "C#": 1, Db: 1, D: 2, "D#": 3, Eb: 3, E: 4, F: 5, "F#": 6, Gb: 6, G: 7, "G#": 8, Ab: 8, A: 9, "A#": 10, Bb: 10, B: 11 };
const MAJOR = [8, 3, 10, 5, 12, 7, 2, 9, 4, 11, 6, 1]; // camelot number by pitch class, "B" ring
const MINOR = [5, 12, 7, 2, 9, 4, 11, 6, 1, 8, 3, 10]; // "A" ring
export function camelot(key) {
  const m = /^([A-G][#b]?)\s*(major|minor|maj|min|m)?/i.exec((key || "").trim());
  if (!m || !(m[1] in NOTE)) return null;
  const minor = /^(minor|min|m)$/i.test(m[2] || "");
  return { n: (minor ? MINOR : MAJOR)[NOTE[m[1]]], minor };
}
export function keysCompatible(a, b) {
  const x = camelot(a), y = camelot(b);
  if (!x || !y) return false;
  if (x.n === y.n) return true;
  const d = Math.abs(x.n - y.n);
  return x.minor === y.minor && (d === 1 || d === 11);
}
function flow(t, prev) {
  if (!prev) return 0;
  let s = 0;
  if (prev.bpm && t.bpm) {
    const d = Math.min(Math.abs(prev.bpm - t.bpm), Math.abs(prev.bpm - t.bpm * 2), Math.abs(prev.bpm * 2 - t.bpm));
    s -= Math.min(d, 40) / 10;
  }
  if (prev.key && t.key && keysCompatible(prev.key, t.key)) s += 1.2;
  return s;
}

// ---------- score ----------
export function score(t, ctx = {}) {
  if (P.ban[t.id] || (t.artistId && P.banA[t.artistId])) return -1e9;
  const cfg = { ...WAVE_DEFAULTS, ...(ctx.cfg || {}) };
  if (cfg.onlyDl && !t.downloadable) return -1e9;
  const fam = 1.5 - cfg.novelty / 100; // 1.5 = stick to what you love, 0.5 = mostly new
  let s = ((P.g[t.genre] || 0) + (P.m[t.mood] || 0) * 0.8 + (P.a[t.artistId] || 0) * 0.9) * fam;
  for (const tag of t.tags || []) s += (P.t[tag] || 0) * 0.3 * fam;
  s += sessionScore(t);
  s += Math.log10((t.plays || 0) + 10) * (0.5 + (cfg.popularity - 50) / 30);
  s += flow(t, ctx.prev);
  if (t.releaseDate) {
    const age = Date.now() - Date.parse(t.releaseDate);
    if (age >= 0 && age < 30 * DAY) s += 1.5 * cfg.novelty / 100;
  }
  if (P.likes[t.id]) s += cfg.novelty < 40 ? 2.5 : cfg.novelty > 70 ? -4 : 0;
  const text = `${t.title} ${(t.tags || []).join(" ")}`.toLowerCase();
  const range = TEMPO[cfg.tempo];
  if (range && t.bpm) s += t.bpm >= range[0] && t.bpm < range[1] ? 3 : -4;
  const instr = t.instrumental || /instrumental|lofi|lo-fi|ambient|beat/.test(text);
  if (cfg.vocals === "instrumental") s += instr ? 3 : -2.5;
  if (cfg.vocals === "vocal") s += instr ? -3 : 1;
  const pre = ctx.mood && MOOD_PRESETS[ctx.mood];
  if (pre) {
    if (pre.genres.includes(t.genre)) s += pre.tags ? 1.5 : 4;
    if (pre.moods.includes(t.mood)) s += 3;
    if (pre.tags && pre.tags.some((x) => text.includes(x))) s += 9;
    else if (pre.tags) s -= 4;
    if (t.bpm) s += t.bpm >= pre.bpm[0] && t.bpm <= pre.bpm[1] ? 2.5 : -2;
  }
  if (ctx.plan) {
    const pl = ctx.plan;
    if (pl.genres?.includes(t.genre)) s += 4;
    if (pl.moods?.includes(t.mood)) s += 3;
    if (pl.bpm_min && t.bpm) s += t.bpm >= pl.bpm_min && t.bpm <= (pl.bpm_max || 999) ? 2.5 : -2.5;
    if (pl.instrumental && t.instrumental) s += 2;
  }
  const h = P.hist.find((x) => x.id === t.id);
  if (h && Date.now() - h.at < 3 * DAY) s -= 6;
  if (playedThisSession.has(t.id)) s -= 20;
  if (P.skip[t.id]) s -= 3;
  s += (Math.random() - 0.5) * (ctx.explore ?? (0.6 + cfg.novelty / 100 * 4.5));
  return s;
}

// ---------- re-rank ----------
// Greedy pick with variety: no artist within the last 8 queued, ≤ perArtist per batch, genre streaks of 3 get pushed
// down, and every 5th slot goes to a discovery track (a genre the listener barely knows) when there is one.
export function pick(cands, n, ctx = {}, perArtist = 2) {
  const ranked = cands.map((t) => ({ t, s: score(t, ctx) })).filter((x) => x.s > -1e8).sort((a, b) => b.s - a.s);
  const out = [], cnt = {};
  const recentArtists = [...(ctx.recent || []).map((t) => t.artistId), ctx.lastArtist].filter(Boolean).slice(-8);
  const isDiscovery = (t) => t.genre && Math.abs(P.g[t.genre] || 0) < 1;
  let prev = ctx.prev;
  const used = new Set();
  while (out.length < n) {
    const slot = out.length + 1;
    const lastGenres = out.slice(-3).map((t) => t.genre);
    const streak = lastGenres.length === 3 && lastGenres.every((g) => g && g === lastGenres[0]) ? lastGenres[0] : null;
    let best = null, bestS = -Infinity;
    for (const x of ranked) {
      if (used.has(x.t.id)) continue;
      const t = x.t;
      if (recentArtists.includes(t.artistId) || out.slice(-8).some((o) => o.artistId === t.artistId) || (cnt[t.artistId] || 0) >= perArtist) continue;
      let s = x.s + flow(t, prev) - flow(t, ctx.prev); // re-evaluate flow against the track that will actually precede it
      if (streak && t.genre === streak) s -= 2;
      if (slot % 5 === 0 && isDiscovery(t)) s += 6;
      if (s > bestS) { bestS = s; best = x; }
    }
    if (!best) break;
    used.add(best.t.id);
    out.push(best.t);
    cnt[best.t.artistId] = (cnt[best.t.artistId] || 0) + 1;
    prev = best.t;
  }
  return out;
}
