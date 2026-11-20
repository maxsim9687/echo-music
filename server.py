"""Эхо: сервер личного музыкального сайта.

Только стандартная библиотека Python (+ необязательный пакет anthropic для ИИ-подборок).
Запуск:  python server.py   (настройки в файле .env рядом)
"""
import hashlib
import hmac
import json
import mimetypes
import os
import re
import secrets

import recsys
import smtplib
import sqlite3
import ssl
import threading
import time
import urllib.parse
import urllib.request
from email.message import EmailMessage
from email.utils import formataddr, parseaddr
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PUBLIC = ROOT / "public"
DATA = ROOT / "data"
UPLOADS = DATA / "uploads"
DB_PATH = DATA / "echo.db"


# ---------- config ----------
def load_env():
    env = {}
    p = ROOT / ".env"
    if p.exists():
        for line in p.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            k, v = line.split("=", 1)
            env[k.strip()] = v.strip()
    for k, v in env.items():
        os.environ.setdefault(k, v)


load_env()
CFG = {
    "smtp_host": os.environ.get("SMTP_HOST", ""),
    "smtp_port": int(os.environ.get("SMTP_PORT", "465") or 465),
    "smtp_user": os.environ.get("SMTP_USER", ""),
    "smtp_password": os.environ.get("SMTP_PASSWORD", ""),
    "mail_from": os.environ.get("MAIL_FROM", ""),
    "site_url": os.environ.get("SITE_URL", "http://localhost:8765").rstrip("/"),
    "admins": {e.strip().lower() for e in os.environ.get("ADMIN_EMAILS", "").split(",") if e.strip()},
    "jamendo": os.environ.get("JAMENDO_CLIENT_ID", ""),
    "youtube": os.environ.get("YOUTUBE_API_KEY", ""),
    "spotify_id": os.environ.get("SPOTIFY_CLIENT_ID", ""),
    "spotify_secret": os.environ.get("SPOTIFY_CLIENT_SECRET", ""),
    "port": int(os.environ.get("PORT", "8765") or 8765),
}
SECURE_COOKIE = CFG["site_url"].startswith("https://")
AUDIO_EXT = {"mp3": "audio/mpeg", "wav": "audio/wav", "flac": "audio/flac", "ogg": "audio/ogg",
             "m4a": "audio/mp4", "aac": "audio/aac", "opus": "audio/ogg"}
IMAGE_TYPES = {"image/jpeg": "jpg", "image/png": "png", "image/webp": "webp"}
MAX_AUDIO = 500 * 1024 * 1024
MAX_IMAGE = 8 * 1024 * 1024
STATIC_TYPES = {".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".css": "text/css; charset=utf-8",
                ".svg": "image/svg+xml", ".png": "image/png", ".ico": "image/x-icon", ".json": "application/json"}
EMAIL_RE = re.compile(r"^[^@\s]{1,64}@[^@\s]{1,190}\.[^@\s]{2,}$")


# ---------- db ----------
_db_lock = threading.Lock()  # serialises check-then-write sequences (login codes)


class db:
    """`with db() as con:` opens a connection, commits on success, rolls back on error, always closes."""

    def __enter__(self):
        self.con = sqlite3.connect(DB_PATH, timeout=10)
        self.con.row_factory = sqlite3.Row
        return self.con

    def __exit__(self, exc_type, *_):
        try:
            if exc_type is None:
                self.con.commit()
            else:
                self.con.rollback()
        finally:
            self.con.close()


def init_db():
    DATA.mkdir(exist_ok=True)
    UPLOADS.mkdir(exist_ok=True)
    with db() as con:
        con.executescript("""
        CREATE TABLE IF NOT EXISTS users(id INTEGER PRIMARY KEY, email TEXT UNIQUE NOT NULL, name TEXT DEFAULT '',
            created INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS codes(email TEXT PRIMARY KEY, code_hash TEXT NOT NULL, expires INTEGER NOT NULL,
            tries INTEGER NOT NULL DEFAULT 0, sent INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS sessions(token_hash TEXT PRIMARY KEY, user_id INTEGER NOT NULL, expires INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS profiles(user_id INTEGER PRIMARY KEY, data TEXT NOT NULL, updated INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS yt_bad(id TEXT PRIMARY KEY, n INTEGER NOT NULL, at INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS lyrics_cache(k TEXT PRIMARY KEY, data TEXT NOT NULL, at INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS yt_cache(q TEXT PRIMARY KEY, data TEXT NOT NULL, at INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS song_meta(k TEXT PRIMARY KEY, genre TEXT NOT NULL, year INTEGER NOT NULL, explicit INTEGER NOT NULL, at INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS shares(id TEXT PRIMARY KEY, title TEXT NOT NULL, data TEXT NOT NULL, owner INTEGER, at INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS uploads(id TEXT PRIMARY KEY, title TEXT NOT NULL, artist TEXT NOT NULL, genre TEXT DEFAULT '',
            mood TEXT DEFAULT '', bpm REAL, ext TEXT NOT NULL, size INTEGER NOT NULL, duration REAL,
            cover TEXT DEFAULT '', created INTEGER NOT NULL, user_id INTEGER);
        """)
        con.execute("PRAGMA journal_mode=WAL")
        cols = {r["name"] for r in con.execute("PRAGMA table_info(codes)")}
        if "link_hash" not in cols:
            con.execute("ALTER TABLE codes ADD COLUMN link_hash TEXT NOT NULL DEFAULT ''")
        now = int(time.time())
        con.execute("DELETE FROM sessions WHERE expires<?", (now,))
        con.execute("DELETE FROM codes WHERE expires<?", (now - 86400,))


def sha(s: str) -> str:
    return hashlib.sha256(s.encode()).hexdigest()


# ---------- mail ----------
def send_code_mail(to_email: str, code: str, link: str):
    msg = EmailMessage()
    name, addr = parseaddr(CFG["mail_from"] or CFG["smtp_user"])
    msg["From"] = formataddr((name or "Эхо", addr or CFG["smtp_user"]))
    msg["To"] = to_email
    msg["Subject"] = f"Вход в Эхо: код {code}"
    msg.set_content(
        f"Чтобы войти, откройте ссылку:\n{link}\n\nИли введите код на сайте: {code}\n\n"
        "Ссылка и код действуют 10 минут и подходят один раз. Если вы не пытались войти, просто проигнорируйте письмо."
    )
    msg.add_alternative(f"""<!doctype html><html lang="ru"><body style="margin:0;padding:0;background:#0b0b0c">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#0b0b0c;padding:32px 12px">
<tr><td align="center">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:460px;background:#161617;border:1px solid #2a2a2c;border-radius:24px;font-family:Inter,'Segoe UI',Arial,sans-serif;color:#f4f4f4">
<tr><td style="padding:32px 32px 8px">
  <table role="presentation" cellpadding="0" cellspacing="0"><tr>
    <td style="width:22px;height:22px;border:2px solid #f4f4f4;border-radius:50%;text-align:center;vertical-align:middle"><span style="display:inline-block;width:10px;height:10px;background:#f4f4f4;border-radius:50%"></span></td>
    <td style="padding-left:10px;font-size:22px;font-weight:700;letter-spacing:-0.5px">Эхо</td>
  </tr></table>
</td></tr>
<tr><td style="padding:20px 32px 0;font-size:28px;line-height:1.15;font-weight:700;letter-spacing:-0.8px">Вход в вашу музыку</td></tr>
<tr><td style="padding:10px 32px 0;font-size:15px;line-height:1.5;color:#a1a1a6">Нажмите кнопку, и вы сразу окажетесь на сайте. Или скопируйте код и вставьте его на странице входа.</td></tr>
<tr><td style="padding:26px 32px 0">
  <a href="{link}" style="display:block;background:#f4f4f4;color:#0b0b0c;text-decoration:none;font-weight:700;font-size:16px;line-height:52px;height:52px;text-align:center;border-radius:999px">Войти в Эхо</a>
</td></tr>
<tr><td style="padding:28px 32px 0;font-size:12px;letter-spacing:1.5px;text-transform:uppercase;color:#8e8e93">Код для входа</td></tr>
<tr><td style="padding:10px 32px 0">
  <div style="background:#0b0b0c;border:1px solid #2a2a2c;border-radius:16px;padding:18px 10px;text-align:center">
    <span style="font-family:'SF Mono',Menlo,Consolas,monospace;font-size:38px;font-weight:700;letter-spacing:12px;color:#ffffff;-webkit-user-select:all;user-select:all">{code}</span>
  </div>
  <div style="padding-top:8px;font-size:12px;color:#8e8e93;text-align:center">Нажмите на код дважды, чтобы выделить и скопировать</div>
</td></tr>
<tr><td style="padding:28px 32px 32px;font-size:13px;line-height:1.5;color:#8e8e93">Ссылка и код действуют 10 минут и подходят один раз. Если вы не пытались войти, просто проигнорируйте письмо.</td></tr>
</table>
<div style="padding-top:16px;font-family:Inter,Arial,sans-serif;font-size:12px;color:#5c5c60">{CFG['site_url'].replace('https://', '')}</div>
</td></tr></table></body></html>""", subtype="html")
    ctx = ssl.create_default_context()
    if CFG["smtp_port"] == 465:
        with smtplib.SMTP_SSL(CFG["smtp_host"], 465, context=ctx, timeout=20) as s:
            s.login(CFG["smtp_user"], CFG["smtp_password"])
            s.send_message(msg)
    else:
        with smtplib.SMTP(CFG["smtp_host"], CFG["smtp_port"], timeout=20) as s:
            s.starttls(context=ctx)
            s.login(CFG["smtp_user"], CFG["smtp_password"])
            s.send_message(msg)


# ---------- AI playlist plan ----------
GENRES = ["Electronic", "Rock", "Metal", "Alternative", "Hip-Hop/Rap", "Experimental", "Punk", "Folk", "Pop", "Ambient",
          "Soundtrack", "World", "Jazz", "Acoustic", "Funk", "R&B/Soul", "Classical", "Reggae", "Country", "Blues",
          "Latin", "Lo-Fi", "Hyperpop", "Techno", "Trap", "House", "Tech House", "Deep House", "Disco", "Electro",
          "Trance", "Future Bass", "Downtempo", "Drum & Bass", "Dubstep", "Vaporwave", "Synthwave"]
MOODS = ["Peaceful", "Romantic", "Sentimental", "Tender", "Easygoing", "Yearning", "Sophisticated", "Sensual", "Cool",
         "Gritty", "Melancholy", "Serious", "Brooding", "Fiery", "Defiant", "Aggressive", "Rowdy", "Excited",
         "Energizing", "Empowering", "Stirring", "Upbeat"]

RULES = [
    (("учеб", "учёб", "урок", "домашк", "экзамен", "сессия", "фокус", "работ", "концентр", "study", "focus"),
     {"title": "Под учёбу", "queries": ["lofi study", "chillhop", "ambient focus", "instrumental beats"],
      "genres": ["Lo-Fi", "Ambient", "Downtempo"], "moods": ["Peaceful", "Cool", "Easygoing"], "bpm_min": 65, "bpm_max": 100, "instrumental": True}),
    (("бег", "трен", "спорт", "зал", "кардио", "run", "gym", "workout"),
     {"title": "На тренировку", "queries": ["workout", "running", "edm energy", "phonk"],
      "genres": ["Electronic", "Drum & Bass", "Trap", "House"], "moods": ["Energizing", "Fiery", "Empowering"], "bpm_min": 120, "bpm_max": 180, "instrumental": False}),
    (("сон", "спат", "засып", "релакс", "медит", "sleep", "relax"),
     {"title": "Перед сном", "queries": ["sleep ambient", "calm piano", "relaxing"],
      "genres": ["Ambient", "Classical", "Acoustic"], "moods": ["Peaceful", "Tender"], "bpm_min": 50, "bpm_max": 90, "instrumental": True}),
    (("ноч", "дорог", "машин", "night", "drive"),
     {"title": "Ночная дорога", "queries": ["night drive", "synthwave", "deep house night"],
      "genres": ["Synthwave", "Deep House", "Electronic", "Downtempo"], "moods": ["Cool", "Brooding", "Sensual"], "bpm_min": 90, "bpm_max": 125, "instrumental": False}),
    (("груст", "печал", "дожд", "sad", "rain"),
     {"title": "Когда грустно", "queries": ["sad", "melancholy piano", "rainy day"],
      "genres": ["Alternative", "Acoustic", "Ambient", "Lo-Fi"], "moods": ["Melancholy", "Sentimental", "Yearning"], "bpm_min": 60, "bpm_max": 105, "instrumental": False}),
    (("вечерин", "туса", "танц", "party", "dance"),
     {"title": "Вечеринка", "queries": ["party", "dance", "house party", "disco"],
      "genres": ["House", "Disco", "Pop", "Tech House"], "moods": ["Upbeat", "Excited", "Rowdy"], "bpm_min": 115, "bpm_max": 135, "instrumental": False}),
]


def rule_plan(prompt: str) -> dict:
    p = prompt.lower()
    for keys, plan in RULES:
        if any(k in p for k in keys):
            return {**plan, "description": f"Подобрано по запросу «{prompt.strip()[:80]}».", "size": 25, "source": "rules"}
    words = [w for w in re.findall(r"[a-zа-яё0-9]+", p) if len(w) > 2][:3]
    return {"title": prompt.strip()[:40] or "Подборка", "description": "Подобрано по ключевым словам.",
            "queries": words or ["chill"], "genres": [], "moods": [], "bpm_min": 0, "bpm_max": 0,
            "instrumental": False, "size": 25, "source": "rules"}


PLAN_SYSTEM = (
    "Ты музыкальный куратор сервиса независимой музыки (каталоги Audius и Jamendo). "
    "Преврати просьбу пользователя в план поиска и ответь ТОЛЬКО JSON-объектом без пояснений, с полями: "
    '"title" (короткое название плейлиста на русском, 2-4 слова), "description" (одно предложение на русском), '
    '"queries" (3-6 коротких поисковых запросов на английском, как теги), "genres" (0-4 жанра строго из списка), '
    '"moods" (0-4 настроения строго из списка), "bpm_min" и "bpm_max" (целые, 0 если неважно), '
    '"instrumental" (true, если лучше без слов), "size" (10-50). '
    "Жанры: " + ", ".join(GENRES) + ". Настроения: " + ", ".join(MOODS) + "."
)


def clean_plan(plan: dict, prompt: str, source: str) -> dict:
    base = rule_plan(prompt)
    out = {
        "title": str(plan.get("title") or base["title"])[:60],
        "description": str(plan.get("description") or base["description"])[:200],
        "queries": [str(q)[:60] for q in (plan.get("queries") or base["queries"])][:6],
        "genres": [g for g in (plan.get("genres") or []) if g in GENRES][:4],
        "moods": [m for m in (plan.get("moods") or []) if m in MOODS][:4],
        "instrumental": bool(plan.get("instrumental")),
        "source": source,
    }
    for k in ("bpm_min", "bpm_max", "size"):
        try:
            out[k] = int(plan.get(k) or 0)
        except (TypeError, ValueError):
            out[k] = 0
    out["size"] = max(10, min(50, out["size"] or 25))
    return out


def gptunnel_plan(prompt: str) -> dict:
    import urllib.request
    body = json.dumps({
        "model": os.environ.get("GPTUNNEL_MODEL") or "gpt-4o",
        "messages": [{"role": "system", "content": PLAN_SYSTEM}, {"role": "user", "content": prompt[:500]}],
    }).encode()
    req = urllib.request.Request("https://gptunnel.ru/v1/chat/completions", data=body, method="POST", headers={
        "Authorization": os.environ["GPTUNNEL_API_KEY"], "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=40) as r:
        data = json.loads(r.read())
    text = data["choices"][0]["message"]["content"]
    m = re.search(r"\{[\s\S]*\}", text)
    if not m:
        raise ValueError("no json in answer")
    return clean_plan(json.loads(m.group(0)), prompt, "gptunnel")


def ai_plan(prompt: str) -> dict:
    if os.environ.get("GPTUNNEL_API_KEY"):
        try:
            return gptunnel_plan(prompt)
        except Exception as e:
            print("gptunnel failed:", type(e).__name__, e)
            return rule_plan(prompt)
    if not os.environ.get("ANTHROPIC_API_KEY"):
        return rule_plan(prompt)
    try:
        import anthropic
        from typing import List
        from pydantic import BaseModel, Field

        class Plan(BaseModel):
            title: str = Field(description="Короткое название плейлиста на русском, 2-4 слова")
            description: str = Field(description="Одно предложение на русском: что за подборка")
            queries: List[str] = Field(description="3-6 коротких поисковых запросов на английском для каталога независимой музыки")
            genres: List[str] = Field(description="0-4 жанра строго из списка")
            moods: List[str] = Field(description="0-4 настроения строго из списка")
            bpm_min: int = Field(description="Минимальный темп, 0 если неважно")
            bpm_max: int = Field(description="Максимальный темп, 0 если неважно")
            instrumental: bool = Field(description="True, если лучше без слов (учёба, сон, фокус)")
            size: int = Field(description="Сколько треков, 10-50")

        client = anthropic.Anthropic()
        resp = client.messages.parse(
            model="claude-opus-5-5",
            max_tokens=2000,
            output_config={"effort": "low"},
            system=(
                "Ты музыкальный куратор сервиса независимой музыки (каталоги Audius и Jamendo). "
                "Преврати просьбу пользователя в план поиска. Жанры выбирай только из: " + ", ".join(GENRES) +
                ". Настроения только из: " + ", ".join(MOODS) + ". Запросы пиши на английском, как теги."
            ),
            messages=[{"role": "user", "content": prompt[:500]}],
            output_format=Plan,
        )
        if resp.stop_reason == "refusal" or resp.parsed_output is None:
            return rule_plan(prompt)
        plan = resp.parsed_output.model_dump()
        plan["genres"] = [g for g in plan["genres"] if g in GENRES]
        plan["moods"] = [m for m in plan["moods"] if m in MOODS]
        plan["size"] = max(10, min(50, plan["size"]))
        plan["source"] = "claude"
        return plan
    except Exception as e:  # any API/network problem: fall back to rules so the feature still works
        print("AI plan failed:", type(e).__name__, e)
        return rule_plan(prompt)


# ---------- catalog proxy (Audius / Jamendo) ----------
# Browsers in some networks cannot reach Audius (Cloudflare) or its storage nodes, so every catalog
# request, audio stream and cover goes through this server. Only fixed upstream routes are allowed.
import urllib.request
import urllib.error

AU_API = "https://api.audius.co/v1"
AU_APP = "EchoMusic"
AU_ROUTES = [re.compile(p) for p in (r"tracks/trending", r"tracks/trending/underground", r"tracks/search",
                                     r"users/[A-Za-z0-9]{1,16}/tracks", r"tracks/[A-Za-z0-9]{1,16}")]
AU_QUERY = {"query", "limit", "time", "genre", "only_downloadable", "offset"}
JA_API = "https://api.jamendo.com/v3.0/tracks/"
JA_QUERY = {"search", "limit", "order", "fuzzytags", "tags", "vocalinstrumental", "id", "offset"}
UA = {"User-Agent": "EchoMusic/1.0 (+https://music.maxsim.space)"}
_cache, _cache_lock = {}, threading.Lock()


def cached_json(url, ttl=300):
    now = time.time()
    with _cache_lock:
        hit = _cache.get(url)
        if hit and now - hit[0] < ttl:
            return hit[1]
    with urllib.request.urlopen(urllib.request.Request(url, headers={**UA, "Accept": "application/json"}), timeout=15) as r:
        value = json.loads(r.read(8_000_000))
    with _cache_lock:
        if len(_cache) > 800:
            _cache.clear()
        _cache[url] = (now, value)
    return value


# ---- catalog search in Apple Music and Spotify: names and covers only, the audio always comes from elsewhere ----
IMG_HOSTS = re.compile(r"^https://(?:is\d-ssl\.mzstatic\.com|i\.scdn\.co)/[A-Za-z0-9/_.,%=+-]{1,300}$")


def apple_search(q, country="us"):
    country = country if re.fullmatch(r"[a-z]{2}", country or "") else "us"
    url = "https://itunes.apple.com/search?" + urllib.parse.urlencode(
        {"term": q[:120], "media": "music", "entity": "song", "limit": 40, "country": country})
    data = cached_json(url, ttl=6 * 3600)
    out = []
    for r in data.get("results") or []:
        if r.get("kind") != "song" or not r.get("trackName"):
            continue
        art = (r.get("artworkUrl100") or "").replace("100x100bb", "600x600bb")
        out.append({"id": str(r.get("trackId")), "title": r["trackName"], "artist": r.get("artistName", ""),
                    "album": r.get("collectionName", ""), "duration": round((r.get("trackTimeMillis") or 0) / 1000),
                    "cover": art, "genre": r.get("primaryGenreName", ""), "link": r.get("trackViewUrl", ""),
                    "year": (r.get("releaseDate") or "")[:4], "explicit": r.get("trackExplicitness") == "explicit"})
    return out


_sp_token = {"v": "", "exp": 0}
_sp_lock = threading.Lock()


def spotify_token():
    with _sp_lock:
        if _sp_token["v"] and time.time() < _sp_token["exp"] - 60:
            return _sp_token["v"]
        import base64
        auth = base64.b64encode(f"{CFG['spotify_id']}:{CFG['spotify_secret']}".encode()).decode()
        req = urllib.request.Request("https://accounts.spotify.com/api/token", data=b"grant_type=client_credentials",
                                     headers={**UA, "Authorization": "Basic " + auth,
                                              "Content-Type": "application/x-www-form-urlencoded"})
        with urllib.request.urlopen(req, timeout=15) as r:
            d = json.loads(r.read(100_000))
        _sp_token.update(v=d["access_token"], exp=time.time() + int(d.get("expires_in", 3600)))
        return _sp_token["v"]


def spotify_search(q, market="US"):
    market = market.upper() if re.fullmatch(r"[A-Za-z]{2}", market or "") else "US"
    url = "https://api.spotify.com/v1/search?" + urllib.parse.urlencode({"q": q[:120], "type": "track", "limit": 40, "market": market})
    with _cache_lock:
        hit = _cache.get(url)
        if hit and time.time() - hit[0] < 6 * 3600:
            return hit[1]
    req = urllib.request.Request(url, headers={**UA, "Authorization": "Bearer " + spotify_token(), "Accept": "application/json"})
    with urllib.request.urlopen(req, timeout=15) as r:
        data = json.loads(r.read(4_000_000))
    out = []
    for t in (data.get("tracks") or {}).get("items") or []:
        if not t or not t.get("name"):
            continue
        imgs = (t.get("album") or {}).get("images") or []
        out.append({"id": t.get("id", ""), "title": t["name"], "artist": ", ".join(a.get("name", "") for a in t.get("artists") or []),
                    "album": (t.get("album") or {}).get("name", ""), "duration": round((t.get("duration_ms") or 0) / 1000),
                    "cover": imgs[0]["url"] if imgs else "", "genre": "", "link": (t.get("external_urls") or {}).get("spotify", ""),
                    "year": ((t.get("album") or {}).get("release_date") or "")[:4], "explicit": bool(t.get("explicit")),
                    "popularity": t.get("popularity", 0)})
    with _cache_lock:
        _cache[url] = (time.time(), out)
    return out


_apple_bad = {}


def apple_top(cc="ru"):
    """Apple Music «most played» chart from Apple's public marketing RSS (names and covers only)."""
    cc = cc if re.fullmatch(r"[a-z]{2}", cc or "") else "ru"
    last = None
    for c in dict.fromkeys([cc, "ru"]):
        if time.time() - _apple_bad.get(c, 0) < 600:
            continue  # this storefront did not answer recently: do not make the listener wait for it again
        try:
            url = f"https://rss.marketingtools.apple.com/api/v2/{c}/music/most-played/100/songs.json"
            with _cache_lock:
                hit = _cache.get(url)
            if hit and time.time() - hit[0] < 6 * 3600:
                data = hit[1]
            else:
                with urllib.request.urlopen(urllib.request.Request(url, headers={**UA, "Accept": "application/json"}), timeout=10) as r:
                    data = json.loads(r.read(4_000_000))
                with _cache_lock:
                    _cache[url] = (time.time(), data)
            out = []
            for r in (data.get("feed") or {}).get("results") or []:
                genres = [g.get("name", "") for g in r.get("genres") or [] if g.get("name") not in ("Музыка", "Music")]
                out.append({"id": str(r.get("id", "")), "title": r.get("name", ""), "artist": r.get("artistName", ""),
                            "cover": (r.get("artworkUrl100") or "").replace("100x100bb", "600x600bb"),
                            "link": r.get("url", ""), "genre": genres[0] if genres else ""})
            if out:
                return {"cc": c, "data": out}
            _apple_bad[c] = time.time()
        except (urllib.error.URLError, TimeoutError, OSError, ValueError) as e:
            _apple_bad[c] = time.time()
            last = e
    raise last or ValueError("empty chart")


_sp_covers, _sp_cov_lock = {}, threading.Lock()


def spotify_cover_url(tid):
    """Cover of a Spotify track via Spotify's public oEmbed (no key)."""
    with _sp_cov_lock:
        if tid in _sp_covers:
            return _sp_covers[tid]
    d = cached_json(f"https://open.spotify.com/oembed?url=https://open.spotify.com/track/{tid}", ttl=7 * 86400)
    url = d.get("thumbnail_url") or ""
    if not re.fullmatch(r"https://(?:image-cdn-[a-z]{2}\.spotifycdn\.com|i\.scdn\.co)/image/[0-9a-f]{20,64}", url):
        url = ""
    with _sp_cov_lock:
        if len(_sp_covers) > 5000:
            _sp_covers.clear()
        _sp_covers[tid] = url
    return url


# ---------- shared playlists: a snapshot of the tracks under a short public link ----------
SHARE_SRC = {"audius", "jamendo", "local", "youtube", "spotify", "apple"}
SHARE_STR = {"id", "src", "rawId", "title", "artist", "artistId", "genre", "mood", "key", "releaseDate", "quality", "ext",
             "via", "channel", "extName"}
SHARE_URL = {"cover", "coverSm", "stream", "dlOrig", "dlMp3", "link", "extLink"}
SHARE_NUM = {"duration", "bpm", "plays", "views"}


def clean_share_track(t):
    if not isinstance(t, dict) or t.get("src") not in SHARE_SRC:
        return None
    out = {}
    for k in SHARE_STR:
        v = t.get(k)
        if isinstance(v, str) and v:
            out[k] = v[:300]
    for k in SHARE_URL:
        v = t.get(k)
        # only our own API paths or https links: nothing that could run as a script when clicked
        if isinstance(v, str) and len(v) < 600 and (re.fullmatch(r"/api/[A-Za-z0-9/_.%=&?:,+-]*", v) or re.fullmatch(r"https://[^\s<>\"'`]+", v)):
            out[k] = v
    for k in SHARE_NUM:
        v = t.get(k)
        if isinstance(v, (int, float)) and not isinstance(v, bool) and 0 <= v < 1e13:
            out[k] = v
    if isinstance(t.get("downloadable"), bool):
        out["downloadable"] = t["downloadable"]
    if t.get("catalog") is True:
        out["catalog"] = True
    if isinstance(t.get("tags"), list):
        out["tags"] = [str(x)[:40] for x in t["tags"][:6]]
    return out if out.get("id") and out.get("title") else None


def au_url(route, params):
    """Canonical upstream URL (sorted params) so the cache hits whatever order the browser sent."""
    p = dict(params)
    p["app_name"] = AU_APP
    return f"{AU_API}/{route}?{urllib.parse.urlencode(sorted(p.items()))}"


# The wave's first requests, kept warm so "Моя волна" starts without waiting on Audius search (1-7 s).
WARM = [("tracks/trending", {"time": "week", "limit": "60"}), ("tracks/trending", {"time": "month", "limit": "40"}),
        ("tracks/trending/underground", {"limit": "30"})]
WARM += [("tracks/search", {"query": q, "limit": "40"}) for q in
         ("phonk", "memphis phonk", "phonk house", "dark phonk", "drift phonk", "phonk drift", "brazilian phonk", "funk phonk")]
WARM += [("tracks/search", {"query": q, "limit": "25"}) for q in
         ("energy", "workout", "chill", "calm", "lofi study", "focus", "night", "late night", "sad", "melancholy")]
WARM += [("tracks/trending", {"time": "week", "genre": g, "limit": "30"}) for g in
         ("Electronic", "Hip-Hop/Rap", "Lo-Fi", "House", "Trap", "Ambient", "Drum & Bass", "Techno", "Deep House", "Pop", "Rock", "Alternative")]


# ---- apps: the site serves the app files itself, mirrored from the GitHub releases ----
APPS_DIR = DATA / "apps"
APPS_REPO = "maxsim9687/echo-music"
APP_FILES = {
    # short name -> (file, content type)
    "android": ("Echo-Android.apk", "application/vnd.android.package-archive"),
    "windows": ("Echo-Windows-x64.zip", "application/zip"),
    "windows-arm": ("Echo-Windows-arm64.zip", "application/zip"),
    "ios": ("Echo.ipa", "application/octet-stream"),
    "trollstore": ("Echo-TrollStore.tipa", "application/octet-stream"),
}
_apps_lock = threading.Lock()


def apps_manifest():
    try:
        return json.loads((APPS_DIR / "manifest.json").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}


def apps_sync():
    """Download each app file from the newest GitHub release that has it, if it is newer than our copy."""
    APPS_DIR.mkdir(parents=True, exist_ok=True)
    req = urllib.request.Request(f"https://api.github.com/repos/{APPS_REPO}/releases?per_page=15",
                                 headers={**UA, "Accept": "application/vnd.github+json"})
    with urllib.request.urlopen(req, timeout=20) as r:
        releases = json.loads(r.read().decode("utf-8"))
    with _apps_lock:
        man = apps_manifest()
        for key, (name, _ct) in APP_FILES.items():
            hit = None
            for rel in releases:
                if rel.get("draft") or rel.get("prerelease"):
                    continue
                for a in rel.get("assets") or []:
                    if a.get("name") == name and a.get("state") == "uploaded":
                        hit = (rel, a)
                        break
                if hit:
                    break
            if not hit:
                continue
            rel, a = hit
            dest = APPS_DIR / name
            old = man.get(key) or {}
            if old.get("tag") == rel["tag_name"] and dest.is_file() and dest.stat().st_size == a["size"]:
                continue
            tmp = APPS_DIR / (name + ".part")
            h = hashlib.sha256()
            got = 0
            with urllib.request.urlopen(urllib.request.Request(a["browser_download_url"], headers=UA), timeout=60) as r, open(tmp, "wb") as f:
                while True:
                    chunk = r.read(1 << 20)
                    if not chunk:
                        break
                    f.write(chunk)
                    h.update(chunk)
                    got += len(chunk)
            if got != a["size"]:
                tmp.unlink(missing_ok=True)
                print("apps: size mismatch for", name, flush=True)
                continue
            os.replace(tmp, dest)
            man[key] = {"file": name, "tag": rel["tag_name"], "size": got, "sha256": h.hexdigest(),
                        "date": rel.get("published_at") or "", "github": a["browser_download_url"]}
            print("apps: mirrored", name, rel["tag_name"], got, flush=True)
        (APPS_DIR / "manifest.json.tmp").write_text(json.dumps(man, ensure_ascii=False), encoding="utf-8")
        os.replace(APPS_DIR / "manifest.json.tmp", APPS_DIR / "manifest.json")


def apps_loop():
    time.sleep(20)
    while True:
        try:
            apps_sync()
        except Exception as e:  # network trouble: keep serving what we have
            print("apps sync error:", type(e).__name__, e, flush=True)
        time.sleep(1800)


def warm_loop():
    while True:
        for route, params in WARM:
            try:
                url = au_url(route, params)
                with _cache_lock:
                    _cache.pop(url, None)
                cached_json(url, ttl=1800)
            except Exception as e:
                print("warm failed:", route, params.get("query") or params.get("genre") or "", type(e).__name__, flush=True)
            time.sleep(0.5)
        time.sleep(15 * 60)


# ---------- chart songs → their YouTube video, found in the background ----------
# The wave takes hits from the Spotify and Apple Music charts. To play the original (not a cover) each song needs
# its YouTube video id. The server looks them up slowly in the background (at most 50 searches a day, the rest of
# the free quota stays for listeners) and keeps them; the page gets the whole map in one request.
_matched = {"map": {}, "at": 0}


def hit_key(artist, title):
    def nk(s):
        s = re.sub(r"\(.*?\)|\[.*?\]", " ", (s or "").lower())
        s = re.sub(r"feat\.?.*$|ft\..*$", " ", s)
        return re.sub(r"[^a-z0-9а-яё]+", " ", s).strip()
    return nk((artist or "").split(",")[0]) + "|" + nk(title)


def _pick_video(items, artist):
    a = hit_key(artist, "x").split("|")[0]
    for v in items:
        text = hit_key(v.get("channel", "") + " " + v.get("title", ""), "x").split("|")[0]
        if a and a.split(" ")[0] in text and not re.search(r"cover|karaoke|remix|sped up|slowed|nightcore|8d audio|reaction|lyrics? video", v.get("title", ""), re.I):
            return v
    return None


def hits_loop():
    time.sleep(20)
    while True:
        try:
            entries = []  # songs listeners' taste asks for, then the Russian chart, then the rest
            try:
                personal = recsys.match_queue(25)
            except Exception as e:
                personal = []
                print("hits: personal queue", type(e).__name__, flush=True)
            entries += personal
            for src, cc in (("apple", "ru"), ("spotify", "global"), ("spotify", "kz"), ("apple", "us")):
                try:
                    items = apple_top(cc).get("data", []) if src == "apple" else spotify_chart(cc).get("items", [])
                    entries += [(i["artist"], i["title"]) for i in items[:100]]
                except Exception as e:
                    print("hits:", src, cc, type(e).__name__, flush=True)
            known = {}
            for name in YT_LISTS:
                try:
                    for v in yt_list(name):
                        known.setdefault(hit_key(v.get("artist"), v.get("title")), v)
                except Exception:
                    pass
            m = dict(_matched["map"])
            stop = False  # once Google says no, only already-cached searches are used this pass
            for artist, title in entries:
                k = hit_key(artist, title)
                if k in m:
                    continue
                if k in known:
                    v = known[k]
                    m[k] = {"videoId": v["videoId"], "artist": artist, "title": title}
                    continue
                if not CFG["youtube"]:
                    continue
                q = f"{artist.split(',')[0]} {title}"
                with db() as con:
                    cached = con.execute("SELECT 1 FROM yt_cache WHERE q=?", ("v2:" + q.lower().strip()[:120],)).fetchone()
                if not cached and (stop or limited("yt:bg", 50, 86400)):
                    continue  # today's background budget is spent; the rest tomorrow
                try:
                    v = _pick_video(yt_search(q), artist)
                except RuntimeError:
                    stop = True  # the shared daily quota is used up
                    continue
                except urllib.error.HTTPError as e:
                    print("hits: search paused, HTTP", e.code, flush=True)
                    stop = True  # Google says slow down or no quota: new searches again in an hour
                    continue
                except Exception as e:
                    print("hits: search", type(e).__name__, flush=True)
                    time.sleep(2)
                    continue
                if v:
                    m[k] = {"videoId": v["videoId"], "artist": artist, "title": title}
                if not cached:
                    time.sleep(3)
            _matched.update(map=m, at=time.time())
            try:
                recsys.match_done([hit_key(a, t) for a, t in personal if hit_key(a, t) in m])
            except Exception:
                pass
            print("hits: matched", len(m), "of", len(entries), flush=True)
        except Exception as e:
            print("hits loop:", type(e).__name__, e, flush=True)
        time.sleep(3600)


# ---------- song metadata (genre, year, explicit) for the hits, from Apple's public catalog ----------
# The wave's taste model needs a genre for every hit. YouTube and the charts do not give one, Apple's catalog does.
# Looked up slowly in the background (Apple allows ~20 searches a minute) and kept for good.
_meta_json = {"v": None, "at": 0}


def _meta_lookup(artist, title):
    a_key = hit_key(artist, "x").split("|")[0]
    t_key = hit_key("x", title).split("|")[1]
    for cc in ("us", "ru"):
        url = "https://itunes.apple.com/search?" + urllib.parse.urlencode({"term": f"{artist.split(',')[0]} {title}"[:120], "entity": "song", "limit": 5, "country": cc})
        with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=12) as r:
            res = json.loads(r.read(2_000_000)).get("results") or []
        for x in res:
            ak = hit_key(x.get("artistName", ""), "x").split("|")[0]
            tk = hit_key("x", x.get("trackName", "")).split("|")[1]
            if a_key and t_key and a_key.split(" ")[0] in ak and tk.startswith(t_key.split(" ")[0]):
                return x.get("primaryGenreName", ""), int((x.get("releaseDate") or "0")[:4] or 0), x.get("trackExplicitness") == "explicit"
        time.sleep(3.2)
    return "", 0, False


def meta_loop():
    time.sleep(40)
    while True:
        try:
            todo = {}
            for name in YT_LISTS:
                try:
                    for v in yt_list(name):
                        todo.setdefault(hit_key(v.get("artist"), v.get("title")), (v.get("artist", ""), v.get("title", "")))
                except Exception:
                    pass
            for v in _matched["map"].values():
                todo.setdefault(hit_key(v["artist"], v["title"]), (v["artist"], v["title"]))
            with db() as con:
                have = {r["k"] for r in con.execute("SELECT k FROM song_meta")}
            left = [(k, a, t) for k, (a, t) in todo.items() if k not in have and a and t]
            print("meta: to look up", len(left), flush=True)
            for k, a, t in left[:600]:
                try:
                    g, y, ex = _meta_lookup(a, t)
                except urllib.error.HTTPError as e:
                    print("meta: apple HTTP", e.code, flush=True)
                    time.sleep(120)
                    continue
                except Exception:
                    time.sleep(10)
                    continue
                with db() as con:
                    con.execute("INSERT OR REPLACE INTO song_meta(k,genre,year,explicit,at) VALUES(?,?,?,?,?)", (k, g, y, int(ex), int(time.time())))
                _meta_json["v"] = None
                time.sleep(3.2)
        except Exception as e:
            print("meta loop:", type(e).__name__, e, flush=True)
        time.sleep(600)


def meta_all():
    if _meta_json["v"] is None or time.time() - _meta_json["at"] > 600:
        with db() as con:
            rows = con.execute("SELECT k, genre, year, explicit FROM song_meta WHERE genre != ''").fetchall()
        _meta_json.update(v={r["k"]: [r["genre"], r["year"], r["explicit"]] for r in rows}, at=time.time())
    return _meta_json["v"]


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k):
        return None


_no_redirect = urllib.request.build_opener(_NoRedirect)
_streams, _streams_lock = {}, threading.Lock()


def au_stream_url(tid, fresh=False):
    """Signed content-node URL for a track, cached for 10 minutes (saves a redirect round trip per play)."""
    now = time.time()
    with _streams_lock:
        hit = _streams.get(tid)
        if hit and not fresh and now - hit[0] < 600:
            return hit[1]
    loc = None
    try:
        _no_redirect.open(urllib.request.Request(f"{AU_API}/tracks/{tid}/stream?app_name={AU_APP}", headers=UA), timeout=15).close()
    except urllib.error.HTTPError as e:
        if e.code in (301, 302, 303, 307, 308):
            loc = e.headers.get("Location")
    if not loc:
        return None
    with _streams_lock:
        if len(_streams) > 5000:
            _streams.clear()
        _streams[tid] = (now, loc)
    return loc


AU_KEEP = ("id", "title", "duration", "genre", "mood", "tags", "bpm", "musical_key", "release_date", "play_count",
           "is_downloadable", "is_streamable", "is_stream_gated", "permalink")


def slim_audius(payload):
    """Audius sends ~15 KB per track; the site needs a few fields. Cuts responses ~20x."""
    def one(t):
        if not isinstance(t, dict):
            return t
        out = {k: t.get(k) for k in AU_KEEP}
        u = t.get("user") or {}
        out["user"] = {"id": u.get("id"), "name": u.get("name")}
        out["artwork"] = bool(t.get("artwork"))
        return out
    d = payload.get("data")
    if isinstance(d, list):
        return {"data": [one(t) for t in d]}
    if isinstance(d, dict):
        return {"data": one(d)}
    return payload


def safe_upstream(url):
    p = urllib.parse.urlparse(url or "")
    host = p.hostname or ""
    return p.scheme == "https" and "." in host and not re.fullmatch(r"[\d.]+|\[.*\]", host) and host != "localhost"


def ja_track(tid):
    if not CFG["jamendo"]:
        return None
    q = urllib.parse.urlencode({"client_id": CFG["jamendo"], "format": "json", "id": tid, "audioformat": "mp32", "audiodlformat": "flac"})
    res = cached_json(f"{JA_API}?{q}", ttl=1800).get("results") or []
    return res[0] if res else None


# ---------- Spotify daily charts (via kworb.net, a public mirror of Spotify's daily charts) ----------
import html as _html

CHARTS = {"global": "Мир", "us": "США", "gb": "Великобритания", "de": "Германия", "kz": "Казахстан", "by": "Беларусь",
          "ua": "Украина", "br": "Бразилия"}
_charts, _charts_lock = {}, threading.Lock()
_ROW = re.compile(r'<tr[^>]*><td class="np">(\d+)</td>\s*<td class="np">([^<]*)</td>\s*<td class="text mp"><div>(.*?)</div></td>(.*?)</tr>', re.S)
_A = re.compile(r'<a href="\.\./(artist|track)/([A-Za-z0-9]+)\.html">(.*?)</a>')


def spotify_chart(cc):
    now = time.time()
    with _charts_lock:
        hit = _charts.get(cc)
        if hit and now - hit[0] < 3 * 3600:
            return hit[1]
    req = urllib.request.Request(f"https://kworb.net/spotify/country/{cc}_daily.html", headers={"User-Agent": "Mozilla/5.0 EchoMusic"})
    with urllib.request.urlopen(req, timeout=20) as r:
        page = r.read(3_000_000).decode("utf-8", "replace")
    m = re.search(r"Spotify Daily Chart[^<]*-\s*([0-9/\-]+)", page)
    items = []
    for pos, change, cell, rest in _ROW.findall(page):
        links = _A.findall(cell)
        artists = [_html.unescape(n) for kind, _, n in links if kind == "artist"]
        track = next(((tid, _html.unescape(n)) for kind, tid, n in links if kind == "track"), None)
        if not track or not artists:
            continue
        streams = next((int(n.replace(",", "")) for n in re.findall(r"<td>([\d,]{5,})</td>", rest)), None)
        items.append({"pos": int(pos), "change": change.strip(), "artist": artists[0], "with": artists[1:4], "title": track[1],
                      "spotify": track[0], "streams": streams})
        if len(items) >= 100:
            break
    data = {"country": cc, "name": CHARTS[cc], "date": m.group(1) if m else "", "items": items}
    if items:
        with _charts_lock:
            _charts[cc] = (now, data)
    return data


# ---------- YouTube (official embedded player only: listening, no downloads) ----------
# The API key is restricted to this server's IPv4 address, so Google APIs are always reached over IPv4.
import socket as _socket
_orig_getaddrinfo = _socket.getaddrinfo


def _ipv4_for_google(host, *args, **kwargs):
    res = _orig_getaddrinfo(host, *args, **kwargs)
    if isinstance(host, str) and host.endswith("googleapis.com"):
        v4 = [r for r in res if r[0] == _socket.AF_INET]
        return v4 or res
    return res


_socket.getaddrinfo = _ipv4_for_google
def yt_search(q):
    """Search music videos via YouTube Data API v3. Results are cached in SQLite for 14 days because the free
    quota is ~100 searches a day."""
    key = "v2:" + q.lower().strip()[:120]
    now = int(time.time())
    with db() as con:
        row = con.execute("SELECT data, at FROM yt_cache WHERE q=?", (key,)).fetchone()
    if row and now - row["at"] < 14 * 86400:
        return json.loads(row["data"])
    if limited("yt:all", 90, 86400):
        raise RuntimeError("quota")
    params = urllib.parse.urlencode({"part": "snippet", "type": "video", "videoCategoryId": "10", "maxResults": "15",
                                     "videoEmbeddable": "true", "q": q[:120], "key": CFG["youtube"]})
    with urllib.request.urlopen(urllib.request.Request(f"https://www.googleapis.com/youtube/v3/search?{params}", headers=UA), timeout=15) as r:
        data = json.loads(r.read(2_000_000))
    items = []
    for it in data.get("items", []):
        vid = (it.get("id") or {}).get("videoId")
        sn = it.get("snippet") or {}
        if vid and re.fullmatch(r"[A-Za-z0-9_-]{11}", vid):
            raw, channel = _html.unescape(sn.get("title", "")), _html.unescape(sn.get("channelTitle", ""))
            a, t, _ = split_artist_title(raw)
            ch = re.sub(r"\s*-\s*Topic$|VEVO$|\s+Official$", "", channel, flags=re.I).strip()
            items.append({"videoId": vid, "title": raw, "channel": channel, "artist": a or ch, "track": t or raw,
                          "thumb": f"https://i.ytimg.com/vi/{vid}/mqdefault.jpg"})
    with db() as con:
        con.execute("INSERT OR REPLACE INTO yt_cache(q,data,at) VALUES(?,?,?)", (key, json.dumps(items, ensure_ascii=False), now))
    return items


# ---------- YouTube music catalog (video IDs from kworb.net public lists; played only via the official player) ----------
YT_LISTS = {"daily": "https://kworb.net/youtube/", "top": "https://kworb.net/youtube/topvideos.html",
            "trending": "https://kworb.net/youtube/trending_music.html"}
_YT_ROW = re.compile(r'<a href="(?:/youtube/trending/)?video/([A-Za-z0-9_-]{11})\.html">(.*?)</a>(.*?)</tr>', re.S)
_YT_NOISE = re.compile(r"\s*[\(\[\|][^\)\]]*?(official|video|audio|lyric|letra|visualizer|mv|m/v|hd|4k|live|en vivo|clip)[^\)\]]*[\)\]]?", re.I)
_yt_lists, _yt_lock = {}, threading.Lock()


def split_artist_title(raw):
    t = _html.unescape(re.sub(r"<[^>]+>", "", raw)).strip()
    t = re.sub(r"\s+", " ", t)
    clean = _YT_NOISE.sub("", t).strip(" -|")
    clean = re.sub(r"\s+(official\s+)?(music\s+)?(m/?v|video|audio)\s*$", "", clean, flags=re.I).strip()
    for sep in (" - ", " – ", " — ", " | "):
        if sep in clean:
            a, b = clean.split(sep, 1)
            b = re.split(r"\s\|\|?\s|\s//\s", b)[0]
            return a.strip(), b.strip(" -")[:120], t
    return "", clean[:120], t


def yt_list(name):
    now = time.time()
    with _yt_lock:
        hit = _yt_lists.get(name)
        if hit and now - hit[0] < 6 * 3600:
            return hit[1]
    req = urllib.request.Request(YT_LISTS[name], headers={"User-Agent": "Mozilla/5.0 EchoMusic"})
    with urllib.request.urlopen(req, timeout=25) as r:
        page = r.read(6_000_000).decode("utf-8", "replace")
    items, seen = [], set()
    for vid, raw, rest in _YT_ROW.findall(page):
        if vid in seen:
            continue
        seen.add(vid)
        artist, title, full = split_artist_title(raw)
        nums = [int(n.replace(",", "")) for n in re.findall(r"<td>([\d,]{4,})</td>", rest)]
        hl = re.search(r'<div>(#[^<]+)</div>', rest)
        items.append({"videoId": vid, "artist": artist, "title": title or full, "full": full,
                      "views": nums[0] if nums else None, "rank": len(items) + 1,
                      "regions": re.findall(r"#\d+ ([A-Za-z ]+)", hl.group(1)) if hl else []})
        if len(items) >= 2500:
            break
    if items:
        with _yt_lock:
            _yt_lists[name] = (now, items)
    return items


def yt_bad_ids():
    with db() as con:
        return {r["id"] for r in con.execute("SELECT id FROM yt_bad")}


# ---------- playlist import by link (Spotify, Apple Music, Deezer, YouTube / YouTube Music) ----------
# Only public track lists are read (titles and artists). Audio is never fetched from these services: the site then
# finds each song in its own playable sources (uploads, Audius, Jamendo, YouTube official player).
BROWSER_UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130 Safari/537.36"
_import_cache, _import_lock = {}, threading.Lock()


def _get(url, headers=None, limit=8_000_000):
    h = {"User-Agent": BROWSER_UA, "Accept-Language": "en-US,en;q=0.8", **(headers or {})}
    with urllib.request.urlopen(urllib.request.Request(url, headers=h), timeout=25) as r:
        return r.read(limit).decode("utf-8", "replace")


def _walk(o):
    if isinstance(o, dict):
        yield o
        for v in o.values():
            yield from _walk(v)
    elif isinstance(o, list):
        for v in o:
            yield from _walk(v)


def import_spotify(kind, sid):
    page = _get(f"https://open.spotify.com/embed/{kind}/{sid}")
    m = re.search(r'<script id="__NEXT_DATA__" type="application/json">(.*?)</script>', page, re.S)
    if not m:
        raise ValueError("Spotify не отдал список треков")
    data = json.loads(m.group(1))
    ent = next((d for d in _walk(data) if isinstance(d.get("trackList"), list) or (d.get("type") == "track" and d.get("title"))), None)
    if not ent:
        raise ValueError("Не удалось прочитать плейлист Spotify")
    name = ent.get("name") or ent.get("title") or "Spotify"
    tracks = []
    if isinstance(ent.get("trackList"), list):
        for t in ent["trackList"]:
            tracks.append({"title": t.get("title", ""), "artist": t.get("subtitle", ""), "duration": round((t.get("duration") or 0) / 1000)})
    else:
        artists = ", ".join(a.get("name", "") for a in ent.get("artists", []) if isinstance(a, dict))
        tracks.append({"title": ent.get("title", ""), "artist": artists or ent.get("subtitle", ""), "duration": round((ent.get("duration") or 0) / 1000)})
    return "Spotify", name, tracks


def import_apple(url):
    page = _get(url)
    m = re.search(r'<script type="application/json" id="serialized-server-data">(.*?)</script>', page, re.S)
    title = re.search(r'<meta property="og:title" content="([^"]*)"', page)
    if not m:
        raise ValueError("Apple Music не отдал список треков")
    data = json.loads(m.group(1))
    tracks, seen = [], set()
    for d in _walk(data):
        name = d.get("title") or d.get("name")
        artist = d.get("artistName") or (d.get("subtitleLinks") or [{}])[0].get("title") if isinstance(d.get("subtitleLinks"), list) else d.get("artistName")
        if isinstance(name, str) and isinstance(artist, str) and (d.get("duration") or d.get("durationInMilliseconds") or "contentDescriptor" in d):
            key = (name.lower(), artist.lower())
            if key in seen:
                continue
            seen.add(key)
            dur = d.get("duration") or d.get("durationInMilliseconds") or 0
            tracks.append({"title": name, "artist": artist, "duration": round(dur / 1000) if dur > 10000 else dur})
    return "Apple Music", _html.unescape(title.group(1)).split(" on Apple Music")[0] if title else "Apple Music", tracks


def import_deezer(kind, did):
    base = json.loads(_get(f"https://api.deezer.com/{kind}/{did}"))
    if base.get("error"):
        raise ValueError("Deezer: " + str(base["error"].get("message", "ошибка")))
    if kind == "track":
        return "Deezer", base.get("title", "Deezer"), [{"title": base.get("title", ""), "artist": (base.get("artist") or {}).get("name", ""), "duration": base.get("duration", 0)}]
    items, url = [], f"https://api.deezer.com/{kind}/{did}/tracks?limit=200"
    while url and len(items) < 1000:
        page = json.loads(_get(url))
        items += page.get("data", [])
        url = page.get("next")
    return "Deezer", base.get("title", "Deezer"), [{"title": t.get("title", ""), "artist": (t.get("artist") or {}).get("name", ""), "duration": t.get("duration", 0)} for t in items]


def import_youtube(list_id=None, video_id=None):
    if video_id and not list_id:
        o = json.loads(_get(f"https://www.youtube.com/oembed?url=https://www.youtube.com/watch?v={video_id}&format=json"))
        a, t, _ = split_artist_title(o.get("title", ""))
        return "YouTube", o.get("title", "YouTube"), [{"title": t, "artist": a or o.get("author_name", ""), "videoId": video_id}]
    page = _get(f"https://www.youtube.com/playlist?list={list_id}", {"Cookie": "SOCS=CAI; CONSENT=YES+1"})
    m = re.search(r"var ytInitialData = (\{.*?\});</script>", page, re.S)
    if not m:
        raise ValueError("YouTube не отдал плейлист (возможно, он закрытый)")
    data = json.loads(m.group(1))
    title = next((d["title"] for d in _walk(data) if d.get("@type") == "playlist" and "title" in d), None)
    meta = next((d for d in _walk(data) if "playlistMetadataRenderer" in d), None)
    if meta:
        title = meta["playlistMetadataRenderer"].get("title", title)
    tracks = []
    for d in _walk(data):
        lv = d.get("lockupViewModel")
        if lv and lv.get("contentType") == "LOCKUP_CONTENT_TYPE_VIDEO" and re.fullmatch(r"[A-Za-z0-9_-]{11}", lv.get("contentId", "")):
            md = (lv.get("metadata") or {}).get("lockupMetadataViewModel") or {}
            raw = ((md.get("title") or {}).get("content")) or ""
            ch = re.search(r'"a11yLabel": ?"Go to channel ([^"]+)"', json.dumps(md, ensure_ascii=False))
            channel = ch.group(1) if ch else ""
            a, t, _ = split_artist_title(raw)
            if not a and " - " not in raw:
                a = channel
            tracks.append({"title": t or raw, "artist": re.sub(r"\s*-\s*Topic$|VEVO$", "", a or channel), "videoId": lv["contentId"]})
            continue
        r = d.get("playlistVideoRenderer")
        if not r or not r.get("videoId"):
            continue
        raw = "".join(x.get("text", "") for x in (r.get("title") or {}).get("runs", []))
        channel = "".join(x.get("text", "") for x in (r.get("shortBylineText") or {}).get("runs", []))
        a, t, _ = split_artist_title(raw)
        tracks.append({"title": t or raw, "artist": a or re.sub(r"\s*-\s*Topic$|VEVO$", "", channel), "videoId": r["videoId"],
                       "duration": int(r.get("lengthSeconds") or 0)})
    return "YouTube", title or "YouTube", tracks


def import_link(url):
    u = url.strip()
    with _import_lock:
        hit = _import_cache.get(u)
        if hit and time.time() - hit[0] < 3600:
            return hit[1]
    m = re.match(r"https?://open\.spotify\.com/(?:intl-[a-z-]+/)?(playlist|album|track)/([A-Za-z0-9]{10,30})", u)
    if m:
        service, name, tracks = import_spotify(m.group(1), m.group(2))
    elif re.match(r"https?://(?:music|itunes)\.apple\.com/", u):
        service, name, tracks = import_apple(u)
    elif (m := re.match(r"https?://(?:www\.)?deezer\.com/(?:[a-z]{2}/)?(playlist|album|track)/(\d+)", u)):
        service, name, tracks = import_deezer(m.group(1), m.group(2))
    elif re.match(r"https?://(?:www\.|music\.|m\.)?(?:youtube\.com|youtu\.be)/", u):
        q = urllib.parse.parse_qs(urllib.parse.urlparse(u).query)
        vid = (q.get("v") or [None])[0] or (re.search(r"youtu\.be/([A-Za-z0-9_-]{11})", u) or [None, None])[1]
        lst = (q.get("list") or [None])[0]
        if lst and not re.fullmatch(r"[A-Za-z0-9_-]{10,64}", lst):
            raise ValueError("Неверная ссылка на плейлист")
        if vid and not re.fullmatch(r"[A-Za-z0-9_-]{11}", vid):
            raise ValueError("Неверная ссылка на видео")
        service, name, tracks = import_youtube(lst, vid)
    elif re.match(r"https?://music\.yandex\.(ru|com|by|kz)/", u):
        raise ValueError("Яндекс Музыка отдаёт плейлисты только российским адресам, а сервер сайта за рубежом. Пока не поддерживается.")
    else:
        raise ValueError("Ссылка не распознана. Поддерживаются Spotify, Apple Music, Deezer, YouTube и YouTube Music.")
    for t in tracks:
        t["artist"] = re.sub(r"\s+", " ", (t.get("artist") or "").replace("\xa0", " ")).strip()
        t["title"] = re.sub(r"\s+", " ", (t.get("title") or "").replace("\xa0", " ")).strip()
    tracks = [t for t in tracks if t.get("title") and t["title"] != name][:1000]
    out = {"service": service, "name": name, "tracks": tracks}
    with _import_lock:
        if len(_import_cache) > 200:
            _import_cache.clear()
        _import_cache[u] = (time.time(), out)
    return out


# ---------- song lyrics (LRCLIB, open lyrics database; synced LRC when available) ----------
_LYR_JUNK = re.compile(r"\s*[\(\[][^\)\]]*(official|video|audio|lyric|visualizer|mv|hd|4k|prod\.?)[^\)\]]*[\)\]]", re.I)


def clean_title(title, artist):
    t = _LYR_JUNK.sub("", title or "").strip()
    if " - " in t and (not artist or artist.lower() in t.lower().split(" - ")[0]):
        a, t = t.split(" - ", 1)
        artist = artist or a
    t = re.sub(r"\s*(feat\.?|ft\.?)\s.*$", "", t, flags=re.I).strip()
    return t, (artist or "").split(",")[0].strip()


def lyrics(artist, title, duration):
    t, a = clean_title(title, artist)
    key = f"{a.lower()}|{t.lower()}"[:300]
    now = int(time.time())
    with db() as con:
        row = con.execute("SELECT data, at FROM lyrics_cache WHERE k=?", (key,)).fetchone()
    if row and now - row["at"] < 30 * 86400:
        return json.loads(row["data"])
    hdr = {"User-Agent": "EchoMusic/1.0 (+https://music.maxsim.space)"}
    found = None
    try:
        q = urllib.parse.urlencode({"artist_name": a, "track_name": t, **({"duration": int(duration)} if duration else {})})
        with urllib.request.urlopen(urllib.request.Request(f"https://lrclib.net/api/get?{q}", headers=hdr), timeout=12) as r:
            found = json.loads(r.read(2_000_000))
    except urllib.error.HTTPError as e:
        if e.code != 404:
            raise
    if not found or not (found.get("syncedLyrics") or found.get("plainLyrics")):
        q = urllib.parse.urlencode({"q": f"{a} {t}".strip()})
        with urllib.request.urlopen(urllib.request.Request(f"https://lrclib.net/api/search?{q}", headers=hdr), timeout=12) as r:
            res = [x for x in json.loads(r.read(4_000_000)) if x.get("syncedLyrics") or x.get("plainLyrics")]
        if duration:
            res.sort(key=lambda x: (not x.get("syncedLyrics"), abs((x.get("duration") or 0) - duration)))
        found = res[0] if res and (not duration or abs((res[0].get("duration") or 0) - duration) < 15) else None
    data = {"synced": (found or {}).get("syncedLyrics") or "", "plain": (found or {}).get("plainLyrics") or "",
            "instrumental": bool((found or {}).get("instrumental")), "source": "LRCLIB" if found else ""}
    with db() as con:
        con.execute("INSERT OR REPLACE INTO lyrics_cache(k,data,at) VALUES(?,?,?)", (key, json.dumps(data, ensure_ascii=False), now))
    return data


# ---------- rate limits ----------
_hits, _hits_lock = {}, threading.Lock()


def limited(key, limit, window):
    now = time.time()
    with _hits_lock:
        arr = [t for t in _hits.get(key, []) if now - t < window]
        if len(arr) >= limit:
            _hits[key] = arr
            return True
        arr.append(now)
        _hits[key] = arr
        if len(_hits) > 20000:
            _hits.clear()
    return False


# ---------- HTTP ----------
class Handler(BaseHTTPRequestHandler):
    server_version = "Echo/1.0"

    # -- helpers --
    def log_message(self, fmt, *args):
        print("%s %s" % (self.ip(), fmt % args), flush=True)

    def rec_user(self):
        """Who the recommender learns for: the account if signed in, else the browser's anonymous id.
        A guest who signs in keeps what was learned while they were a guest."""
        u = self.user()
        anon = (self.headers.get("X-Anon") or "").strip().lower()
        guest = "a:" + anon if re.fullmatch(r"[0-9a-f]{32}", anon) else None
        if u:
            who = f"u:{u['id']}"
            if guest:
                recsys.merge_guest(guest, who)
            return who
        return guest

    def ip(self):
        xff = self.headers.get("X-Forwarded-For", "") if self.client_address[0] == "127.0.0.1" else ""
        return (xff.split(",")[-1].strip() if xff else self.client_address[0])[:64]

    def end_headers(self):
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("X-Frame-Options", "SAMEORIGIN")
        self.send_header("Referrer-Policy", "strict-origin-when-cross-origin")
        self.send_header("Content-Security-Policy",
                         "default-src 'self'; script-src 'self' 'unsafe-inline' https://www.youtube.com https://s.ytimg.com; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; "
                         "font-src https://fonts.gstatic.com; img-src 'self' data: blob: https://i.ytimg.com; media-src 'self' blob:; connect-src 'self'; "
                         "frame-src https://www.youtube-nocookie.com https://www.youtube.com; "
                         "frame-ancestors 'self'; base-uri 'self'; form-action 'self'")
        super().end_headers()

    def proxy(self, url, cache="public, max-age=86400", download_name=None, kinds=("audio/", "image/"), quiet=False):
        """Stream an upstream file to the client. With quiet=True a failed upstream returns False
        without answering, so the caller can retry another way. Returns True once a response was sent."""
        if not safe_upstream(url):
            if quiet:
                return False
            self.err(502, "Источник недоступен")
            return True
        h = dict(UA)
        rng = self.headers.get("Range", "")
        if re.fullmatch(r"bytes=\d*-\d*", rng):
            h["Range"] = rng
        try:
            r = urllib.request.urlopen(urllib.request.Request(url, headers=h), timeout=25)
        except urllib.error.HTTPError as e:
            if quiet and e.code != 416:
                return False
            self.err(416 if e.code == 416 else 502, "Источник не отдал файл")
            return True
        except (urllib.error.URLError, TimeoutError, OSError):
            if quiet:
                return False
            self.err(504, "Источник не ответил")
            return True
        with r:
            ctype = (r.headers.get("Content-Type") or "").split(";")[0].strip()
            if not ctype.startswith(kinds):
                ctype = "application/octet-stream"
            self.send_response(r.status)
            self.send_header("Content-Type", ctype)
            for k in ("Content-Length", "Content-Range"):
                if r.headers.get(k):
                    self.send_header(k, r.headers[k])
            self.send_header("Accept-Ranges", "bytes")
            self.send_header("Cache-Control", cache)
            if download_name:
                self.send_header("Content-Disposition", "attachment; filename*=UTF-8''" + urllib.parse.quote(download_name))
            self.end_headers()
            if self.command == "HEAD":
                return True
            try:
                while True:
                    chunk = r.read(1 << 16)
                    if not chunk:
                        break
                    self.wfile.write(chunk)
            except (BrokenPipeError, ConnectionResetError, TimeoutError, OSError):
                pass
        return True

    def catalog(self, path, query):
        if limited("cat:" + self.ip(), 600, 600):
            return self.err(429, "Слишком много запросов, подождите минуту")
        q = urllib.parse.parse_qs(query)
        try:
            m = re.fullmatch(r"/api/au/stream/([A-Za-z0-9]{1,16})", path)
            if m:
                tid = m.group(1)
                loc = au_stream_url(tid)
                if loc and self.proxy(loc, cache="private, max-age=3600", kinds=("audio/",), quiet=True):
                    return None
                loc = au_stream_url(tid, fresh=True)  # cached signature expired or node gone: resolve again
                return self.proxy(loc or f"{AU_API}/tracks/{tid}/stream?app_name={AU_APP}", cache="private, max-age=3600", kinds=("audio/",))
            m = re.fullmatch(r"/api/au/art/([A-Za-z0-9]{1,16})/(150|480|1000)", path)
            if m:
                art = (cached_json(f"{AU_API}/tracks/{m.group(1)}?app_name={AU_APP}", ttl=86400).get("data") or {}).get("artwork") or {}
                url = art.get(f"{m.group(2)}x{m.group(2)}") or art.get("480x480") or art.get("150x150")
                return self.proxy(url, cache="public, max-age=604800", kinds=("image/",)) if url else self.err(404, "Нет обложки")
            m = re.fullmatch(r"/api/au/(.+)", path)
            if m and any(r.fullmatch(m.group(1)) for r in AU_ROUTES):
                params = {k: v[0][:200] for k, v in q.items() if k in AU_QUERY}
                return self.send_json(slim_audius(cached_json(au_url(m.group(1), params), ttl=1200)))
            if path == "/api/ja/tracks":
                if not CFG["jamendo"]:
                    return self.send_json({"results": []})
                params = {k: v[0][:200] for k, v in q.items() if k in JA_QUERY}
                params.update(client_id=CFG["jamendo"], format="json", include="musicinfo", audioformat="mp32", audiodlformat="flac")
                return self.send_json(cached_json(f"{JA_API}?{urllib.parse.urlencode(params)}", ttl=600))
            m = re.fullmatch(r"/api/ja/(stream|dl|art)/(\d{1,12})", path)
            if m:
                t = ja_track(m.group(2))
                if not t:
                    return self.err(404, "Трек не найден")
                kind = m.group(1)
                if kind == "art":
                    return self.proxy(t.get("image") or t.get("album_image"), cache="public, max-age=604800", kinds=("image/",))
                if kind == "dl":
                    if not t.get("audiodownload_allowed"):
                        return self.err(403, "Автор не разрешил скачивание")
                    name = re.sub(r'[\\/:*?"<>|]+', "", f"{t.get('artist_name', '')} - {t.get('name', '')}")[:150] + ".flac"
                    return self.proxy(t.get("audiodownload"), cache="private, max-age=3600", download_name=name)
                return self.proxy(t.get("audio"), cache="private, max-age=3600", kinds=("audio/",))
        except (urllib.error.URLError, TimeoutError, OSError, ValueError, KeyError) as e:
            print("catalog error:", type(e).__name__, e, flush=True)
            return self.err(504, "Каталог не ответил")
        return self.err(404, "Не найдено")

    def send_json(self, obj, status=200, headers=None):
        body = json.dumps(obj, ensure_ascii=False, separators=(",", ":")).encode()
        gz = len(body) > 1400 and "gzip" in (self.headers.get("Accept-Encoding") or "")
        if gz:
            import gzip
            body = gzip.compress(body, 5)
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        if gz:
            self.send_header("Content-Encoding", "gzip")
            self.send_header("Vary", "Accept-Encoding")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        for k, v in (headers or {}).items():
            self.send_header(k, v)
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)

    def err(self, status, text):
        self.send_json({"error": text}, status)

    def body_len(self):
        cl = (self.headers.get("Content-Length") or "0").strip()
        return int(cl) if cl.isdigit() else -1

    def read_body(self, limit):
        n = self.body_len()
        if n < 0:
            raise ValueError("bad_length")
        if n > limit:
            raise ValueError("too_large")
        return self.rfile.read(n) if n else b""

    def read_json(self, limit=512 * 1024):
        """Parsed JSON object, or None for anything that is not a JSON object. Size errors propagate."""
        raw = self.read_body(limit)
        try:
            v = json.loads(raw or b"{}")
        except (ValueError, UnicodeDecodeError):
            return None
        return v if isinstance(v, dict) else None

    def user(self):
        c = self.headers.get("Cookie", "")
        m = re.search(r"(?:^|;\s*)sid=([A-Za-z0-9_\-]+)", c)
        if not m:
            return None
        with db() as con:
            row = con.execute("SELECT u.* FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=? AND s.expires>?",
                              (sha(m.group(1)), int(time.time()))).fetchone()
        return dict(row) if row else None

    def role(self, u):
        return "admin" if u and u["email"].lower() in CFG["admins"] else "user"

    def csrf_ok(self):
        return self.headers.get("X-Echo") == "1"

    # -- routing --
    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        path = parsed.path
        if path.startswith(("/api/au/", "/api/ja/")):
            return self.catalog(path, parsed.query)
        if path == "/api/apps":
            man = apps_manifest()
            return self.send_json({k: {kk: v[kk] for kk in ("file", "tag", "size", "sha256", "date")} for k, v in man.items()})
        m = re.fullmatch(r"/dl/(android|windows|windows-arm|ios|trollstore)", path)
        if m:
            name, ctype = APP_FILES[m.group(1)]
            if (APPS_DIR / name).is_file():
                return self.send_file(APPS_DIR / name, ctype, download_name=name, cache="public, max-age=600")
            self.send_response(302)  # not mirrored yet: the same file on GitHub
            self.send_header("Location", f"https://github.com/{APPS_REPO}/releases/latest/download/{name}")
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        if path in ("/apps", "/app", "/download"):
            return self.send_file(PUBLIC / "apps.html", "text/html; charset=utf-8", cache="no-cache")
        if path == "/api/yt/catalog":
            name = (urllib.parse.parse_qs(parsed.query).get("list") or ["daily"])[0]
            if name not in YT_LISTS:
                return self.err(404, "Нет такого списка")
            try:
                bad = yt_bad_ids()
                return self.send_json({"list": name, "data": [v for v in yt_list(name) if v["videoId"] not in bad]})
            except (urllib.error.URLError, TimeoutError, OSError) as e:
                print("yt catalog error:", type(e).__name__, e, flush=True)
                return self.err(504, "Каталог YouTube сейчас недоступен")
        m = re.fullmatch(r"/api/yt/thumb/([A-Za-z0-9_-]{11})", path)
        if m:
            return self.proxy(f"https://i.ytimg.com/vi/{m.group(1)}/hqdefault.jpg", cache="public, max-age=604800", kinds=("image/",))
        if path == "/api/import":
            url = (urllib.parse.parse_qs(parsed.query).get("url") or [""])[0].strip()[:500]
            if not url.startswith(("http://", "https://")):
                return self.err(400, "Вставьте ссылку на плейлист, альбом или трек")
            if limited("imp:" + self.ip(), 40, 3600):
                return self.err(429, "Слишком много импортов, подождите немного")
            try:
                return self.send_json(import_link(url))
            except ValueError as e:
                return self.err(400, str(e))
            except (urllib.error.URLError, TimeoutError, OSError, KeyError, TypeError) as e:
                print("import error:", type(e).__name__, e, flush=True)
                return self.err(504, "Сервис не ответил. Проверьте, что плейлист открытый, и попробуйте ещё раз.")
        if path == "/api/lyrics":
            qs = urllib.parse.parse_qs(parsed.query)
            g = lambda k: (qs.get(k) or [""])[0].strip()[:200]
            if not g("title"):
                return self.err(400, "Нет названия")
            if limited("lyr:" + self.ip(), 120, 3600):
                return self.err(429, "Слишком много запросов")
            try:
                dur = float(g("duration") or 0)
            except ValueError:
                dur = 0
            try:
                return self.send_json(lyrics(g("artist"), g("title"), dur))
            except (urllib.error.URLError, TimeoutError, OSError, ValueError) as e:
                print("lyrics error:", type(e).__name__, e, flush=True)
                return self.err(504, "Тексты сейчас недоступны")
        if path == "/api/yt/search":
            if not CFG["youtube"]:
                return self.err(503, "YouTube не подключён: нужен YOUTUBE_API_KEY на сервере")
            q = (urllib.parse.parse_qs(parsed.query).get("q") or [""])[0].strip()
            if len(q) < 2:
                return self.err(400, "Слишком короткий запрос")
            if limited("yt-ip:" + self.ip(), 60, 3600):
                return self.err(429, "Слишком много запросов к YouTube, подождите")
            try:
                return self.send_json({"data": yt_search(q)})
            except RuntimeError:
                return self.err(429, "Дневной лимит поиска по YouTube исчерпан, попробуйте завтра")
            except (urllib.error.URLError, TimeoutError, OSError, ValueError) as e:
                print("youtube error:", type(e).__name__, e, flush=True)
                return self.err(504, "YouTube не ответил")
        m = re.fullmatch(r"/api/(apple|spotify)/search", path)
        if m:
            qs = urllib.parse.parse_qs(parsed.query)
            q = (qs.get("q") or [""])[0].strip()
            cc = (qs.get("cc") or ["us"])[0].strip()
            if len(q) < 2:
                return self.err(400, "Слишком короткий запрос")
            if m.group(1) == "spotify" and not (CFG["spotify_id"] and CFG["spotify_secret"]):
                return self.err(503, "Поиск по Spotify не подключён: нужен ключ Spotify на сервере")
            if limited(m.group(1) + "-ip:" + self.ip(), 240, 3600):
                return self.err(429, "Слишком много запросов, подождите")
            try:
                return self.send_json({"data": apple_search(q, cc) if m.group(1) == "apple" else spotify_search(q, cc)})
            except (urllib.error.URLError, TimeoutError, OSError, ValueError, KeyError) as e:
                print(m.group(1), "search error:", type(e).__name__, e, flush=True)
                return self.err(504, ("Apple Music" if m.group(1) == "apple" else "Spotify") + " не ответил")
        if path in ("/api/rec/candidates", "/api/rec/explain"):
            who = self.rec_user()
            if not who:
                return self.err(400, "Нет идентификатора слушателя")
            if limited("rec:" + self.ip(), 600, 3600):
                return self.err(429, "Слишком много запросов")
            if path == "/api/rec/explain":
                return self.send_json(recsys.explain(who))
            return self.send_json({"data": recsys.candidates(who)})
        if path == "/api/meta":
            return self.send_json({"map": meta_all()})
        if path == "/api/hits/matched":
            return self.send_json({"map": _matched["map"], "at": int(_matched["at"])})
        if path == "/api/apple/top":
            cc = (urllib.parse.parse_qs(parsed.query).get("cc") or ["ru"])[0].strip()
            try:
                return self.send_json(apple_top(cc))
            except (urllib.error.URLError, TimeoutError, OSError, ValueError) as e:
                print("apple top error:", type(e).__name__, e, flush=True)
                return self.err(504, "Чарт Apple Music сейчас недоступен")
        m = re.fullmatch(r"/api/sp/cover/([A-Za-z0-9]{22})", path)
        if m:
            if limited("spcov:" + self.ip(), 600, 3600):
                return self.err(429, "Слишком много запросов")
            try:
                url = spotify_cover_url(m.group(1))
            except (urllib.error.URLError, TimeoutError, OSError, ValueError):
                url = ""
            return self.proxy(url, cache="public, max-age=604800", kinds=("image/",)) if url else self.err(404, "Нет обложки")
        m = re.fullmatch(r"/api/share/([A-Za-z0-9_-]{8,16})", path)
        if m:
            if limited("shareget:" + self.ip(), 300, 3600):
                return self.err(429, "Слишком много запросов")
            with db() as con:
                row = con.execute("SELECT title, data, at FROM shares WHERE id=?", (m.group(1),)).fetchone()
            if not row:
                return self.err(404, "Ссылка не найдена или устарела")
            d = json.loads(row["data"])
            return self.send_json({"title": row["title"], "desc": d.get("desc", ""), "tracks": d.get("tracks", []), "at": row["at"]})
        if path == "/api/img":
            u = (urllib.parse.parse_qs(parsed.query).get("u") or [""])[0]
            if not IMG_HOSTS.fullmatch(u):
                return self.err(400, "Недопустимый адрес картинки")
            return self.proxy(u, cache="public, max-age=604800", kinds=("image/",))
        if path == "/api/charts":
            return self.send_json({"charts": [{"id": k, "name": v} for k, v in CHARTS.items()]})
        m = re.fullmatch(r"/api/charts/([a-z]{2,6})", path)
        if m:
            if m.group(1) not in CHARTS:
                return self.err(404, "Нет такого чарта")
            if limited("chart:" + self.ip(), 120, 3600):
                return self.err(429, "Слишком много запросов")
            try:
                return self.send_json(spotify_chart(m.group(1)))
            except (urllib.error.URLError, TimeoutError, OSError) as e:
                print("chart error:", type(e).__name__, e, flush=True)
                return self.err(504, "Чарт сейчас недоступен. Попробуйте позже.")
        if path == "/api/me":
            u = self.user()
            return self.send_json({"user": {"email": u["email"], "role": self.role(u)} if u else None,
                                   "jamendo": bool(CFG["jamendo"]), "youtube": bool(CFG["youtube"]),
                                   "spotify": bool(CFG["spotify_id"] and CFG["spotify_secret"])})
        if path == "/api/profile":
            u = self.user()
            if not u:
                return self.err(401, "Войдите, чтобы синхронизировать вкус")
            with db() as con:
                row = con.execute("SELECT data FROM profiles WHERE user_id=?", (u["id"],)).fetchone()
            return self.send_json({"data": json.loads(row["data"]) if row else None})
        if path == "/api/uploads":
            q = (urllib.parse.parse_qs(parsed.query).get("q") or [""])[0].strip().lower()[:100]
            with db() as con:
                rows = [dict(r) for r in con.execute("SELECT * FROM uploads ORDER BY created DESC").fetchall()]
            if q:
                words = q.split()
                rows = [r for r in rows if all(w in f"{r['title']} {r['artist']} {r['genre']} {r['mood']}".lower() for w in words)]
            return self.send_json({"data": rows})
        m = re.fullmatch(r"/media/([a-f0-9]{16})(/cover)?", path)
        if m:
            return self.serve_media(m.group(1), bool(m.group(2)))
        return self.serve_static(path)

    def do_POST(self):
        path = urllib.parse.urlparse(self.path).path
        if not self.csrf_ok():
            return self.err(403, "Запрос отклонён")
        try:
            if path == "/api/auth/code":
                return self.auth_code()
            if path == "/api/auth/verify":
                return self.auth_verify()
            if path == "/api/auth/logout":
                return self.logout()
            if path == "/api/ai/playlist":
                u = self.user()
                if not u:
                    return self.err(401, "Войдите по почте, чтобы ИИшка собирала плейлисты")
                body = self.read_json() or {}
                prompt = str(body.get("prompt", "")).strip()[:500]
                if not prompt:
                    return self.err(400, "Опишите, какой нужен плейлист")
                if limited(f"ai:{u['id']}", 40, 86400) or limited("ai:all", 400, 86400):
                    return self.send_json({"plan": rule_plan(prompt), "limited": True})
                return self.send_json({"plan": ai_plan(prompt)})
            if path == "/api/rec/reset":
                who = self.rec_user()
                if who:
                    recsys.reset(who)
                return self.send_json({"ok": True})
            if path in ("/api/rec/events", "/api/rec/rank"):
                who = self.rec_user()
                if not who:
                    return self.err(400, "Нет идентификатора слушателя")
                if limited("rec:" + self.ip(), 600, 3600):
                    return self.err(429, "Слишком много запросов")
                body = self.read_json(limit=1024 * 1024) or {}
                if path == "/api/rec/events":
                    ev = body.get("events") if isinstance(body.get("events"), list) else []
                    return self.send_json({"ok": True, "stored": recsys.add_events(who, ev)})
                cands = body.get("candidates") if isinstance(body.get("candidates"), list) else []
                ctx = body.get("ctx") if isinstance(body.get("ctx"), dict) else {}
                return self.send_json({"scores": recsys.rank(who, cands, ctx)})
            if path == "/api/share":
                if limited("share:" + self.ip(), 30, 3600) or limited("share:all", 3000, 86400):
                    return self.err(429, "Слишком много ссылок, попробуйте позже")
                body = self.read_json(limit=2 * 1024 * 1024) or {}
                title = re.sub(r"\s+", " ", str(body.get("title", "")))[:80].strip() or "Плейлист"
                desc = re.sub(r"\s+", " ", str(body.get("desc", "")))[:200].strip()
                raw = body.get("tracks") if isinstance(body.get("tracks"), list) else []
                tracks = [c for c in (clean_share_track(t) for t in raw[:500]) if c]
                if not tracks:
                    return self.err(400, "В плейлисте нет треков")
                u = self.user()
                sid = secrets.token_urlsafe(8)
                with db() as con:
                    con.execute("INSERT INTO shares(id,title,data,owner,at) VALUES(?,?,?,?,?)",
                                (sid, title, json.dumps({"desc": desc, "tracks": tracks}, ensure_ascii=False), u["id"] if u else None, int(time.time())))
                return self.send_json({"id": sid, "url": f"{CFG['site_url']}/#pl-{sid}"})
            if path == "/api/yt/bad":
                body = self.read_json() or {}
                vid = str(body.get("id", ""))
                if not re.fullmatch(r"[A-Za-z0-9_-]{11}", vid):
                    return self.err(400, "Неверный id")
                if limited("ytbad:" + self.ip(), 200, 3600):
                    return self.err(429, "Слишком много запросов")
                with db() as con:
                    con.execute("INSERT INTO yt_bad(id,n,at) VALUES(?,1,?) ON CONFLICT(id) DO UPDATE SET n=n+1, at=excluded.at", (vid, int(time.time())))
                return self.send_json({"ok": True})
            if path == "/api/uploads":
                return self.upload_audio()
            m = re.fullmatch(r"/api/uploads/([a-f0-9]{16})/cover", path)
            if m:
                return self.upload_cover(m.group(1))
        except ValueError as e:
            if str(e) == "too_large":
                return self.err(413, "Файл слишком большой")
            if str(e) == "bad_length":
                return self.err(411, "Неверная длина запроса")
            raise
        return self.err(404, "Не найдено")

    def do_PUT(self):
        path = urllib.parse.urlparse(self.path).path
        if not self.csrf_ok():
            return self.err(403, "Запрос отклонён")
        if path == "/api/profile":
            u = self.user()
            if not u:
                return self.err(401, "Войдите, чтобы синхронизировать вкус")
            try:
                body = self.read_json(1024 * 1024)
            except ValueError:
                return self.err(413, "Профиль слишком большой")
            if body is None:
                return self.err(400, "Неверные данные")
            with db() as con:
                con.execute("INSERT INTO profiles(user_id,data,updated) VALUES(?,?,?) ON CONFLICT(user_id) DO UPDATE SET data=excluded.data, updated=excluded.updated",
                            (u["id"], json.dumps(body.get("data"), ensure_ascii=False), int(time.time())))
            return self.send_json({"ok": True})
        return self.err(404, "Не найдено")

    def do_DELETE(self):
        path = urllib.parse.urlparse(self.path).path
        if not self.csrf_ok():
            return self.err(403, "Запрос отклонён")
        m = re.fullmatch(r"/api/uploads/([a-f0-9]{16})", path)
        if m:
            if self.role(self.user()) != "admin":
                return self.err(403, "Только для администратора")
            with db() as con:
                row = con.execute("SELECT * FROM uploads WHERE id=?", (m.group(1),)).fetchone()
                if not row:
                    return self.err(404, "Трек не найден")
                con.execute("DELETE FROM uploads WHERE id=?", (row["id"],))
            for f in UPLOADS.glob(row["id"] + ".*"):
                f.unlink(missing_ok=True)
            return self.send_json({"ok": True})
        return self.err(404, "Не найдено")

    # -- auth: one-time code + magic link by email --
    def auth_code(self):
        body = self.read_json() or {}
        email = str(body.get("email", "")).strip().lower()
        if not EMAIL_RE.match(email):
            return self.err(400, "Проверьте адрес почты")
        if limited("mail-ip:" + self.ip(), 6, 3600) or limited("mail-all", 300, 86400):
            return self.err(429, "Слишком много писем. Попробуйте через час.")
        now = int(time.time())
        code = f"{secrets.randbelow(1_000_000):06d}"
        link_token = secrets.token_urlsafe(32)
        with _db_lock, db() as con:
            row = con.execute("SELECT sent FROM codes WHERE email=?", (email,)).fetchone()
            if row and now - row["sent"] < 45:
                return self.err(429, f"Письмо уже отправлено. Повторить можно через {45 - (now - row['sent'])} с")
            con.execute("INSERT INTO codes(email,code_hash,link_hash,expires,tries,sent) VALUES(?,?,?,?,0,?) "
                        "ON CONFLICT(email) DO UPDATE SET code_hash=excluded.code_hash, link_hash=excluded.link_hash, "
                        "expires=excluded.expires, tries=0, sent=excluded.sent",
                        (email, sha(email + ":" + code), sha("link:" + link_token), now + 600, now))
        link = f"{CFG['site_url']}/#ml-{email.encode().hex()}-{link_token}"
        try:
            send_code_mail(email, code, link)
        except Exception as e:
            print("SMTP error:", type(e).__name__, e, flush=True)
            return self.err(502, "Не удалось отправить письмо. Попробуйте позже.")
        return self.send_json({"ok": True})

    def auth_verify(self):
        body = self.read_json() or {}
        email = str(body.get("email", "")).strip().lower()
        code = re.sub(r"\D", "", str(body.get("code", "")))[:6]
        link = str(body.get("link", ""))[:100]
        if not EMAIL_RE.match(email) or not (code or link):
            return self.err(400, "Неверный код")
        if limited("fail:" + email, 25, 86400) or limited("verify-ip:" + self.ip(), 60, 3600):
            return self.err(429, "Слишком много попыток. Попробуйте завтра.")
        now = int(time.time())
        with _db_lock, db() as con:
            # reserve one attempt atomically before comparing, so parallel guesses cannot exceed the limit
            row = con.execute("UPDATE codes SET tries=tries+1 WHERE email=? AND tries<5 AND expires>? "
                              "RETURNING code_hash, link_hash", (email, now)).fetchone()
            if not row:
                return self.err(400, "Код истёк или попыток больше нет. Запросите новое письмо.")
            ok = bool(link and row["link_hash"] and hmac.compare_digest(row["link_hash"], sha("link:" + link))) or \
                bool(code and hmac.compare_digest(row["code_hash"], sha(email + ":" + code)))
            if not ok:
                return self.err(400, "Ссылка устарела. Запросите новое письмо." if link else "Неверный код")
            con.execute("DELETE FROM codes WHERE email=?", (email,))
            u = con.execute("SELECT * FROM users WHERE email=?", (email,)).fetchone()
            if not u:
                con.execute("INSERT INTO users(email,created) VALUES(?,?)", (email, now))
                u = con.execute("SELECT * FROM users WHERE email=?", (email,)).fetchone()
            token = secrets.token_urlsafe(32)
            con.execute("INSERT INTO sessions(token_hash,user_id,expires) VALUES(?,?,?)", (sha(token), u["id"], now + 60 * 86400))
        cookie = f"sid={token}; Path=/; HttpOnly; SameSite=Lax; Max-Age={60 * 86400}" + ("; Secure" if SECURE_COOKIE else "")
        return self.send_json({"user": {"email": email, "role": self.role(dict(u))}}, headers={"Set-Cookie": cookie})

    def logout(self):
        c = self.headers.get("Cookie", "")
        m = re.search(r"(?:^|;\s*)sid=([A-Za-z0-9_\-]+)", c)
        if m:
            with db() as con:
                con.execute("DELETE FROM sessions WHERE token_hash=?", (sha(m.group(1)),))
        return self.send_json({"ok": True}, headers={"Set-Cookie": "sid=; Path=/; Max-Age=0; HttpOnly; SameSite=Lax"})

    # -- uploads (admin) --
    def upload_audio(self):
        u = self.user()
        if self.role(u) != "admin":
            return self.err(403, "Загружать музыку может только администратор")
        q = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query)
        g = lambda k: (q.get(k) or [""])[0].strip()[:200]
        title, artist, filename = g("title"), g("artist"), g("filename")
        ext = filename.rsplit(".", 1)[-1].lower() if "." in filename else ""
        if ext not in AUDIO_EXT:
            return self.err(400, "Поддерживаются MP3, WAV, FLAC, OGG, M4A")
        if not title or not artist:
            return self.err(400, "Укажите название и исполнителя")
        n = self.body_len()
        if n <= 0:
            return self.err(400, "Пустой файл")
        if n > MAX_AUDIO:
            return self.err(413, "Файл больше 500 МБ")
        uid = secrets.token_hex(8)
        dest = UPLOADS / f"{uid}.{ext}"
        left = n
        with open(dest, "wb") as f:
            while left > 0:
                chunk = self.rfile.read(min(1 << 20, left))
                if not chunk:
                    break
                f.write(chunk)
                left -= len(chunk)
        if left:
            dest.unlink(missing_ok=True)
            return self.err(400, "Загрузка прервалась")
        try:
            bpm = float(g("bpm")) if g("bpm") else None
            dur = float(g("duration")) if g("duration") else None
        except ValueError:
            bpm = dur = None
        with db() as con:
            con.execute("INSERT INTO uploads(id,title,artist,genre,mood,bpm,ext,size,duration,created,user_id) VALUES(?,?,?,?,?,?,?,?,?,?,?)",
                        (uid, title, artist, g("genre"), g("mood"), bpm, ext, n, dur, int(time.time()), u["id"]))
        return self.send_json({"id": uid})

    def upload_cover(self, uid):
        if self.role(self.user()) != "admin":
            return self.err(403, "Только для администратора")
        ctype = (self.headers.get("Content-Type") or "").split(";")[0].strip()
        ext = IMAGE_TYPES.get(ctype)
        if not ext:
            return self.err(400, "Обложка должна быть JPG, PNG или WEBP")
        data = self.read_body(MAX_IMAGE)
        with db() as con:
            if not con.execute("SELECT 1 FROM uploads WHERE id=?", (uid,)).fetchone():
                return self.err(404, "Трек не найден")
            (UPLOADS / f"{uid}.cover.{ext}").write_bytes(data)
            con.execute("UPDATE uploads SET cover=? WHERE id=?", (ext, uid))
        return self.send_json({"ok": True})

    # -- files --
    def serve_media(self, uid, cover):
        with db() as con:
            row = con.execute("SELECT * FROM uploads WHERE id=?", (uid,)).fetchone()
        if not row:
            return self.err(404, "Не найдено")
        if cover:
            if not row["cover"]:
                return self.err(404, "Нет обложки")
            return self.send_file(UPLOADS / f"{uid}.cover.{row['cover']}", f"image/{'jpeg' if row['cover'] == 'jpg' else row['cover']}")
        q = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query)
        dl = None
        if q.get("download"):
            safe = re.sub(r'[\\/:*?"<>|]+', "", f"{row['artist']} - {row['title']}")[:150]
            dl = f"{safe}.{row['ext']}"
        return self.send_file(UPLOADS / f"{uid}.{row['ext']}", AUDIO_EXT[row["ext"]], download_name=dl)

    def serve_static(self, path):
        rel = urllib.parse.unquote(path).lstrip("/") or "index.html"
        try:
            target = (PUBLIC / rel).resolve()
            inside = PUBLIC.resolve() in target.parents
        except (ValueError, OSError):
            return self.err(404, "Не найдено")
        if not inside or not target.is_file():
            target = PUBLIC / "index.html"
        ctype = STATIC_TYPES.get(target.suffix.lower()) or mimetypes.guess_type(str(target))[0] or "application/octet-stream"
        return self.send_file(target, ctype, cache="no-cache")

    def send_file(self, p: Path, ctype, download_name=None, cache="public, max-age=86400"):
        if not p.is_file():
            return self.err(404, "Файл не найден")
        size = p.stat().st_size
        start, end = 0, size - 1
        rng = self.headers.get("Range")
        status = 200
        if rng:
            m = re.match(r"bytes=(\d*)-(\d*)", rng)
            if m:
                if m.group(1):
                    start = int(m.group(1))
                    if m.group(2):
                        end = min(int(m.group(2)), size - 1)
                elif m.group(2):
                    start = max(0, size - int(m.group(2)))
                if start > end or start >= size:
                    self.send_response(416)
                    self.send_header("Content-Range", f"bytes */{size}")
                    self.end_headers()
                    return
                status = 206
        self.send_response(status)
        self.send_header("Content-Type", ctype)
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Length", str(end - start + 1))
        self.send_header("Cache-Control", cache)
        if status == 206:
            self.send_header("Content-Range", f"bytes {start}-{end}/{size}")
        if download_name:
            self.send_header("Content-Disposition", "attachment; filename*=UTF-8''" + urllib.parse.quote(download_name))
        self.end_headers()
        if self.command == "HEAD":
            return
        with open(p, "rb") as f:
            f.seek(start)
            left = end - start + 1
            try:
                while left > 0:
                    chunk = f.read(min(1 << 16, left))
                    if not chunk:
                        break
                    self.wfile.write(chunk)
                    left -= len(chunk)
            except (BrokenPipeError, ConnectionResetError):
                pass

    do_HEAD = do_GET


if __name__ == "__main__":
    init_db()
    threading.Thread(target=warm_loop, daemon=True).start()
    threading.Thread(target=hits_loop, daemon=True).start()
    threading.Thread(target=meta_loop, daemon=True).start()
    threading.Thread(target=apps_loop, daemon=True).start()
    recsys.init(db=db, hit_key=hit_key, yt_list=yt_list, yt_lists=lambda: list(YT_LISTS),
                matched=lambda: _matched["map"], ua=UA)
    srv = ThreadingHTTPServer((os.environ.get("HOST", "127.0.0.1"), CFG["port"]), Handler)
    print(f"Эхо запущено: http://localhost:{CFG['port']}  (админы: {len(CFG['admins'])}, почта: {'да' if CFG['smtp_host'] else 'нет'})")
    srv.serve_forever()
