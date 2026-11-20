"""Эхо: the recommender behind «Моя волна». One taste model per listener, kept on the server.

What it learns from
    Every listener action the page reports (POST /api/rec/events): like, unlike, full listen, listen past 30 s,
    skip (with how many seconds it played), «Не то». Each becomes a signed value with a time decay (half-life
    30 days), so the model follows who you are now, not who you were in spring.

What it knows about songs
    Genre, year and explicit flag from Apple Music's catalog (song_meta, filled by the server in the background),
    language from the title's script and words, popularity, length, source.

What it knows about artists
    Deezer's public "related artists" graph, built from millions of listeners: for Billie Eilish it says Lana Del
    Rey, AURORA, Joji. Cached for 30 days, fetched in the background at most one request a second.

How a song is scored (rank)
    1. artist taste      how much you like this artist yourself (decayed sum of your reactions)
    2. artist graph      your taste spread over related artists: liked artist → related (×0.6) → their related
                         (×0.25); dislikes spread too, at half strength
    3. genre / language / era taste
    4. neural network    a small MLP (content features → chance you like it). A global model is trained on all
                         listeners; each listener's copy is fine-tuned on their own reactions. Its voice grows
                         with the number of reactions it has seen.
    5. collaborative     implicit ALS over listeners × songs, only once at least 5 listeners have enough
                         reactions ("listeners like you also play …")
    Each part is weighted by how much evidence it has, and the strongest one becomes the reason shown in the UI.

Candidates (GET /api/rec/candidates)
    Songs of your top related artists that are already playable (YouTube music lists, chart songs matched to
    their videos, songs other listeners played). Top tracks of related artists that are not playable yet go into
    a matching queue the server works through within its YouTube search budget.

Everything heavy (graph fetches, training, ALS) runs in one background thread on a schedule; requests only read
cached state, so a rank call over 400 songs takes milliseconds on the 1-CPU server.
"""
import json
import math
import random
import re
import threading
import time
import urllib.parse
import urllib.request
from collections import defaultdict, deque

import numpy as np

D = {}  # injected by server.py: db, hit_key, yt_list, yt_lists, matched, ua

DAY = 86400
HALF_LIFE = 30 * DAY
KIND_VALUE = {"like": 3.0, "unlike": -2.0, "finish": 1.5, "listen": 0.7, "ban": -5.0, "start": 0.0}

GENRES = ["Pop", "Hip-Hop/Rap", "Rock", "Alternative", "Electronic", "R&B/Soul", "Latin", "K-Pop", "Country",
          "Metal", "Phonk", "Folk", "Jazz", "Classical", "Soundtrack", "Other"]
GENRE_RU = {"Pop": "поп", "Hip-Hop/Rap": "хип-хоп и рэп", "Rock": "рок", "Alternative": "альтернатива",
            "Electronic": "электроника", "R&B/Soul": "R&B и соул", "Latin": "латино", "K-Pop": "K-pop",
            "Country": "кантри", "Metal": "метал", "Phonk": "фонк", "Folk": "фолк и авторская", "Jazz": "джаз",
            "Classical": "классика", "Soundtrack": "саундтреки", "Other": "другое"}
LANGS = ["ru", "en", "es", "pt", "ko", "other"]
LANG_RU = {"ru": "на русском", "en": "на английском", "es": "на испанском", "pt": "на португальском",
           "ko": "K-pop и J-pop", "other": "на других языках"}
ERAS = ["", "classic", "2000s", "2010s", "new"]
ERA_RU = {"classic": "классика до 2000-х", "2000s": "2000-е", "2010s": "2010-е", "new": "новинки"}
SRCS = ["youtube", "chart", "indie", "local"]

_GENRE_MAP = [
    (r"phonk", "Phonk"),
    (r"k-?pop|j-?pop|korean|japan|anime", "K-Pop"),
    (r"latin|latino|reggaet|urbano|mexic|sertanejo|brazil|mpb|bachata|salsa|cumbia|corrido|banda|pagode|forró|samba|regional", "Latin"),
    (r"hip.?hop|rap|trap|drill|grime|хип|рэп", "Hip-Hop/Rap"),
    (r"metal|метал", "Metal"),
    (r"alternative|indie|альтерн|инди|emo|punk", "Alternative"),
    (r"rock|рок|grunge", "Rock"),
    (r"r&b|soul|funk|disco|gospel|соул", "R&B/Soul"),
    (r"dance|electr|house|techno|trance|edm|dubstep|drum|bass|lo-?fi|ambient|synth|downtempo|hardstyle|танц|электр", "Electronic"),
    (r"country|americana|кантри", "Country"),
    (r"folk|singer|songwriter|acoustic|bard|шансон|авторск|фолк", "Folk"),
    (r"jazz|blues|джаз|блюз", "Jazz"),
    (r"classical|orchestr|opera|piano|классик", "Classical"),
    (r"soundtrack|film|tv|game|score|саундтр", "Soundtrack"),
    (r"pop|поп|estrada|эстрад|world|reggae", "Pop"),
]
_GENRE_RE = [(re.compile(p, re.I), g) for p, g in _GENRE_MAP]


def norm_genre(g):
    if not g:
        return ""
    for rx, out in _GENRE_RE:
        if rx.search(g):
            return out
    return "Other"


def nk(s):
    s = re.sub(r"\(.*?\)|\[.*?\]", " ", (s or "").lower())
    s = re.sub(r"feat\.?.*$|ft\..*$", " ", s)
    return re.sub(r"[^a-z0-9а-яё]+", " ", s).strip()


def artist_key(artist):
    """Main artist only: "Miyagi & Эндшпиль feat. Рем Дигга" → "miyagi"."""
    a = re.split(r",|&|\s+x\s+|\s+feat\.?\s|\s+ft\.?\s|\s+и\s+|\s+and\s+|\s+with\s+", artist or "", maxsplit=1, flags=re.I)[0]
    return nk(a)


_RU = re.compile(r"[а-яё]", re.I)
_KO = re.compile(r"[가-힣ㄱ-ㅎぁ-んァ-ン一-龯]")
_PT = re.compile(r"[ãõ]|\b(você|não|saudade|coração)\b", re.I)
_ES = re.compile(r"[ñ¿¡]|\b(el|la|los|las|mi|tu|que|amor|corazón|te quiero|baila|noche|vida|quiero|ella|señorita)\b", re.I)
_ASCII = re.compile(r"^[\x00-\x7F’'“”…–—]*$")


def lang_of(title, artist, tags=()):
    s = f"{title} {artist}"
    if re.search(r"[әғқңөұүһ]", s, re.I):
        return "other"  # Kazakh, not Russian
    if _RU.search(s):
        return "ru"
    if _KO.search(s):
        return "ko"
    if _PT.search(s):
        return "pt"
    if _ES.search(s) or "latin" in (tags or ()):
        return "es"
    if _ASCII.match(s):
        return "en"
    return "other"


def era_of(y):
    if not y:
        return ""
    return "classic" if y < 2000 else "2000s" if y < 2010 else "2010s" if y < 2023 else "new"


def src_of(t):
    s = t.get("src", "")
    if s == "local":
        return "local"
    if s in ("audius", "jamendo"):
        return "indie"
    if t.get("via") in ("spotify", "apple") or t.get("catalog"):
        return "chart"
    return "youtube"


def daypart(ts=None):
    h = time.localtime(ts or time.time()).tm_hour
    return 0 if h < 6 else 1 if h < 12 else 2 if h < 18 else 3


# ------------------------------------------------------------------ storage

SCHEMA = """
CREATE TABLE IF NOT EXISTS rec_events(id INTEGER PRIMARY KEY, user TEXT NOT NULL, item TEXT NOT NULL, kind TEXT NOT NULL,
  value REAL NOT NULL, secs REAL, ctx TEXT, at INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS rec_events_user ON rec_events(user, at);
CREATE TABLE IF NOT EXISTS rec_items(id TEXT PRIMARY KEY, key TEXT, akey TEXT, artist TEXT, title TEXT, src TEXT, data TEXT,
  genre TEXT, year INTEGER, lang TEXT, pop REAL, at INTEGER);
CREATE INDEX IF NOT EXISTS rec_items_akey ON rec_items(akey);
CREATE TABLE IF NOT EXISTS rec_users(user TEXT PRIMARY KEY, model TEXT, n INTEGER, updated INTEGER);
CREATE TABLE IF NOT EXISTS dz_artist(key TEXT PRIMARY KEY, id INTEGER, name TEXT, fans INTEGER, related TEXT, top TEXT, at INTEGER);
CREATE TABLE IF NOT EXISTS rec_match_queue(key TEXT PRIMARY KEY, artist TEXT, title TEXT, prio REAL, at INTEGER);
"""

ITEM_KEYS = ("id", "src", "rawId", "title", "artist", "artistId", "cover", "coverSm", "duration", "genre", "mood", "tags",
             "bpm", "key", "releaseDate", "quality", "downloadable", "ext", "stream", "dlOrig", "dlMp3", "link", "views",
             "plays", "via", "channel", "extLink", "extName", "catalog", "instrumental")
URL_KEYS = {"cover", "coverSm", "stream", "dlOrig", "dlMp3", "link", "extLink"}
SRC_OK = {"audius", "jamendo", "local", "youtube", "spotify", "apple"}


def clean_item(t):
    """Keep only known fields; URLs must be our API paths or https (they are shown to other listeners later)."""
    if not isinstance(t, dict) or t.get("src") not in SRC_OK:
        return None
    out = {}
    for k in ITEM_KEYS:
        v = t.get(k)
        if v is None or v == "":
            continue
        if k in URL_KEYS:
            if isinstance(v, str) and len(v) < 600 and (re.fullmatch(r"/api/[A-Za-z0-9/_.%=&?:,+-]*", v) or re.fullmatch(r"https://[^\s<>\"'`]+", v)):
                out[k] = v
        elif isinstance(v, str):
            out[k] = v[:300]
        elif isinstance(v, bool):
            out[k] = v
        elif isinstance(v, (int, float)) and math.isfinite(v):
            out[k] = v
        elif k == "tags" and isinstance(v, list):
            out[k] = [str(x)[:40] for x in v[:6]]
    if not out.get("id") or not out.get("title") or len(out["id"]) > 120:
        return None
    return out


_meta_cache = {"m": {}, "at": 0}


def song_meta():
    """hit_key → (genre, year, explicit) from Apple's catalog, refreshed every 10 minutes."""
    if time.time() - _meta_cache["at"] > 600:
        try:
            with D["db"]() as con:
                rows = con.execute("SELECT k, genre, year, explicit FROM song_meta WHERE genre != ''").fetchall()
            _meta_cache.update(m={r["k"]: (r["genre"], r["year"], r["explicit"]) for r in rows}, at=time.time())
        except Exception as e:
            print("recsys: meta", type(e).__name__, e, flush=True)
    return _meta_cache["m"]


def info(t):
    """Facts about one song used everywhere below."""
    title, artist = t.get("title", ""), t.get("artist", "")
    m = song_meta().get(D["hit_key"](artist, title))
    genre = norm_genre(m[0]) if m else norm_genre(t.get("genre", ""))
    text = f"{title} {' '.join(t.get('tags') or [])}"
    if re.search(r"phonk", text, re.I):
        genre = "Phonk"
    rd = str(t.get("releaseDate") or "")[:4]
    year = (m[1] if m else 0) or (int(rd) if rd.isdigit() else 0)
    plays = t.get("plays") or (t.get("views") or 0) / 50
    return {
        "genre": genre, "year": year or 0, "explicit": bool(m and m[2]) or bool(re.search(r"explicit", title, re.I)),
        "lang": lang_of(title, artist, t.get("tags") or ()), "akey": artist_key(artist), "src": src_of(t),
        "pop": min(1.0, math.log10((plays or 0) + 1) / 8), "dur": min(1.0, (t.get("duration") or 210) / 420),
    }


def register_items(items):
    """Remember songs we were asked about, so other listeners' candidates and the ALS can use them."""
    now = int(time.time())
    rows = []
    for t in items:
        c = clean_item(t)
        if not c:
            continue
        i = info(c)
        rows.append((c["id"], D["hit_key"](c.get("artist", ""), c.get("title", "")), i["akey"], c.get("artist", ""), c.get("title", ""),
                     c["src"], json.dumps(c, ensure_ascii=False), i["genre"], i["year"], i["lang"], i["pop"], now))
    if rows:
        with D["db"]() as con:
            con.executemany("INSERT OR IGNORE INTO rec_items(id,key,akey,artist,title,src,data,genre,year,lang,pop,at) "
                            "VALUES(?,?,?,?,?,?,?,?,?,?,?,?)", rows)
    return len(rows)


# ------------------------------------------------------------------ events and identity

def add_events(user, events):
    """events: [{kind, t: track, secs, mood}] from the page. Returns how many were stored."""
    now = int(time.time())
    rows, items = [], []
    for e in events[:200]:
        if not isinstance(e, dict):
            continue
        kind = e.get("kind")
        t = clean_item(e.get("t"))
        if kind not in KIND_VALUE and kind != "skip" or not t:
            continue
        secs = e.get("secs")
        secs = float(secs) if isinstance(secs, (int, float)) and math.isfinite(secs) and 0 <= secs < 36000 else None
        if kind == "skip":
            value = -1.5 if (secs or 0) < 10 else -0.8 if (secs or 0) < 30 else -0.3
        else:
            value = KIND_VALUE[kind]
        at = e.get("at")
        at = int(at / 1000) if isinstance(at, (int, float)) and now - 7 * DAY < at / 1000 <= now + 60 else now
        ctx = json.dumps({"mood": str(e.get("mood") or "")[:30], "dp": daypart(at)})
        rows.append((user, t["id"], kind, value, secs, ctx, at))
        items.append(t)
    if not rows:
        return 0
    register_items(items)
    with D["db"]() as con:
        con.executemany("INSERT INTO rec_events(user,item,kind,value,secs,ctx,at) VALUES(?,?,?,?,?,?,?)", rows)
    _dirty.add(user)
    STATE.pop(user, None)
    return len(rows)


_merged = set()


def merge_guest(guest, user):
    """A guest who signs in keeps what the model learned while they were a guest."""
    if not guest or guest in _merged:
        return
    _merged.add(guest)
    with D["db"]() as con:
        n = con.execute("UPDATE rec_events SET user=? WHERE user=?", (user, guest)).rowcount
    if n:
        STATE.pop(user, None)
        _dirty.add(user)
        print("recsys: merged", n, "guest events into", user, flush=True)


def seed_from_profiles():
    """First start: turn existing likes and history in saved profiles into events, so nobody starts from zero."""
    with D["db"]() as con:
        if con.execute("SELECT 1 FROM rec_events LIMIT 1").fetchone():
            return
        profiles = con.execute("SELECT user_id, data FROM profiles").fetchall()
    for r in profiles:
        try:
            p = json.loads(r["data"])
        except ValueError:
            continue
        ev = []
        for t in (p.get("likes") or {}).values():
            at = (t.get("likedAt") or time.time() * 1000)
            ev.append({"kind": "like", "t": t, "at": at})
        for t in (p.get("hist") or [])[:100]:
            ev.append({"kind": "listen", "t": t, "at": t.get("at") or time.time() * 1000})
        # seeded events may be older than a week: store them with their real time
        user = f"u:{r['user_id']}"
        rows, items = [], []
        for e in ev:
            t = clean_item(e["t"])
            if not t:
                continue
            at = int(e["at"] / 1000) if isinstance(e["at"], (int, float)) else int(time.time())
            rows.append((user, t["id"], e["kind"], KIND_VALUE[e["kind"]], None, json.dumps({"mood": "", "dp": daypart(at)}), at))
            items.append(t)
        register_items(items)
        with D["db"]() as con:
            con.executemany("INSERT INTO rec_events(user,item,kind,value,secs,ctx,at) VALUES(?,?,?,?,?,?,?)", rows)
        _dirty.add(user)
        print("recsys: seeded", len(rows), "events for", user, flush=True)


# ------------------------------------------------------------------ Deezer related-artist graph

_dz_lock = threading.Lock()
_dz_queue = deque()
_dz_queued = set()
_dz_mem = {}  # akey → {"id","name","fans","related":[(akey,name)],"top":[title]} or None for "not found"


def _dz_get(path):
    url = "https://api.deezer.com/" + path
    with urllib.request.urlopen(urllib.request.Request(url, headers=D.get("ua") or {}), timeout=12) as r:
        d = json.loads(r.read(2_000_000))
    if isinstance(d, dict) and d.get("error"):
        raise ValueError(str(d["error"])[:100])
    return d


def dz_artist(akey):
    """Cached artist node, or None when unknown yet (it is then queued for the background worker)."""
    if not akey:
        return None
    if akey in _dz_mem:
        return _dz_mem[akey]
    with D["db"]() as con:
        r = con.execute("SELECT id, name, fans, related, top, at FROM dz_artist WHERE key=?", (akey,)).fetchone()
    if r and time.time() - r["at"] < 30 * DAY:
        node = None if not r["id"] else {"id": r["id"], "name": r["name"], "fans": r["fans"],
                                          "related": json.loads(r["related"] or "[]"), "top": json.loads(r["top"] or "[]")}
        _dz_mem[akey] = node
        return node
    queue_artist(akey, akey)
    return None


def queue_artist(akey, name, front=False):
    with _dz_lock:
        if not akey or akey in _dz_queued or akey in _dz_mem:
            return
        _dz_queued.add(akey)
        (_dz_queue.appendleft if front else _dz_queue.append)((akey, name))


def _dz_fetch(akey, name):
    q = urllib.parse.quote(name)
    found = _dz_get(f"search/artist?q={q}&limit=8").get("data") or []
    same = [a for a in found if artist_key(a.get("name", "")) == akey]
    pick = max(same or found[:1], key=lambda a: a.get("nb_fan") or 0, default=None)
    if not pick:
        node = None
    else:
        time.sleep(1)
        rel = _dz_get(f"artist/{pick['id']}/related?limit=25").get("data") or []
        time.sleep(1)
        top = _dz_get(f"artist/{pick['id']}/top?limit=10").get("data") or []
        node = {"id": pick["id"], "name": pick.get("name", name), "fans": pick.get("nb_fan") or 0,
                "related": [(artist_key(a.get("name", "")), a.get("name", "")) for a in rel if a.get("name")],
                "top": [t.get("title", "") for t in top if t.get("title")]}
    with D["db"]() as con:
        con.execute("INSERT OR REPLACE INTO dz_artist(key,id,name,fans,related,top,at) VALUES(?,?,?,?,?,?,?)",
                    (akey, node["id"] if node else 0, node["name"] if node else name, node["fans"] if node else 0,
                     json.dumps(node["related"] if node else [], ensure_ascii=False),
                     json.dumps(node["top"] if node else [], ensure_ascii=False), int(time.time())))
    _dz_mem[akey] = node
    return node


def dz_worker_step(limit=20):
    """Fetch up to `limit` queued artists (≈3 s each: search, related, top)."""
    done = 0
    while done < limit:
        with _dz_lock:
            if not _dz_queue:
                break
            akey, name = _dz_queue.popleft()
        try:
            if _dz_fetch(akey, name):
                STATE.clear()  # a new graph node changes what everybody's taste spreads to
        except Exception as e:
            print("recsys: deezer", akey, type(e).__name__, e, flush=True)
            time.sleep(5)
        finally:
            with _dz_lock:
                _dz_queued.discard(akey)
        done += 1
        time.sleep(1)
    return done


# ------------------------------------------------------------------ per-listener state

class Taste:
    """Decayed sums of reactions per artist, genre, language and era, plus what the graph spreads from them."""

    def __init__(self):
        self.artist = defaultdict(float)
        self.artist_name = {}
        self.genre = defaultdict(float)
        self.lang = defaultdict(float)
        self.era = defaultdict(float)
        self.item = {}            # item id → latest decayed value (for "already finished / skipped")
        self.last = {}            # item id → (kind, at)
        self.banned = set()
        self.graph = {}           # akey → spread score
        self.graph_from = {}      # akey → the liked artist it was reached from (for the reason text)
        self.graph_name = {}      # akey → artist name as Deezer writes it
        self.pos = 0              # how many positive reactions
        self.n = 0                # how many reactions in total

    def strength(self):
        """0 … 1: how much we know about this listener."""
        return 1 - math.exp(-self.pos / 8)  # 3 likes ≈ 0.3, 10 ≈ 0.7, 25 ≈ 0.95


STATE = {}           # user → (Taste, built_at)
_dirty = set()       # users with new events since their model was trained


def _user_events(user, limit=4000):
    with D["db"]() as con:
        rows = con.execute(
            "SELECT e.item, e.kind, e.value, e.secs, e.ctx, e.at, i.data FROM rec_events e LEFT JOIN rec_items i ON i.id=e.item "
            "WHERE e.user=? ORDER BY e.at DESC LIMIT ?", (user, limit)).fetchall()
    return rows


def build_taste(user):
    hit = STATE.get(user)
    if hit and time.time() - hit[1] < 300:
        return hit[0]
    T = Taste()
    now = time.time()
    for r in _user_events(user):
        if r["kind"] == "start":
            continue
        try:
            t = json.loads(r["data"]) if r["data"] else None
        except ValueError:
            t = None
        if not t:
            continue
        w = r["value"] * 0.5 ** ((now - r["at"]) / HALF_LIFE)
        i = info(t)
        T.n += 1
        if r["value"] > 0:
            T.pos += 1
        if r["kind"] == "ban":
            T.banned.add(r["item"])
        if r["item"] not in T.last:
            T.last[r["item"]] = (r["kind"], r["at"])
        T.item[r["item"]] = T.item.get(r["item"], 0) + w
        if i["akey"]:
            T.artist[i["akey"]] += w
            T.artist_name.setdefault(i["akey"], t.get("artist", ""))
        if i["genre"] and i["genre"] != "Other":  # "Other" means "we do not know", not a taste
            T.genre[i["genre"]] += w
        T.lang[i["lang"]] += w * 0.7
        if era_of(i["year"]):
            T.era[era_of(i["year"])] += w * 0.5
    _spread(T)
    STATE[user] = (T, time.time())
    if len(STATE) > 500:
        for k in list(STATE)[:100]:
            STATE.pop(k, None)
    return T


def _spread(T):
    """Personalised propagation over the related-artist graph (two hops, decaying)."""
    seeds = sorted(T.artist.items(), key=lambda kv: -abs(kv[1]))[:40]
    g, src, names = defaultdict(float), {}, {}
    for akey, w in seeds:
        if abs(w) < 0.3:
            continue
        node = dz_artist(akey)
        if not node:
            continue
        sign = 1.0 if w > 0 else 0.5  # dislikes spread at half strength
        rel = node["related"][:20]
        for i, (rk, _name) in enumerate(rel):
            if not rk or rk == akey:
                continue
            names.setdefault(rk, _name)
            add = w * sign * 0.6 * (1 - i / 25)
            g[rk] += add
            if add > 0 and (rk not in src or add > src[rk][1]):
                src[rk] = (akey, add)
            if i < 8 and w > 0:
                n2 = _dz_mem.get(rk) if rk in _dz_mem else None
                if n2 is None and rk not in _dz_mem:
                    queue_artist(rk, _name)  # second hop: learn it for next time
                for j, (rk2, _n2) in enumerate((n2 or {}).get("related", [])[:12]):
                    if rk2 and rk2 != akey:
                        names.setdefault(rk2, _n2)
                        g[rk2] += w * 0.25 * (1 - i / 25) * (1 - j / 15)
                        src.setdefault(rk2, (akey, 0))
    T.graph = dict(g)
    T.graph_name = names
    T.graph_from = {k: v[0] for k, v in src.items()}


# ------------------------------------------------------------------ neural network (numpy MLP)

N_IN = len(GENRES) + len(LANGS) + len(ERAS) + len(SRCS) + 4 + 4 + 1
HID = 16


def features(t, dp=None):
    i = info(t)
    x = np.zeros(N_IN, dtype=np.float32)
    o = 0
    if i["genre"] in GENRES:
        x[o + GENRES.index(i["genre"])] = 1
    o += len(GENRES)
    x[o + LANGS.index(i["lang"])] = 1
    o += len(LANGS)
    x[o + ERAS.index(era_of(i["year"]))] = 1
    o += len(ERAS)
    x[o + SRCS.index(i["src"])] = 1
    o += len(SRCS)
    x[o] = i["pop"]
    x[o + 1] = i["dur"]
    x[o + 2] = 1.0 if i["explicit"] else 0.0
    x[o + 3] = 1.0 if (t.get("bpm") or 0) > 120 else 0.0
    o += 4
    x[o + (daypart() if dp is None else dp)] = 1
    o += 4
    x[o] = 1.0
    return x


class MLP:
    """N_IN → 16 (tanh) → 1 (sigmoid). Small on purpose: a listener gives tens to hundreds of reactions."""

    def __init__(self, seed=7):
        rng = np.random.default_rng(seed)
        self.w1 = (rng.standard_normal((HID, N_IN)) * 0.15).astype(np.float32)
        self.b1 = np.zeros(HID, dtype=np.float32)
        self.w2 = (rng.standard_normal(HID) * 0.15).astype(np.float32)
        self.b2 = np.float32(0)
        self.seen = 0

    def copy(self):
        m = MLP.__new__(MLP)
        m.w1, m.b1, m.w2, m.b2, m.seen = self.w1.copy(), self.b1.copy(), self.w2.copy(), np.float32(self.b2), self.seen
        return m

    def predict(self, X):
        H = np.tanh(X @ self.w1.T + self.b1)
        return 1 / (1 + np.exp(-(H @ self.w2 + self.b2)))

    def fit(self, X, y, w, epochs=30, lr=0.05, l2=1e-3, prior=None):
        """Weighted log-loss, full-batch gradient descent. With `prior`, weights are pulled towards the global model
        (so a listener with few reactions stays close to what works for everyone)."""
        if len(X) == 0:
            return self
        wsum = w.sum() or 1
        for _ in range(epochs):
            H = np.tanh(X @ self.w1.T + self.b1)
            p = 1 / (1 + np.exp(-(H @ self.w2 + self.b2)))
            g = (p - y) * w / wsum
            gw2 = H.T @ g
            gb2 = g.sum()
            gh = np.outer(g, self.w2) * (1 - H * H)
            gw1 = gh.T @ X
            gb1 = gh.sum(axis=0)
            if prior is not None:
                gw1 += l2 * 10 * (self.w1 - prior.w1)
                gw2 += l2 * 10 * (self.w2 - prior.w2)
            else:
                gw1 += l2 * self.w1
                gw2 += l2 * self.w2
            self.w1 -= lr * gw1
            self.b1 -= lr * gb1
            self.w2 -= lr * gw2
            self.b2 -= lr * gb2
        self.seen += len(X)
        return self

    def dump(self):
        return {"w1": np.round(self.w1, 4).tolist(), "b1": np.round(self.b1, 4).tolist(), "w2": np.round(self.w2, 4).tolist(),
                "b2": round(float(self.b2), 4), "seen": self.seen, "n_in": N_IN}

    @staticmethod
    def load(d):
        if not d or d.get("n_in") != N_IN:
            return None
        m = MLP.__new__(MLP)
        m.w1 = np.array(d["w1"], dtype=np.float32)
        m.b1 = np.array(d["b1"], dtype=np.float32)
        m.w2 = np.array(d["w2"], dtype=np.float32)
        m.b2 = np.float32(d["b2"])
        m.seen = d.get("seen", 0)
        return m


GLOBAL = {"mlp": None, "at": 0}
MODELS = {}  # user → MLP


def _training_set(rows):
    X, y, w = [], [], []
    for r in rows:
        if r["kind"] == "start" or not r["data"]:
            continue
        try:
            t = json.loads(r["data"])
            dp = json.loads(r["ctx"] or "{}").get("dp")
        except ValueError:
            continue
        X.append(features(t, dp if isinstance(dp, int) and 0 <= dp < 4 else None))
        y.append(1.0 if r["value"] > 0 else 0.0)
        w.append(min(3.0, abs(r["value"])) * 0.5 ** ((time.time() - r["at"]) / (2 * HALF_LIFE)))
    if not X:
        return np.zeros((0, N_IN), np.float32), np.zeros(0, np.float32), np.zeros(0, np.float32)
    return np.stack(X), np.array(y, np.float32), np.array(w, np.float32)


def _negatives(user, n):
    """Implicit negatives: songs the listener has not reacted to, sampled from everything the site knows.
    Without them a listener who mostly presses «like» teaches the network that every song is a hit."""
    if n <= 0:
        return np.zeros((0, N_IN), np.float32)
    with D["db"]() as con:
        rows = con.execute("SELECT data FROM rec_items WHERE id NOT IN (SELECT item FROM rec_events WHERE user=?) "
                           "ORDER BY RANDOM() LIMIT ?", (user or "", n)).fetchall()
    X = []
    for r in rows:
        try:
            X.append(features(json.loads(r["data"])))
        except ValueError:
            pass
    return np.stack(X) if X else np.zeros((0, N_IN), np.float32)


def _with_negatives(X, y, w, user):
    """Balance positives with sampled unknown songs (label 0, low weight: "probably not", not "disliked")."""
    pos = int((y > 0.5).sum())
    neg = int((y < 0.5).sum())
    Xn = _negatives(user, max(0, min(600, pos * 2 - neg)))
    if not len(Xn):
        return X, y, w
    return (np.vstack([X, Xn]), np.concatenate([y, np.zeros(len(Xn), np.float32)]),
            np.concatenate([w, np.full(len(Xn), 0.35, np.float32)]))


def train_global():
    with D["db"]() as con:
        rows = con.execute("SELECT e.kind, e.value, e.ctx, e.at, i.data FROM rec_events e JOIN rec_items i ON i.id=e.item "
                           "WHERE e.kind != 'start' ORDER BY e.at DESC LIMIT 20000").fetchall()
    X, y, w = _training_set(rows)
    X, y, w = _with_negatives(X, y, w, None)
    m = MLP()
    if len(X) >= 20:
        m.fit(X, y, w, epochs=120, lr=0.1)
    GLOBAL.update(mlp=m, at=time.time())
    print("recsys: global model on", len(X), "reactions", flush=True)


def train_user(user):
    rows = _user_events(user, 3000)
    X, y, w = _training_set(rows)
    real = len(X)
    prior = GLOBAL["mlp"] or MLP()
    m = prior.copy()
    if real >= 5:
        X, y, w = _with_negatives(X, y, w, user)
        m.fit(X, y, w, epochs=80, lr=0.1, prior=prior)
    m.seen = real
    X = X[:real]
    MODELS[user] = m
    with D["db"]() as con:
        con.execute("INSERT OR REPLACE INTO rec_users(user,model,n,updated) VALUES(?,?,?,?)",
                    (user, json.dumps(m.dump()), len(X), int(time.time())))


def user_model(user):
    m = MODELS.get(user)
    if m is not None:
        return m
    with D["db"]() as con:
        r = con.execute("SELECT model FROM rec_users WHERE user=?", (user,)).fetchone()
    m = MLP.load(json.loads(r["model"])) if r and r["model"] else None
    if m is None:
        _dirty.add(user)
        m = GLOBAL["mlp"]
    else:
        MODELS[user] = m
    return m


# ------------------------------------------------------------------ collaborative filtering (implicit ALS)

CF = {"users": {}, "items": {}, "U": None, "V": None, "at": 0}


def train_cf(factors=24, iters=8, reg=0.1, alpha=8.0):
    """Implicit ALS (Hu, Koren, Volinsky). Off until enough listeners exist for it to mean something."""
    with D["db"]() as con:
        rows = con.execute("SELECT user, item, SUM(value) v FROM rec_events WHERE kind != 'start' GROUP BY user, item").fetchall()
    by_user = defaultdict(dict)
    for r in rows:
        if r["v"] > 0:
            by_user[r["user"]][r["item"]] = r["v"]
    users = [u for u, it in by_user.items() if len(it) >= 10]
    if len(users) < 5:
        CF.update(U=None, V=None, at=time.time())
        return
    item_users = defaultdict(int)
    for u in users:
        for i in by_user[u]:
            item_users[i] += 1
    items = [i for i, c in item_users.items() if c >= 2]
    if len(items) < 20:
        CF.update(U=None, V=None, at=time.time())
        return
    ui = {u: k for k, u in enumerate(users)}
    ii = {i: k for k, i in enumerate(items)}
    C = np.zeros((len(users), len(items)), np.float32)
    for u in users:
        for i, v in by_user[u].items():
            if i in ii:
                C[ui[u], ii[i]] = alpha * min(v, 6)
    Pm = (C > 0).astype(np.float32)
    rng = np.random.default_rng(1)
    U = rng.standard_normal((len(users), factors)).astype(np.float32) * 0.1
    V = rng.standard_normal((len(items), factors)).astype(np.float32) * 0.1
    I = np.eye(factors, dtype=np.float32)
    for _ in range(iters):
        VtV = V.T @ V
        for u in range(len(users)):
            c = C[u]
            Cu = V.T * c  # (f, items)
            U[u] = np.linalg.solve(VtV + Cu @ V + reg * I, (V.T * (1 + c)) @ Pm[u])
        UtU = U.T @ U
        for i in range(len(items)):
            c = C[:, i]
            Ci = U.T * c
            V[i] = np.linalg.solve(UtU + Ci @ U + reg * I, (U.T * (1 + c)) @ Pm[:, i])
    CF.update(users=ui, items=ii, U=U, V=V, at=time.time())
    print("recsys: ALS on", len(users), "listeners ×", len(items), "songs", flush=True)


def cf_score(user, item_id):
    if CF["U"] is None or user not in CF["users"] or item_id not in CF["items"]:
        return None
    return float(CF["U"][CF["users"][user]] @ CF["V"][CF["items"][item_id]])


# ------------------------------------------------------------------ ranking

def _norm_map(m, k=3.0):
    """tanh-squash a decayed sum so one heavy artist does not drown everything else."""
    return lambda key: math.tanh(m.get(key, 0) / k)


def rank(user, candidates, ctx=None):
    """{id: [score, reason]} for each candidate. Scores are on a common scale around 0 (≈ −6 … +6)."""
    ctx = ctx or {}
    cands = [c for c in (clean_item(t) for t in candidates[:500]) if c]
    if not cands:
        return {}
    register_items(cands)
    T = build_taste(user)
    m = user_model(user)
    strength = T.strength()
    nn_conf = min(1.0, (m.seen if m is not None else 0) / 60) if m is not None else 0
    a_f, g_f, l_f, e_f = _norm_map(T.artist), _norm_map(T.genre, 6), _norm_map(T.lang, 6), _norm_map(T.era, 6)
    graph_f = _norm_map(T.graph, 2)
    X = np.stack([features(t) for t in cands]) if m is not None else None
    P = m.predict(X) if m is not None else None
    out = {}
    now = time.time()
    for k, t in enumerate(cands):
        i = info(t)
        if t["id"] in T.banned or (i["akey"] and T.artist.get(i["akey"], 0) < -6):
            out[t["id"]] = [-99, "вы это не любите"]
            continue
        parts = {
            "artist": 2.4 * a_f(i["akey"]),
            "graph": 2.2 * graph_f(i["akey"]),
            "genre": 1.3 * g_f(i["genre"]),
            "lang": 1.4 * l_f(i["lang"]),
            "era": 0.4 * e_f(era_of(i["year"])),
        }
        if P is not None:
            parts["nn"] = 2.6 * nn_conf * (float(P[k]) - 0.5) * 2
        cf = cf_score(user, t["id"])
        if cf is not None:
            parts["cf"] = 1.6 * math.tanh(cf)
        # what you already reacted to
        last = T.last.get(t["id"])
        pen = 0.0
        if last:
            kind, at = last
            age = now - at
            if kind in ("finish", "listen") and age < 2 * DAY:
                pen -= 2.5
            if kind == "skip" and age < 14 * DAY:
                pen -= 3.0
        score = sum(parts.values()) * (0.5 + 0.5 * strength) + pen
        best = max(parts.items(), key=lambda kv: kv[1])
        out[t["id"]] = [round(score, 3), _reason(best, i, T) if best[1] > 0.6 else ""]
    return out


def _reason(best, i, T):
    kind = best[0]
    if kind == "artist":
        return f"Вы слушаете {T.artist_name.get(i['akey']) or 'этого исполнителя'}"
    if kind == "graph":
        src = T.graph_from.get(i["akey"])
        name = T.artist_name.get(src) if src else None
        return f"Похоже на {name}" if name else "Похоже на ваших исполнителей"
    if kind == "genre":
        return f"Ваш жанр: {GENRE_RU.get(i['genre'], i['genre'])}"
    if kind == "lang":
        return f"Вы любите песни {LANG_RU.get(i['lang'], '')}".strip()
    if kind == "nn":
        return "Нейросеть: в вашем вкусе"
    if kind == "cf":
        return "Слушатели с похожим вкусом"
    if kind == "era":
        return f"Ваша эпоха: {ERA_RU.get(era_of(i['year']), '')}"
    return ""


# ------------------------------------------------------------------ candidates

def _universe():
    """Songs that can be played right now: YouTube music lists, chart songs with a found video, and songs any
    listener has reacted to. akey → [track dict]."""
    u = defaultdict(list)
    seen = set()

    def add(t):
        if t["id"] in seen:
            return
        seen.add(t["id"])
        u[artist_key(t.get("artist", ""))].append(t)

    for name in D["yt_lists"]():
        try:
            for v in D["yt_list"](name):
                add({"id": "yt:" + v["videoId"], "src": "youtube", "rawId": v["videoId"], "title": v.get("title", ""),
                     "artist": v.get("artist", ""), "views": v.get("views") or 0, "_yt": v})
        except Exception:
            pass
    for v in D["matched"]().values():
        add({"id": "yt:" + v["videoId"], "src": "youtube", "rawId": v["videoId"], "title": v["title"], "artist": v["artist"],
             "_yt": {"videoId": v["videoId"], "artist": v["artist"], "title": v["title"]}})
    with D["db"]() as con:
        rows = con.execute("SELECT DISTINCT i.data FROM rec_events e JOIN rec_items i ON i.id=e.item WHERE e.value > 0 "
                           "ORDER BY e.at DESC LIMIT 3000").fetchall()
    for r in rows:
        try:
            add(json.loads(r["data"]))
        except ValueError:
            pass
    return u


_uni_cache = {"u": None, "at": 0}


def universe():
    if _uni_cache["u"] is None or time.time() - _uni_cache["at"] > 900:
        _uni_cache.update(u=_universe(), at=time.time())
    return _uni_cache["u"]


def candidates(user, n=60):
    """Songs of the artists your taste and the graph point to, that you have not just heard."""
    T = build_taste(user)
    U = universe()
    scored = defaultdict(float)
    for ak, w in T.artist.items():
        if w > 0.5:
            scored[ak] += math.tanh(w / 3) * 1.0
    for ak, w in T.graph.items():
        if w > 0.2:
            scored[ak] += math.tanh(w / 2) * 1.2
    picks = []
    now = time.time()
    for ak, s in sorted(scored.items(), key=lambda kv: -kv[1])[:80]:
        songs = [t for t in U.get(ak, []) if t["id"] not in T.banned and not (t["id"] in T.last and now - T.last[t["id"]][1] < 3 * DAY)]
        random.shuffle(songs)
        for t in songs[:3]:
            picks.append((s + random.random() * 0.3, t))
        if not songs:
            node = _dz_mem.get(ak)
            if node and node.get("top"):
                for title in node["top"][:3]:
                    _queue_match(node["name"], title, s)
    picks.sort(key=lambda x: -x[0])
    out = []
    for _s, t in picks[:n]:
        if "_yt" in t:
            out.append({"yt": t["_yt"]})
        else:
            out.append({"t": t})
    return out


def _queue_match(artist, title, prio):
    k = D["hit_key"](artist, title)
    try:
        with D["db"]() as con:
            con.execute("INSERT INTO rec_match_queue(key,artist,title,prio,at) VALUES(?,?,?,?,?) "
                        "ON CONFLICT(key) DO UPDATE SET prio=MAX(prio, excluded.prio)", (k, artist, title, prio, int(time.time())))
    except Exception as e:
        print("recsys: queue", type(e).__name__, flush=True)


def match_queue(n=40):
    """Most wanted songs (by listeners' taste) for the server's YouTube matching loop."""
    with D["db"]() as con:
        rows = con.execute("SELECT artist, title FROM rec_match_queue ORDER BY prio DESC, at DESC LIMIT ?", (n,)).fetchall()
    return [(r["artist"], r["title"]) for r in rows]


def match_done(keys):
    if keys:
        with D["db"]() as con:
            con.executemany("DELETE FROM rec_match_queue WHERE key=?", [(k,) for k in keys])


def reset(user):
    """«Сбросить вкус»: forget reactions and the trained model; likes stay (they are your music, not a guess)."""
    with D["db"]() as con:
        con.execute("DELETE FROM rec_events WHERE user=? AND kind != 'like'", (user,))
        con.execute("DELETE FROM rec_users WHERE user=?", (user,))
    STATE.pop(user, None)
    MODELS.pop(user, None)
    _dirty.add(user)


# ------------------------------------------------------------------ explain

def explain(user):
    T = build_taste(user)
    m = user_model(user)
    top = lambda d, k=6: [kv for kv in sorted(d.items(), key=lambda kv: -kv[1]) if kv[1] > 0.3][:k]
    nn_likes = []
    if m is not None and m.seen >= 10:
        probe = []
        for g in GENRES[:-1]:
            probe.append((GENRE_RU[g], {"title": "song" if g != "Phonk" else "phonk", "artist": "x", "genre": g}))
        X = np.stack([features(t) for _, t in probe])
        p = m.predict(X)
        nn_likes = [name for (name, _), v in sorted(zip(probe, p.tolist()), key=lambda z: -z[1]) if v > 0.55][:4]
    graph = [T.artist_name.get(k) or (_dz_mem.get(k) or {}).get("name") or k for k, _ in top(T.graph, 8) if k not in T.artist]
    graph_names = []
    for k in [k for k, _ in top(T.graph, 12) if k not in T.artist][:8]:
        graph_names.append(T.graph_name.get(k) or (_dz_mem.get(k) or {}).get("name") or k)
    return {
        "reactions": T.n, "positive": T.pos, "strength": round(T.strength(), 2),
        "artists": [T.artist_name.get(k, k) for k, _ in top(T.artist)],
        "genres": [GENRE_RU.get(k, k) for k, _ in top(T.genre, 5)],
        "langs": [LANG_RU.get(k, k) for k, _ in top(T.lang, 2)],
        "eras": [ERA_RU.get(k, k) for k, _ in top(T.era, 2) if k],
        "similar": graph_names or graph,
        "nn": {"seen": m.seen if m is not None else 0, "likes": nn_likes},
        "cf": CF["U"] is not None and user in CF["users"],
        "graph_ready": sum(1 for k in list(T.artist)[:40] if k in _dz_mem),
    }


# ------------------------------------------------------------------ background

def _loop():
    time.sleep(15)
    try:
        seed_from_profiles()
    except Exception as e:
        print("recsys: seed", type(e).__name__, e, flush=True)
    last_global = last_cf = 0
    while True:
        try:
            # graph first: it is what makes the next rank calls better
            for user in list(_dirty)[:20]:
                T = build_taste(user)
                for ak, w in sorted(T.artist.items(), key=lambda kv: -kv[1])[:30]:
                    if w > 0.3 and ak not in _dz_mem:
                        queue_artist(ak, T.artist_name.get(ak, ak), front=True)
            dz_worker_step(15)
            if time.time() - last_global > 1800:
                train_global()
                last_global = time.time()
            for user in list(_dirty)[:10]:
                _dirty.discard(user)
                train_user(user)
                STATE.pop(user, None)
            if time.time() - last_cf > 3600:
                train_cf()
                last_cf = time.time()
        except Exception as e:
            print("recsys loop:", type(e).__name__, e, flush=True)
        time.sleep(10)


def init(**deps):
    D.update(deps)
    with D["db"]() as con:
        con.executescript(SCHEMA)
    threading.Thread(target=_loop, daemon=True, name="recsys").start()
