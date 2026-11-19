// Music sources: Audius (no key), Jamendo (free client id), and the site's own uploads.
const AU = "/api/au"; // proxied through our server: Audius is blocked in some networks
let JON = false, YON = false;
export const setYouTube = (on) => { YON = !!on; };
let SON = false;
export const setSpotify = (on) => { SON = !!on; };
export const hasSpotify = () => SON;
// Apple Music / Spotify search results: only names and covers come from there. The sound is a full version found
// on Audius, Jamendo, the site's uploads or the official YouTube player, resolved when the track starts.
export const CATALOG_NAME = { apple: "Apple Music", spotify: "Spotify" };
export function fromCatalog(r, src) {
  const cover = r.cover ? `/api/img?u=${encodeURIComponent(r.cover)}` : "";
  return {
    id: `${src}:${r.id}`, src, catalog: true, rawId: r.id, title: r.title, artist: r.artist,
    artistId: "cat:" + (r.artist || "").split(",")[0].trim().toLowerCase(), cover, coverSm: cover, duration: r.duration || 0,
    genre: r.genre || r.album || "", mood: "", tags: [], bpm: null, plays: r.popularity || 0, downloadable: false,
    quality: CATALOG_NAME[src], stream: "", link: r.link, extLink: r.link, extName: CATALOG_NAME[src],
  };
}
const storeCountry = () => {
  const lang = (navigator.language || "en-US").split("-");
  const cc = (lang[1] || "").toLowerCase();
  return /^[a-z]{2}$/.test(cc) ? cc : lang[0] === "ru" ? "ru" : "us";
};
export async function catalogSearch(src, q) {
  const d = await getJSON(`/api/${src}/search?q=${encodeURIComponent(q)}&cc=${storeCountry()}`, {}, 30 * 60e3);
  return (d.data || []).map((r) => fromCatalog(r, src));
}
export const hasYouTube = () => YON;
// YouTube: listening only through the official embedded player. No stream extraction, no downloads.
export function fromYouTube(v, hint = {}) {
  return {
    id: "yt:" + v.videoId, src: "youtube", rawId: v.videoId,
    title: hint.title || v.track || v.title, artist: hint.artist || v.artist || v.channel, artistId: "yt:" + (hint.artist || v.artist || v.channel || "").split(",")[0].trim().toLowerCase(),
    channel: v.channel, cover: `/api/yt/thumb/${v.videoId}`, coverSm: `/api/yt/thumb/${v.videoId}`, duration: 0, genre: "", mood: "", tags: [],
    bpm: null, plays: 0, downloadable: false, quality: "YouTube Music", stream: "", link: `https://www.youtube.com/watch?v=${v.videoId}`,
  };
}
// A YouTube hit from the public charts: full song, played only through the official embedded player.
const LATIN = new Set(["mexico", "argentina", "colombia", "chile", "peru", "spain", "brazil", "puerto rico"]);
export function fromYouTubeCatalog(v, list) {
  const artist = v.artist || "YouTube";
  const regions = (v.regions || []).map((r) => r.trim().toLowerCase());
  return {
    id: "yt:" + v.videoId, src: "youtube", rawId: v.videoId, title: v.title, artist,
    artistId: "yt:" + artist.split(",")[0].trim().toLowerCase(), channel: "YouTube",
    cover: `/api/yt/thumb/${v.videoId}`, coverSm: `/api/yt/thumb/${v.videoId}`, duration: 0, genre: "", mood: "",
    tags: ["youtube", list === "top" ? "classic hit" : "hit", ...(regions.some((r) => LATIN.has(r)) ? ["latin"] : []), ...regions.slice(0, 3)],
    bpm: null, plays: v.views || 0, views: v.views || 0, downloadable: false, quality: "YouTube",
    stream: "", link: `https://www.youtube.com/watch?v=${v.videoId}`,
  };
}
export const spotifySearchUrl = (t) => `https://open.spotify.com/search/${encodeURIComponent(`${t.artist} ${t.title}`.trim())}`;
export const importLink = (url) => api(`/api/import?url=${encodeURIComponent(url)}`);
export const reportBadYouTube = (id) => api("/api/yt/bad", { method: "POST", body: { id } }).catch(() => {});
export async function ytSearch(q) {
  if (!YON) return [];
  return ((await getJSON(`/api/yt/search?q=${encodeURIComponent(q)}`, {}, 60 * 60e3)).data || []);
}
export const setJamendo = (on) => { JON = !!on; };
export const hasJamendo = () => JON;

const EXCLUDE = new Set(["Podcasts", "Spoken Word", "Comedy", "Audiobooks", "Kids", "Devotional"]);
const cache = new Map();

async function getJSON(url, opt = {}, ttl = 0) {
  if (ttl && cache.has(url)) {
    const c = cache.get(url);
    if (Date.now() - c.t < ttl) return c.v;
  }
  const ctl = new AbortController();
  const timer = setTimeout(() => ctl.abort(), 15000);
  try {
    const r = await fetch(url, { ...opt, signal: ctl.signal });
    const v = await r.json().catch(() => ({}));
    if (!r.ok) throw Object.assign(new Error(v.error || `HTTP ${r.status}`), { status: r.status });
    if (ttl) cache.set(url, { t: Date.now(), v });
    return v;
  } finally {
    clearTimeout(timer);
  }
}

// ---- own server ----
export function api(path, { method = "GET", body, headers = {}, raw } = {}) {
  const h = { "X-Echo": "1", ...headers };
  let b = body;
  if (body !== undefined && !raw) { h["Content-Type"] = "application/json"; b = JSON.stringify(body); }
  return getJSON(path, { method, body: b, headers: h, credentials: "same-origin" });
}

// ---- Audius ----
const g0 = (g) => (g || "").split(",")[0].trim();
function fromAudius(t) {
  return {
    id: "au:" + t.id, src: "audius", rawId: t.id,
    title: t.title, artist: t.user?.name || "Неизвестный артист", artistId: "au:" + (t.user?.id || ""),
    cover: t.artwork ? `${AU}/art/${t.id}/480` : "",
    coverSm: t.artwork ? `${AU}/art/${t.id}/150` : "",
    duration: t.duration || 0, genre: g0(t.genre), mood: t.mood || "",
    tags: (t.tags || "").split(",").map((s) => s.trim().toLowerCase()).filter(Boolean).slice(0, 8),
    bpm: t.bpm || null, key: t.musical_key || "", releaseDate: t.release_date || "", plays: t.play_count || 0,
    downloadable: !!t.is_downloadable, quality: "MP3 320",
    stream: `${AU}/stream/${t.id}`,
    link: t.permalink ? "https://audius.co" + t.permalink : "",
  };
}
const okAudius = (t) => t && t.is_streamable !== false && !t.is_stream_gated && t.duration >= 60 && t.duration <= 1500 && !EXCLUDE.has(g0(t.genre));
const auList = (d) => (d.data || []).filter(okAudius).map(fromAudius);

export async function auTrending({ genre, time = "week", limit = 40, offset = 0 } = {}) {
  const q = new URLSearchParams({ time, limit });
  if (offset) q.set("offset", offset);
  if (genre) q.set("genre", genre);
  return auList(await getJSON(`${AU}/tracks/trending?${q}`, {}, 10 * 60e3));
}
export async function auUnderground(limit = 30) {
  return auList(await getJSON(`${AU}/tracks/trending/underground?limit=${limit}`, {}, 10 * 60e3));
}
export async function auSearch(query, { limit = 30, onlyDl = false } = {}) {
  const q = new URLSearchParams({ query, limit });
  if (onlyDl) q.set("only_downloadable", "true");
  return auList(await getJSON(`${AU}/tracks/search?${q}`, {}, 5 * 60e3));
}
export async function auArtist(artistId, limit = 20) {
  const id = artistId.replace(/^au:/, "");
  if (!id) return [];
  return auList(await getJSON(`${AU}/users/${id}/tracks?limit=${limit}`, {}, 10 * 60e3));
}

// ---- Jamendo ----
export const jTag = (genre) => (genre || "").toLowerCase().replace(/&/g, "and").replace(/[^a-z0-9]/g, "");
function fromJamendo(t) {
  const genres = t.musicinfo?.tags?.genres || [];
  const vartags = t.musicinfo?.tags?.vartags || [];
  return {
    id: "ja:" + t.id, src: "jamendo", rawId: t.id,
    title: t.name, artist: t.artist_name, artistId: "ja:" + t.artist_id,
    cover: `/api/ja/art/${t.id}`, coverSm: `/api/ja/art/${t.id}`,
    duration: +t.duration || 0, genre: genres[0] ? genres[0][0].toUpperCase() + genres[0].slice(1) : "",
    mood: "", tags: [...genres, ...vartags].map((s) => s.toLowerCase()).slice(0, 8),
    bpm: null, plays: 0, instrumental: t.musicinfo?.vocalinstrumental === "instrumental",
    downloadable: !!(t.audiodownload_allowed && t.audiodownload), quality: "FLAC",
    stream: `/api/ja/stream/${t.id}`, dlOrig: `/api/ja/dl/${t.id}`, dlMp3: `/api/ja/stream/${t.id}`, link: t.shareurl || "",
  };
}
async function jamendo(params) {
  if (!JON) return [];
  const q = new URLSearchParams({ limit: 30, ...params });
  const d = await getJSON(`/api/ja/tracks?${q}`, {}, 10 * 60e3);
  return (d.results || []).filter((t) => t.audio && +t.duration >= 60).map(fromJamendo);
}
export const jaSearch = (query, limit = 30) => jamendo({ search: query, limit, order: "relevance" });
export const jaPopular = ({ tags, limit = 30, instrumental, offset = 0, order = "popularity_month" } = {}) =>
  jamendo({ limit, order, ...(offset ? { offset } : {}), ...(tags ? { fuzzytags: tags } : {}), ...(instrumental ? { vocalinstrumental: "instrumental" } : {}) });

// ---- own uploads ----
function fromLocal(u) {
  return {
    id: "lo:" + u.id, src: "local", rawId: u.id, title: u.title, artist: u.artist, artistId: "lo:" + u.artist.toLowerCase(),
    cover: u.cover ? `/media/${u.id}/cover` : "", coverSm: u.cover ? `/media/${u.id}/cover` : "",
    duration: u.duration || 0, genre: u.genre || "", mood: u.mood || "", tags: u.genre ? [u.genre.toLowerCase()] : [],
    bpm: u.bpm || null, plays: 0, downloadable: true, quality: u.ext.toUpperCase(), ext: u.ext,
    stream: `/media/${u.id}`, dlOrig: `/media/${u.id}?download=1`, size: u.size,
  };
}
export async function localTracks(q = "") {
  try { return ((await getJSON(`/api/uploads${q ? "?q=" + encodeURIComponent(q) : ""}`)).data || []).map(fromLocal); } catch { return []; }
}

// ---- combined search ----
export async function searchAll(query, { src = "all", onlyDl = false } = {}) {
  const jobs = [];
  // own uploads first: they are the site's own music and must never drown in catalog results
  if (src === "all" || src === "local") jobs.push(localTracks(query));
  if (src === "all" || src === "audius") jobs.push(auSearch(query, { onlyDl, limit: 40 }));
  if ((src === "all" || src === "jamendo") && JON) jobs.push(jaSearch(query, 30));
  const res = await Promise.allSettled(jobs);
  const failed = res.filter((r) => r.status === "rejected").length;
  let list = res.flatMap((r) => (r.status === "fulfilled" ? r.value : []));
  if (onlyDl) list = list.filter((t) => t.downloadable);
  return { list: rankByQuery(dedupe(list), query), failed: failed === res.length && res.length > 0 };
}

// Exact and whole-word matches first; uploads get a head start. Stable for equal scores.
export function rankByQuery(list, query) {
  const q = query.toLowerCase().trim();
  if (!q) return list;
  const words = q.split(/\s+/);
  const sc = (t) => {
    const title = t.title.toLowerCase().trim(), artist = t.artist.toLowerCase().trim();
    let s = t.src === "local" ? 6 : 0;
    if (title === q || artist === q) s += 10;
    if (title.startsWith(q)) s += 4;
    if (`${artist} ${title}`.includes(q) || `${title} ${artist}`.includes(q)) s += 3;
    s += words.filter((w) => title.includes(w) || artist.includes(w)).length;
    s += Math.log10((t.plays || 0) + 1) * 0.3;
    return s;
  };
  return list.map((t, i) => ({ t, i, s: sc(t) })).sort((a, b) => b.s - a.s || a.i - b.i).map((x) => x.t);
}

export function dedupe(list) {
  const seen = new Set();
  return list.filter((t) => (seen.has(t.id) ? false : (seen.add(t.id), true)));
}

// Saved likes/history may hold URLs from older versions: rebuild them for the current routes.
export function refresh(t) {
  if (!t) return t;
  if (t.src === "audius" && t.rawId) {
    t.stream = `${AU}/stream/${t.rawId}`;
    if (t.cover && !t.cover.startsWith("/")) { t.cover = `${AU}/art/${t.rawId}/480`; t.coverSm = `${AU}/art/${t.rawId}/150`; }
  }
  if (t.src === "jamendo" && t.rawId) { t.stream = t.dlMp3 = `/api/ja/stream/${t.rawId}`; t.dlOrig = `/api/ja/dl/${t.rawId}`; t.cover = t.coverSm = `/api/ja/art/${t.rawId}`; }
  return t;
}
