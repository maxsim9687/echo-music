# Эхо — native apps spec (iOS SwiftUI + Windows WinUI 3)

Both apps are **real native apps**, not web views. No WKWebView / WebView2 anywhere. They talk to the site's JSON API.
UI language: Russian. Product name: «Эхо». Bundle/package id: `space.maxsim.echo`.

The web version in `../public/` (index.html, styles.css, app.js, api.js, recs.js, download.js, fx.js) is the
**reference implementation**: copy its behaviour, texts and look. Read it before writing code.

## Backend API (base `https://music.maxsim.space`)
Every POST/PUT/DELETE must send header `X-Echo: 1`. Auth is a cookie `sid` (HttpOnly, 60 days) set by `/api/auth/verify`;
keep and persist cookies (iOS: `HTTPCookieStorage.shared` + `URLSession.shared` does it; Windows: `HttpClient` with a
`CookieContainer` you serialize to LocalState). Errors come as `{"error": "текст по-русски"}` with a 4xx/5xx status — show that text.

| Method | Path | Notes |
|---|---|---|
| GET | `/api/me` | `{user: {email, role: "admin"\|"user"} \| null, jamendo: bool}` |
| POST | `/api/auth/code` `{email}` | sends an email with a 6-digit code + magic link |
| POST | `/api/auth/verify` `{email, code}` | sets cookie, returns `{user}` |
| POST | `/api/auth/logout` | |
| GET/PUT | `/api/profile` | `{data: <profile JSON, same shape as web recs.js P>}`; PUT body `{data: P}` (≤1 MB). Merge like `mergeProfile` in recs.js |
| GET | `/api/au/tracks/trending?time=week\|month&limit=N&genre=G` | Audius. `{data:[track]}` |
| GET | `/api/au/tracks/trending/underground?limit=N` | |
| GET | `/api/au/tracks/search?query=Q&limit=N[&only_downloadable=true]` | |
| GET | `/api/au/users/{userId}/tracks?limit=N` | |
| GET | `/api/au/stream/{trackId}` | audio/mpeg 320 kbps, supports Range |
| GET | `/api/au/art/{trackId}/{150\|480\|1000}` | jpeg cover |
| GET | `/api/ja/tracks?search=&limit=&order=popularity_month\|relevance&fuzzytags=a+b&vocalinstrumental=instrumental` | Jamendo raw `{results:[...]}` (only if `/api/me` jamendo=true) |
| GET | `/api/ja/stream/{id}` · `/api/ja/dl/{id}` (FLAC) · `/api/ja/art/{id}` | |
| GET | `/api/uploads[?q=text]` | admin-uploaded tracks `{data:[{id,title,artist,genre,mood,bpm,ext,size,duration,cover}]}`; `q` filters by title/artist/genre |
| GET | `/media/{id}` · `/media/{id}?download=1` · `/media/{id}/cover` | upload audio (original file) / cover |
| POST | `/api/uploads?title=&artist=&genre=&mood=&bpm=&filename=&duration=` raw file body | admin only |
| POST | `/api/uploads/{id}/cover` raw image body (Content-Type image/jpeg\|png\|webp) | admin only |
| DELETE | `/api/uploads/{id}` | admin only |
| GET | `/api/charts` → `{charts:[{id,name}]}` · `/api/charts/{cc}` → `{country,name,date,items:[{pos,change,artist,with,title,spotify,streams}]}` | Spotify daily charts |
| POST | `/api/ai/playlist` `{prompt}` | needs login; `{plan:{title,description,queries,genres,moods,bpm_min,bpm_max,instrumental,size,source}}` |

Audius track (slim): `{id,title,duration,genre,mood,tags,bpm,musical_key,release_date,play_count,is_downloadable,is_streamable,is_stream_gated,permalink,user:{id,name},artwork:bool}`.
Normalize every source into one Track model exactly like `fromAudius` / `fromJamendo` / `fromLocal` in api.js
(fields: id with prefix `au:`/`ja:`/`lo:`, src, rawId, title, artist, artistId, cover, coverSm, duration, genre, mood, tags,
bpm, key, releaseDate, plays, downloadable, quality, stream, dlOrig, dlMp3, ext). Filter Audius like `okAudius`.

## Features (all required)
1. **Главная (like Spotify, information-dense)**, top to bottom:
   - «Моя волна» hero card: big title, mood chips (Энергия, Спокойствие, Фокус, Ночь, Грусть, Phonk, Drift Phonk), «Настроить» panel
     (Знакомое↔новое slider, Андеграунд↔хиты slider, Темп: любой/спокойный/средний/быстрый, Вокал: любой/без слов/с вокалом,
     «только со скачиванием»), round play button bottom-left with current track title/artist, big cover of current track on the right,
     «Дальше в волне» next track, «Нравится» / «Не то» buttons.
   - Greeting row «Доброе утро/день/вечер» + 6 quick tiles (2×3): recently played tracks/playlists, like Spotify's top grid.
   - «Недавно слушали» horizontal cards.
   - «Для вас» mixes (same 6 as web `mixDefs`: В духе <жанр>, Похоже на <артист>, Андеграунд, Под учёбу, Хиты месяца…).
   - «Любимые артисты» horizontal round avatars (cover of their top track).
   - «Чарты» teaser: top-5 of the selected Spotify chart with «Все чарты» link.
   - «Новое» (Audius trending week, sorted by release_date desc).
   - «Ваша музыка» (admin uploads) if any.
   - «Жанры и настроения» grid of tiles that start a wave with that mood/genre.
2. **Поиск**: one field searching Audius + Jamendo + uploads (`/api/uploads?q=`) in parallel, debounce 350 ms, filters
   «Только со скачиванием», source chips. Plus «ИИшка» card: prompt → `/api/ai/playlist` → gather & rank like web, Слушать / Сохранить.
3. **Чарты**: country chips, top-100 list, per row «Найти на Audius» with Оригинал/Версия/Нет badge (same matching as web
   `matchChart`), «Слушать то, что нашлось», «Открыть в Spotify».
4. **Коллекция**: Любимое, Мои плейлисты, Недавно слушали, Скачанное (offline files).
5. **Плеер**: native audio, background playback, lock screen / media keys (iOS: AVPlayer + AVAudioSession .playback +
   MPNowPlayingInfoCenter + MPRemoteCommandCenter; Windows: MediaPlayer + SystemMediaTransportControls). Mini player bar
   always visible + full-screen player (big cover, seek, prev/play/next, like, download, shuffle, queue list).
   Prefetch next track. After 4 consecutive load errors stop and show a message.
6. **Скачивание**: options exactly like `downloadOptions` in download.js (Audius: MP3 320 + WAV; Jamendo: FLAC + WAV 24-bit + MP3;
   uploads: original + WAV). WAV is decoded locally at the source sample rate (iOS: AVAssetReader → LinearPCM → write RIFF;
   Windows: NAudio `MediaFoundationReader` → `WaveFileWriter`). Downloaded files play offline and appear in «Скачанное».
   iOS: store in app Documents (UIFileSharingEnabled + LSSupportsOpeningDocumentsInPlace = YES so they show in Files) + share sheet.
   Windows: save to `Music\Эхо` and offer «Открыть папку».
7. **Вход по почте**: email → code (6 digits, autofill one-time-code on iOS) → logged in. Show role. Logout clears profile.
   New listener without taste → «Настройка вкуса» screen (genres, favourite artists, favourite songs → likes), reachable from Settings.
8. **Администратор** (role admin): «Загрузка» screen — pick audio file (+optional cover), title/artist/genre/mood/bpm, upload with
   progress, list uploads with delete. Uploaded tracks must appear in search, wave and «Ваша музыка».
9. **Настройки**: 10 themes (same names and colours as web `THEMES` / styles.css `:root[data-v]` tokens), default **10 «Стекло»**;
   taste bars (genres/moods/artists), «Настроить вкус», «Сбросить вкус», sources info, account.
10. **Фон**: animated soft spots like web fx.js (iOS: `TimelineView(.animation)` + `Canvas` with radial gradients and blur;
   Windows: Win2D `CanvasAnimatedControl` or Composition), reacting to touch/pointer and pulsing while playing; per-theme params as in fx.js `CFG`.
   Respect Reduce Motion.

## Design
Monochrome, same tokens as `public/styles.css` (`--bg --fg --muted --line --card --card-2`, per-theme fonts approximated with
system fonts: display = bold/heavy, theme 2/7 serif italic, theme 3 black condensed). Big bold «Моя волна» title, pill buttons,
rounded cards (radius per theme), glass (material/acrylic) in theme 10. Generous spacing, Spotify-like density on Главная.
SF Symbols on iOS, Segoe Fluent Icons on Windows. No emoji icons. Dark by default; light themes 2, 3, 8.

## Recommendation engine v2 («Моя волна 2») — implement exactly this, in its own file (Recs.swift / Recs.cs)
Profile `P` (persisted, synced via /api/profile, same JSON keys as web): `g` genres, `m` moods, `a` artists, `t` tags, `k` keys,
`an` artist names, `fav` favourite artists, `hist` (≤100 slim tracks with `at`), `skip`, `ban`, `banA` (banned artists), `likes`,
`playlists`, `decayAt`, `updated`.
- **Decay**: on load and before scoring, multiply every value in g/m/a/t by `0.5^((now-decayAt)/30 days)`, set decayAt=now.
- **Learning weights** `learn(t,w)`: g += w, m += 0.7w, a += 0.8w, each of first 4 tags += 0.3w; clamp each to [-25, 40].
  Events: like +3 · unlike −2 · finished (≥90 %) +1.5 · listened >30 s +0.5 · skip <10 s −1.5 · skip 10–30 s −0.8 · «Не то» −5 and
  `ban[id]`, and if the same artist got «Не то» twice → `banA[artistId]`.
- **Session vector** (in memory, reset when app restarts or 30 min idle): for each of the last 8 tracks of this session, +1 for
  listened/finished/liked, −1.5 for skipped, on its genre/mood/artist. `session(t)` = sum of the matching entries × 1.5.
  This is the instant reaction: two skips of a genre in a row push it down right away.
- **Candidate generation** (parallel, start playing as soon as ≥6 rankable candidates exist):
  taste (trending in top-3 genres, tracks of top-3 artists, search by top-2 tags), similar-to-liked (for 3 random liked tracks:
  search by their first two tags and by their genre), discovery (underground, trending week, one random genre the user has
  never played = exploration), preset queries for the selected mood, uploads, liked tracks when novelty < 60, Jamendo popular
  by top genres.
- **Score** (cfg = wave settings, fam = 1.5 − novelty/100):
  ```
  s  = fam × (g[genre] + 0.8·m[mood] + 0.9·a[artist] + Σ 0.3·t[tag])
     + session(t)
     + log10(plays+10) × (0.5 + (popularity−50)/30)
     + flow: if current track bpm and t.bpm: −min(|Δbpm|, 40)/10 ; if keys are Camelot-compatible (same, ±1, relative maj/min) +1.2
     + fresh: release within 30 days → +1.5 × novelty/100
     + liked: novelty<40 → +2.5, novelty>70 → −4
     + tempo / vocals / mood-preset bonuses exactly as web recs.js score()
     − played in last 3 days 6 · played this session 20 · in skip list 3 · banned artist ∞ · banned track ∞
     + noise uniform(−0.5,0.5) × (0.6 + novelty/100 × 4.5)
  ```
- **Re-rank** (greedy): no artist repeat within the last 8 queued tracks, max 2 per artist per batch, genre streak of 3 → −2 for the
  same genre next, every 5th slot must be a discovery candidate (genre with |g| < 1) if one exists. Batches of 8, refill when < 4 left.
- Mixes, AI playlists and chart playback reuse `score` with `explore` lowered.

## Build
- iOS: folder `apps/ios`, **XcodeGen** `project.yml` (target «Echo», iOS 17, Swift 5 language mode, bundle `space.maxsim.echo`,
  UIBackgroundModes audio, NSAppTransportSecurity default (HTTPS only), display name «Эхо»). Sources under `apps/ios/Echo/`.
  CI builds unsigned: `xcodegen` → `xcodebuild -scheme Echo -configuration Release -sdk iphoneos CODE_SIGNING_ALLOWED=NO` →
  zip `Payload/Echo.app` as `Echo.ipa`. No third-party packages (SPM) unless unavoidable.
- Windows: folder `apps/windows`, `Echo.sln` + `Echo/Echo.csproj` WinUI 3, .NET 8, Windows App SDK (latest stable 1.x), unpackaged
  (`WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, `SelfContained=true`, RuntimeIdentifier win-x64),
  packages allowed: Microsoft.WindowsAppSDK, Microsoft.Graphics.Win2D, CommunityToolkit.Mvvm, NAudio. App icon + title «Эхо».
  CI: `dotnet publish -c Release -r win-x64` → zip of the publish folder.
