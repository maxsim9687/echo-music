// «Моя волна 3» — candidate generation, ranking and re-ranking.
//
// Why the old wave repeated itself: it always pulled the same trending lists, remembered only what was *played*
// (after 30 s) and forgot everything on reload. This engine fixes that with:
//   1. a wide, randomised candidate pool (≈15 sources, random query seeds, random offsets, ~3000 YouTube hits);
//   2. a persistent "served" memory: anything already queued is pushed far down for 1–14 days, artists get fatigue;
//   3. Thompson-sampling bandits over genres and over sources, so the wave keeps exploring but learns what works;
//   4. a taste vector (genres, moods, tags) compared with each track by cosine similarity, not just exact keys;
//   5. listening context: time of day and the current session (two skips of a genre push it down at once);
//   6. MMR re-ranking for variety plus quotas: familiar vs new, discovery slots, a cap on any single source.
// Every picked track carries `reason` (shown in the UI) and `via` (which source found it, used for learning).
import * as A from "./api.js?v=17";
import * as R from "./recs.js?v=17";

const DAY = 86400e3;
const rnd = (a, b) => a + Math.random() * (b - a);
const pickOne = (arr) => arr[Math.floor(Math.random() * arr.length)];
function shuffle(arr) {
  const a = arr.slice();
  for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(Math.random() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; }
  return a;
}
function sampleN(arr, n) { return shuffle(arr).slice(0, n); }

// ---------------------------------------------------------------- vocabulary
// Several search seeds per genre: a random two of them are used each time, so the same genre never yields
// the same candidate list twice in a row.
export const GENRE_QUERIES = {
  "Electronic": ["electronic", "edm", "synth", "electro", "melodic techno", "progressive", "idm", "future garage"],
  "Hip-Hop/Rap": ["hip hop", "rap", "boom bap", "trap beat", "drill", "lofi hip hop", "underground rap", "cloud rap"],
  "Lo-Fi": ["lofi", "lofi beats", "chillhop", "study beats", "jazzhop", "lofi piano", "rainy lofi", "sleepy lofi"],
  "House": ["house", "deep house", "tech house", "afro house", "melodic house", "garage house", "disco house", "minimal house"],
  "Deep House": ["deep house", "organic house", "melodic deep", "lounge house", "nu disco"],
  "Techno": ["techno", "melodic techno", "hard techno", "minimal techno", "industrial techno", "dub techno"],
  "Drum & Bass": ["drum and bass", "liquid dnb", "neurofunk", "jungle", "dnb", "jump up"],
  "Ambient": ["ambient", "drone", "atmospheric", "space ambient", "dark ambient", "meditation"],
  "Pop": ["pop", "indie pop", "synthpop", "dream pop", "bedroom pop", "electropop"],
  "Rock": ["rock", "indie rock", "alternative rock", "post rock", "garage rock", "shoegaze"],
  "Alternative": ["alternative", "indie", "emo", "post punk", "grunge", "alt rock"],
  "Jazz": ["jazz", "nu jazz", "smooth jazz", "jazz fusion", "lofi jazz"],
  "R&B/Soul": ["rnb", "soul", "neo soul", "alt rnb", "slow jams"],
  "Classical": ["classical", "piano", "orchestral", "neoclassical", "cinematic"],
  "Trap": ["trap", "phonk", "dark trap", "808", "rage beat"],
  "Dubstep": ["dubstep", "riddim", "melodic dubstep", "brostep"],
  "Trance": ["trance", "uplifting trance", "psytrance", "progressive trance"],
  "Downtempo": ["downtempo", "trip hop", "chillout", "chill electronic"],
  "Synthwave": ["synthwave", "retrowave", "darksynth", "outrun", "vaporwave"],
  "Acoustic": ["acoustic", "guitar", "singer songwriter", "folk acoustic"],
  "Experimental": ["experimental", "glitch", "noise", "avant garde"],
  "Soundtrack": ["soundtrack", "film score", "epic", "game music"],
  "Funk": ["funk", "nu funk", "future funk", "disco"],
  "Metal": ["metal", "metalcore", "djent", "doom"],
  "Future Bass": ["future bass", "kawaii future bass", "melodic bass"],
  "Hyperpop": ["hyperpop", "digicore", "glitchcore"],
  "Latin": ["latin", "reggaeton", "dembow", "latin trap"],
};
const DISCOVERY_POOL = Object.keys(GENRE_QUERIES);
const YT_KEY = "Мейнстрим"; // genre bucket for YouTube hits (they have no genre metadata)
export const genreKey = (t) => t.genre || (t.src === "youtube" ? YT_KEY : "Другое");

// ---------------------------------------------------------------- context
export function daypart(d = new Date()) {
  const h = d.getHours();
  return h < 6 ? "night" : h < 11 ? "morning" : h < 17 ? "day" : h < 22 ? "evening" : "night";
}

// ---------------------------------------------------------------- Thompson sampling
// Beta(a, b) sample via two Gamma samples (Marsaglia–Tsang). Arms start at Beta(1, 1) = uniform.
function gamma(k) {
  if (k < 1) return gamma(k + 1) * Math.pow(Math.random(), 1 / k);
  const d = k - 1 / 3, c = 1 / Math.sqrt(9 * d);
  for (;;) {
    let x, v;
    do {
      const u1 = Math.random() || 1e-9, u2 = Math.random();
      x = Math.sqrt(-2 * Math.log(u1)) * Math.cos(2 * Math.PI * u2);
      v = 1 + c * x;
    } while (v <= 0);
    v = v * v * v;
    const u = Math.random();
    if (u < 1 - 0.0331 * x ** 4 || Math.log(u) < 0.5 * x * x + d * (1 - v + Math.log(v))) return d * v;
  }
}
function betaSample(a, b) { const x = gamma(a), y = gamma(b); return x / (x + y); }
const armCache = new Map();
function arm(table, key) {
  // one sample per arm per ranking pass, so all tracks of a genre share the same optimism this round
  const k = table + ":" + key;
  if (!armCache.has(k)) {
    const s = (R.P[table] || {})[key] || { a: 0, b: 0 };
    armCache.set(k, betaSample(1 + s.a, 1 + s.b));
  }
  return armCache.get(k);
}
export function banditStats(table) {
  return Object.entries(R.P[table] || {}).map(([k, s]) => ({ k, n: s.a + s.b, rate: (s.a + 1) / (s.a + s.b + 2) }))
    .sort((x, y) => y.rate - x.rate);
}

// ---------------------------------------------------------------- taste vector
function features(t) {
  const f = new Set();
  if (t.genre) f.add("g:" + t.genre.toLowerCase());
  if (t.mood) f.add("m:" + t.mood.toLowerCase());
  for (const tag of (t.tags || []).slice(0, 8)) f.add("t:" + tag.toLowerCase());
  return f;
}
let userVec = null;
function buildUserVec() {
  const v = new Map();
  const add = (k, w) => { if (w > 0) v.set(k, (v.get(k) || 0) + w); };
  for (const [g, w] of Object.entries(R.P.g)) add("g:" + g.toLowerCase(), w);
  for (const [m, w] of Object.entries(R.P.m)) add("m:" + m.toLowerCase(), w * 0.8);
  for (const [t, w] of Object.entries(R.P.t)) add("t:" + t.toLowerCase(), w);
  // liked tracks count directly: their features are what the listener chose
  for (const t of Object.values(R.P.likes)) for (const f of features(t)) add(f, 0.6);
  let norm = 0;
  for (const w of v.values()) norm += w * w;
  userVec = { v, norm: Math.sqrt(norm) || 1 };
}
function cosine(t) {
  if (!userVec) buildUserVec();
  const f = features(t);
  if (!f.size) return 0;
  let dot = 0;
  for (const k of f) dot += userVec.v.get(k) || 0;
  return dot / (userVec.norm * Math.sqrt(f.size));
}
function similarity(a, b) {
  if (a.artistId && a.artistId === b.artistId) return 1;
  let s = a.genre && a.genre === b.genre ? 0.4 : 0;
  const fa = features(a), fb = features(b);
  let inter = 0;
  for (const k of fa) if (fb.has(k)) inter++;
  const uni = fa.size + fb.size - inter;
  return Math.min(1, s + (uni ? (inter / uni) * 0.6 : 0));
}

// ---------------------------------------------------------------- served memory
export function servedPenalty(t) {
  const at = (R.P.served || {})[t.id];
  let p = 0;
  if (at) {
    const age = Date.now() - at;
    p += age < DAY ? 14 : age < 3 * DAY ? 8 : age < 7 * DAY ? 4 : age < 14 * DAY ? 1.5 : 0;
  }
  const fat = (R.P.servedA || {})[t.artistId];
  if (fat) {
    const hrs = (Date.now() - fat.at) / 3600e3;
    p += fat.n * Math.pow(0.5, hrs / 12) * 1.2; // artist fatigue halves every 12 h
  }
  return p;
}

// ---------------------------------------------------------------- candidate sources
function tagVia(list, via) { for (const t of list) t.via = t.via || via; return list; }
const ytCache = {};
// The public YouTube charts are full of regional film songs, "lyrical" uploads and channel compilations.
// Keep songs in Latin or Cyrillic script that look like a single track.
const CATALOG_JUNK = /full video|full song|video song|lyrical|jukebox|#|\bstatus\b|songs? 20\d\d|new .*songs|haryanvi|bhojpuri|punjabi|bollywood|tollywood|kollywood|telugu|tamil|nonstop|\bmix\b|\b(?:top|best) \d+/i;
const FOREIGN_SCRIPT = /[؀-ۿऀ-෿฀-๿က-႟]/;
async function ytCatalog(name) {
  if (ytCache[name] && Date.now() - ytCache[name].at < 3 * 3600e3) return ytCache[name].list;
  const d = await A.api(`/api/yt/catalog?list=${name}`);
  const list = (d.data || []).filter((v) => v.artist && v.title && !CATALOG_JUNK.test(`${v.artist} ${v.title}`) && !FOREIGN_SCRIPT.test(`${v.artist} ${v.title}`))
    .map((v) => A.fromYouTubeCatalog(v, name));
  ytCache[name] = { at: Date.now(), list };
  return list;
}
export { ytCatalog };

// ---------------------------------------------------------------- mood map
// Every track gets an estimate on two axes, valence (sad → happy) and energy (calm → intense), from the Audius
// mood tag, tempo, genre and words in the title. Tracks with nothing to go on get null.
const MOOD_VE = {
  Energizing: [0.7, 0.9], Upbeat: [0.85, 0.75], Excited: [0.85, 0.9], Fiery: [0.45, 0.95], Empowering: [0.75, 0.8], Rowdy: [0.65, 0.9],
  Stirring: [0.6, 0.7], Peaceful: [0.6, 0.12], Easygoing: [0.7, 0.35], Tender: [0.55, 0.2], Cool: [0.55, 0.45], Sophisticated: [0.55, 0.4],
  Serious: [0.35, 0.5], Brooding: [0.2, 0.45], Sensual: [0.55, 0.4], Romantic: [0.7, 0.35], Melancholy: [0.12, 0.25], Sentimental: [0.3, 0.25],
  Yearning: [0.25, 0.35], Aggressive: [0.25, 0.95], Defiant: [0.35, 0.85], Gritty: [0.3, 0.8],
};
const GENRE_E = {
  Ambient: 0.12, "Lo-Fi": 0.3, Classical: 0.25, Acoustic: 0.3, Jazz: 0.35, Folk: 0.3, Downtempo: 0.35, "R&B/Soul": 0.45, Soundtrack: 0.4,
  Pop: 0.62, Alternative: 0.55, Rock: 0.72, "Hip-Hop/Rap": 0.68, Trap: 0.78, House: 0.75, "Deep House": 0.6, "Tech House": 0.8, Techno: 0.85,
  Electronic: 0.7, "Drum & Bass": 0.92, Dubstep: 0.92, Trance: 0.85, Metal: 0.95, Hyperpop: 0.88, Synthwave: 0.6, "Future Bass": 0.75,
  Funk: 0.7, Disco: 0.78, Latin: 0.75, Reggae: 0.5, World: 0.5, Experimental: 0.5,
};
const SAD_RE = /\bsad\b|sadness|cry|crying|tears|lonely|alone|heartbreak|broken|miss you|грус|слёз|слез|одинок|плачу|разлук|тоск|slowed/i;
const HAPPY_RE = /happy|party|dance|summer|sunshine|good vibes|fun\b|celebrat|вечерин|танц|лето|счаст|радост/i;
const CALM_RE = /chill|calm|relax|sleep|lofi|lo-fi|acoustic|piano|ambient|soft|спокой|сон|колыбел/i;
const HYPE_RE = /hype|workout|gym|rage|hard|phonk|bass boost|energy|drift|бешен|качал/i;
export function moodEstimate(t) {
  const text = `${t.title} ${(t.tags || []).join(" ")}`;
  let v = null, e = null, vn = 0, en = 0;
  const addV = (x, w = 1) => { v = (v || 0) + x * w; vn += w; };
  const addE = (x, w = 1) => { e = (e || 0) + x * w; en += w; };
  const mv = MOOD_VE[t.mood];
  if (mv) { addV(mv[0], 2); addE(mv[1], 2); }
  if (t.bpm) addE(Math.max(0, Math.min(1, (t.bpm - 65) / 100)), 1.2);
  if (GENRE_E[t.genre] !== undefined) addE(GENRE_E[t.genre], 0.8);
  if (SAD_RE.test(text)) addV(0.15, 1.5);
  if (HAPPY_RE.test(text)) addV(0.82, 1.2);
  if (CALM_RE.test(text)) addE(0.2, 1);
  if (HYPE_RE.test(text)) addE(0.9, 1);
  if (t.moodVe) { addV(t.moodVe[0], 1.5); addE(t.moodVe[1], 1.5); }
  return { v: vn ? v / vn : null, e: en ? e / en : null };
}
// How well a track fits the chosen preset. YouTube hits count only when they came from that preset's own search
// or are by one of its artists; free-catalog tracks need a tag (phonk), or a genre / mood / tempo match.
export function moodFit(t, mood) {
  const pre = mood && R.MOOD_PRESETS[mood];
  if (!pre) return 1;
  if (t.moodHit === mood) return 2;
  const text = `${t.title} ${t.artist} ${(t.tags || []).join(" ")} ${t.genre || ""}`.toLowerCase();
  if (t.src === "youtube") return pre.ytArtists?.some((a) => (t.artist || "").toLowerCase().includes(a)) ? 2 : 0;
  if (pre.tags) return pre.tags.some((x) => text.includes(x)) ? 2 : 0;
  let m = 0;
  if (pre.genres.includes(t.genre)) m++;
  if (pre.moods.includes(t.mood)) m += 1.5;
  if (t.bpm) m += t.bpm >= pre.bpm[0] && t.bpm <= pre.bpm[1] ? 0.5 : -1;
  const est = moodEstimate(t);
  if (est.v !== null && est.e !== null) m += 1 - (Math.abs(est.v - pre.ve[0]) + Math.abs(est.e - pre.ve[1]));
  else if (est.e !== null) m += 0.5 - Math.abs(est.e - pre.ve[1]);
  if (pre.instrumental && t.instrumental) m += 0.5;
  return m >= 1 ? (m >= 2 ? 2 : 1) : 0;
}
const NOT_SONG = /\b(mix|mixtape|playlist|compilation|hour|hours|1h|2h|nonstop|full album|live stream|радио|сборник|час)\b/i;
// A YouTube search hit is a real song only if it looks like "Artist - Title" or comes from an artist's own
// channel ("… - Topic", VEVO). Shorts, "top 10", radios and "best songs" lists are thrown away.
const YT_JUNK = /#|\btop\s?\d+|\bbest\b|\bsongs\b|\bradio\b|24\/7|\bmix\b|mixtape|playlist|compilation|according|\bhours?\b|nonstop|full album|\breaction\b|tutorial|сборник|подборк|лучшие|\?/i;
export const isSongVideo = (v) => !YT_JUNK.test(v.title || "") && (/\s[-–—]\s/.test(v.title || "") || /-\s*Topic$|VEVO$/i.test(v.channel || ""));
function moodYouTube(mood, pre) {
  const out = [];
  for (const q of sampleN(pre.yt || [], 2)) {
    out.push(A.ytSearch(q).then((vs) => vs.filter(isSongVideo).map((v) => ({ ...A.fromYouTube(v), moodHit: mood, moodVe: pre.ve }))));
  }
  if (pre.ytArtists?.length) {
    out.push(Promise.all(["daily", "top", "trending"].map((n) => ytCatalog(n).catch(() => []))).then((ls) => ls.flat()
      .filter((t) => pre.ytArtists.some((a) => (t.artist || "").toLowerCase().includes(a))).map((t) => ({ ...t, moodHit: mood, moodVe: pre.ve }))));
  }
  return out;
}
export function sources(mood, cfg = {}) {
  const pre = mood && R.MOOD_PRESETS[mood];
  const jobs = [];
  const add = (p, via) => jobs.push(p.then((l) => tagVia(l, via)).catch(() => []));
  const topG = R.topGenres(4), topA = R.topArtists(5), topT = R.topTags(6);
  const off = () => pickOne([0, 0, 20, 40, 60]); // random page so trending lists rotate

  if (pre) {
    sampleN(pre.queries, Math.min(3, pre.queries.length)).forEach((q) => add(A.auSearch(q, { limit: 40 }), "mood"));
    pre.genres.slice(0, 3).forEach((g) => add(A.auTrending({ genre: g, limit: 30, offset: off() }), "mood"));
    for (const g of sampleN(pre.genres, 2)) for (const q of sampleN(GENRE_QUERIES[g] || [], 1)) add(A.auSearch(q, { limit: 25 }), "mood");
  }
  if (!pre?.tags) {
    // taste: the genres you like, each through a random trending page and a random search seed
    for (const g of topG) {
      add(A.auTrending({ genre: g, limit: 30, offset: off() }), "taste");
      for (const q of sampleN(GENRE_QUERIES[g] || [g], 1)) add(A.auSearch(q, { limit: 25 }), "taste");
    }
    for (const a of sampleN(topA.filter((x) => x.startsWith("au:")), 3)) add(A.auArtist(a, 25), "artist");
    for (const tag of sampleN(topT, 3)) add(A.auSearch(tag, { limit: 25 }), "tags");
    // similar to what you love: combine tags of random liked tracks
    for (const seed of R.likeSeeds(4)) {
      const tags = sampleN((seed.tags || []).slice(0, 5), 2).join(" ");
      if (tags) add(A.auSearch(tags, { limit: 20 }), "similar");
      if (seed.genre) add(A.auTrending({ genre: seed.genre, limit: 25, offset: off() }), "similar");
    }
    // general charts, rotated
    add(A.auTrending({ time: pickOne(["week", "month"]), limit: 50, offset: off() }), "charts");
    add(A.auUnderground(40), "underground");
    // exploration: two genres you rarely play, chosen by the genre bandit's optimism
    const unknown = DISCOVERY_POOL.filter((g) => Math.abs(R.P.g[g] || 0) < 1);
    const explore = unknown.map((g) => ({ g, s: arm("gb", g) })).sort((a, b) => b.s - a.s).slice(0, 2).map((x) => x.g);
    for (const g of explore) for (const q of sampleN(GENRE_QUERIES[g], 1)) add(A.auSearch(q, { limit: 20 }), "explore");
  }
  if (pre && cfg.yt !== false && !cfg.onlyDl && A.hasYouTube()) {
    for (const p of moodYouTube(mood, pre)) add(p, "youtube");
  } else if (cfg.yt !== false && !cfg.onlyDl) {
    add(ytCatalog("daily"), "youtube");
    if (Math.random() < 0.7) add(ytCatalog("trending"), "youtube");
    if (Math.random() < 0.5) add(ytCatalog("top"), "youtube");
  }
  if (A.hasJamendo()) {
    const tags = (pre?.tags || (pre ? pre.genres : topG).map(A.jTag)).filter(Boolean).slice(0, 3).join("+");
    add(A.jaPopular({ tags: tags || undefined, limit: 40, offset: pickOne([0, 0, 40, 80]), order: pickOne(["popularity_week", "popularity_month", "releasedate"]), instrumental: cfg.vocals === "instrumental" }), "jamendo");
  }
  add(A.localTracks(), "uploads");
  if ((cfg.novelty ?? 50) < 60) jobs.push(Promise.resolve(tagVia(Object.values(R.P.likes).map((t) => A.refresh({ ...t })), "liked")));
  return jobs;
}

// ---------------------------------------------------------------- scoring
const TEMPO = { slow: [0, 95], mid: [95, 125], fast: [125, 999] };
const NOVEL_VIA = new Set(["explore", "underground", "youtube", "charts"]);

export function score(t, ctx = {}) {
  if (R.P.ban[t.id] || (t.artistId && R.P.banA[t.artistId])) return { s: -1e9 };
  const cfg = { ...R.WAVE_DEFAULTS, ...(ctx.cfg || {}) };
  if (cfg.onlyDl && !t.downloadable) return { s: -1e9 };
  if (cfg.yt === false && t.src === "youtube") return { s: -1e9 };
  const nov = cfg.novelty / 100;
  const fam = 1.5 - nov;
  const why = [];

  // 1. explicit affinity
  const ga = R.P.g[t.genre] || 0, aa = R.P.a[t.artistId] || 0, ma = R.P.m[t.mood] || 0;
  let s = (ga + 0.8 * ma + 0.9 * aa) * fam;
  for (const tag of t.tags || []) s += (R.P.t[tag] || 0) * 0.25 * fam;
  if (aa > 3) why.push(["artist", 3 + aa / 5]);
  if (ga > 3) why.push(["genre", 2 + ga / 8]);

  // 2. taste vector similarity (catches related tags/genres the listener never rated directly)
  const sim = cosine(t);
  s += sim * 9;
  if (sim > 0.45) why.push(["similar", 1.5 + sim * 3]);

  // 3. bandits: optimism about the genre bucket and the source that found the track
  const gb = arm("gb", genreKey(t)), sb = arm("sb", t.via || "other");
  s += (gb - 0.5) * (3 + nov * 4) + (sb - 0.5) * 3;

  // 4. context: time of day and this session
  const dp = (R.P.dp || {})[daypart()] || {};
  s += (dp[genreKey(t)] || 0) * 0.5;
  s += R.sessionScoreOf(t);

  // 5. popularity calibrated by the slider
  s += Math.log10((t.plays || t.views / 50 || 0) + 10) * (0.45 + (cfg.popularity - 50) / 30);
  if (t.src === "youtube") { s += 0.5 + (cfg.popularity - 50) / 25; if (cfg.popularity > 60) why.push(["hit", 2.5]); }

  // 6. flow from the previous track: tempo and harmonic key
  if (ctx.prev) {
    if (ctx.prev.bpm && t.bpm) {
      const d = Math.min(Math.abs(ctx.prev.bpm - t.bpm), Math.abs(ctx.prev.bpm - t.bpm * 2), Math.abs(ctx.prev.bpm * 2 - t.bpm));
      s -= Math.min(d, 40) / 10;
    }
    if (ctx.prev.key && t.key && R.keysCompatible(ctx.prev.key, t.key)) s += 1.2;
  }

  // 7. freshness
  if (t.releaseDate) {
    const age = Date.now() - Date.parse(t.releaseDate);
    if (age >= 0 && age < 30 * DAY) { s += 2 * nov; if (nov > 0.4) why.push(["fresh", 2]); }
  }

  // 8. novelty: unknown artist bonus when the slider asks for new music
  if (!aa && !R.P.likes[t.id]) { s += (nov - 0.4) * 4; if (nov > 0.55 && NOVEL_VIA.has(t.via)) why.push(["new", 1.5 + nov]); }
  if (R.P.likes[t.id]) s += nov < 0.4 ? 2.5 : nov > 0.7 ? -5 : -1;

  // 9. settings and presets
  const text = `${t.title} ${(t.tags || []).join(" ")}`.toLowerCase();
  const range = TEMPO[cfg.tempo];
  if (range && t.bpm) s += t.bpm >= range[0] && t.bpm < range[1] ? 3 : -4;
  if (range && !t.bpm && t.src === "youtube") s -= 1;
  const instr = t.instrumental || /instrumental|lofi|lo-fi|ambient|beat/.test(text);
  if (cfg.vocals === "instrumental") s += instr ? 3 : t.src === "youtube" ? -6 : -2.5;
  if (cfg.vocals === "vocal") s += instr ? -3 : 1;
  const pre = ctx.mood && R.MOOD_PRESETS[ctx.mood];
  if (t.duration > 900 || (t.src !== "youtube" && NOT_SONG.test(t.title || "") && (!t.duration || t.duration > 420))) return { s: -1e9 }; // DJ sets and hour-long mixes
  if (pre) {
    const fit = moodFit(t, ctx.mood);
    if (fit === 0) return { s: -1e9 }; // does not match the chosen mood at all: never in this wave
    s += fit === 2 ? 7 : 3;
    if (pre.genres.includes(t.genre)) s += 1.5;
    if (pre.moods.includes(t.mood)) s += 2;
    if (t.bpm) s += t.bpm >= pre.bpm[0] && t.bpm <= pre.bpm[1] ? 1.5 : -2;
    why.push(["mood", 4]);
  }
  // mood sliders: sad ↔ happy, calm ↔ intense (50 = any)
  const est = (cfg.valence !== 50 || cfg.energy !== 50) ? moodEstimate(t) : null;
  for (const [val, k] of [[cfg.valence ?? 50, "v"], [cfg.energy ?? 50, "e"]]) {
    const strength = Math.abs(val - 50) / 50;
    if (strength < 0.15) continue;
    const x = est[k];
    if (x === null) { s -= strength * (t.moodHit ? 0.5 : 2.5); continue; }
    const d = Math.abs(x - val / 100);
    s += strength * (3.5 - 9 * d);
    if (d < 0.2 && strength > 0.5) why.push(["slider", 2.6]);
  }
  if (ctx.plan) {
    const pl = ctx.plan;
    if (pl.genres?.includes(t.genre)) s += 4;
    if (pl.moods?.includes(t.mood)) s += 3;
    if (pl.bpm_min && t.bpm) s += t.bpm >= pl.bpm_min && t.bpm <= (pl.bpm_max || 999) ? 2.5 : -2.5;
    if (pl.instrumental && t.instrumental) s += 2;
  }
  if (t.via === "uploads") { s += 1.5; why.push(["upload", 2.8]); }

  // 10. repetition control
  s -= servedPenalty(t);
  const h = R.P.hist.find((x) => x.id === t.id);
  if (h && Date.now() - h.at < 3 * DAY) s -= 6;
  if (R.playedInSession(t.id)) s -= 25;
  if (R.P.skip[t.id]) s -= Date.now() - R.P.skip[t.id] < 7 * DAY ? 5 : 2;

  // 11. a little noise so equal scores shuffle
  s += rnd(-0.5, 0.5) * (ctx.explore ?? (0.5 + nov * 3));
  return { s, why };
}

const REASON = {
  artist: (t) => `Вы часто слушаете ${t.artist}`,
  genre: (t) => `Вы любите ${t.genre}`,
  similar: () => "Похоже на ваше любимое",
  hit: () => "Хит на YouTube",
  fresh: () => "Свежий релиз",
  new: () => "Новое для вас",
  mood: () => "Под настроение",
  slider: () => "Под ваши ползунки настроения",
  upload: () => "Ваша музыка",
};
function reasonOf(t, why) {
  if (!why?.length) return t.src === "youtube" ? "Популярно сейчас" : "Открытие волны";
  const [k] = why.slice().sort((a, b) => b[1] - a[1])[0];
  return REASON[k](t);
}

// ---------------------------------------------------------------- re-ranking
// Greedy MMR: value = relevance − λ·(max similarity to what is already in the batch and the recent queue),
// with hard rules (artist spacing, per-artist and per-source caps) and quotas (discovery slots, familiar share).
export function pick(cands, n, ctx = {}) {
  armCache.clear(); userVec = null;
  const cfg = { ...R.WAVE_DEFAULTS, ...(ctx.cfg || {}) };
  const nov = cfg.novelty / 100;
  const scored = [];
  for (const t of cands) {
    const r = score(t, ctx);
    if (r.s > -1e8) scored.push({ t, s: r.s, why: r.why });
  }
  scored.sort((a, b) => b.s - a.s);
  // MMR on the best 300 is plenty and keeps it fast; at least 120 of them are not YouTube hits, so the
  // free catalogs and your uploads can always fill the slots that YouTube's share leaves
  const ytTop = scored.filter((x) => x.t.src === "youtube").slice(0, 180);
  const pool = [...ytTop, ...scored.filter((x) => x.t.src !== "youtube").slice(0, 300 - ytTop.length)].sort((a, b) => b.s - a.s);
  const recent = (ctx.recent || []).filter(Boolean).slice(-8);
  const out = [], perArtist = {}, perSrc = {};
  const lambda = 1.5 + nov * 3;
  const srcCap = Math.max(2, Math.ceil(n * (cfg.yt === false ? 0 : 0.75))); // YouTube hits: at most ~75 % of a batch
  const sliderStrength = Math.max(Math.abs((cfg.valence ?? 50) - 50), Math.abs((cfg.energy ?? 50) - 50)) / 50;
  const familiarWanted = Math.round(n * Math.max(0.15, 0.85 - nov));
  let familiarCount = 0;
  // mainstream share: YouTube hits have no genre/tags, so similarity alone would never pick them.
  // The popularity slider sets how many of them a batch should contain (≈15 % at "андеграунд", ≈55 % at "хиты").
  // with a mood only that mood's own YouTube results count; strong sliders lower the share of untagged hits
  const ytWanted = cfg.yt === false ? 0 : Math.round(n * Math.min(0.8, 0.3 + cfg.popularity / 180) * (ctx.mood ? 0.85 : 1 - sliderStrength * 0.5));
  let ytCount = 0;
  let prev = ctx.prev;
  const used = new Set();
  // "familiar" means a new track by an artist you like; your liked tracks themselves are capped separately,
  // otherwise a big imported likes list fills the whole wave (at novelty 50 at most 2 of 10 are old likes)
  const isLikedT = (t) => !!R.P.likes[t.id];
  const isFamiliar = (t) => (R.P.a[t.artistId] || 0) > 1.5 && !isLikedT(t);
  const likedCap = Math.round(n * Math.max(0, 0.4 - nov * 0.5));
  let likedCount = 0;
  const isDiscovery = (t) => Math.abs(R.P.g[genreKey(t)] || 0) < 1 && !isFamiliar(t);

  while (out.length < n) {
    const slot = out.length + 1;
    const lastGenres = out.slice(-3).map(genreKey);
    const streak = lastGenres.length === 3 && lastGenres.every((g) => g === lastGenres[0]) ? lastGenres[0] : null;
    const needFamiliar = familiarCount < familiarWanted && (n - out.length) <= (familiarWanted - familiarCount);
    const left = n - out.length;
    const ytNeed = ytCount < ytWanted ? (left <= ytWanted - ytCount ? 12 : 3.5) : 0;
    let best = null, bestV = -Infinity;
    for (const x of pool) {
      const t = x.t;
      if (used.has(t.id)) continue;
      const spacing = [...recent, ...out].slice(-8);
      if (spacing.some((o) => o.artistId && o.artistId === t.artistId)) continue;
      if ((perArtist[t.artistId] || 0) >= 2) continue;
      if (t.src === "youtube" && (perSrc.youtube || 0) >= srcCap) continue;
      if (isLikedT(t) && likedCount >= likedCap) continue;
      let v = x.s;
      // flow against the track that will actually precede this one
      if (prev?.bpm && t.bpm) v -= Math.min(Math.abs(prev.bpm - t.bpm), 40) / 14;
      if (prev?.key && t.key && R.keysCompatible(prev.key, t.key)) v += 0.8;
      let maxSim = 0;
      for (const o of [...recent.slice(-4), ...out]) maxSim = Math.max(maxSim, similarity(t, o));
      v -= lambda * maxSim;
      if (streak && genreKey(t) === streak) v -= 3;
      if (slot % 4 === 0 && isDiscovery(t)) v += 5 + nov * 4;
      if (needFamiliar && isFamiliar(t)) v += 6;
      if (ytNeed && t.src === "youtube" && !isLikedT(t) && (!ctx.mood || t.moodHit === ctx.mood)) v += ytNeed;
      if (v > bestV) { bestV = v; best = x; }
    }
    if (!best) break;
    used.add(best.t.id);
    const t = best.t;
    t.reason = reasonOf(t, best.why);
    out.push(t);
    perArtist[t.artistId] = (perArtist[t.artistId] || 0) + 1;
    perSrc[t.src] = (perSrc[t.src] || 0) + 1;
    if (isFamiliar(t)) familiarCount++;
    if (isLikedT(t)) likedCount++;
    if (t.src === "youtube") ytCount++;
    prev = t;
  }
  if (ctx.markServed !== false) R.markServed(out);
  return out;
}

// What the wave thinks of you right now (shown in Settings).
export function explain() {
  return {
    genres: banditStats("gb").slice(0, 6),
    sources: banditStats("sb").slice(0, 8),
    daypart: daypart(),
    served: Object.keys(R.P.served || {}).length,
  };
}
export const SOURCE_NAMES = {
  taste: "ваши жанры", artist: "любимые артисты", tags: "ваши теги", similar: "похожее на лайки", charts: "чарты Audius",
  underground: "андеграунд", explore: "открытия", youtube: "хиты YouTube", jamendo: "Jamendo", uploads: "ваша музыка",
  liked: "любимое", mood: "настроение", other: "другое",
};
