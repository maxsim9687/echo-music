import Foundation

// MARK: - Presets and settings (same as web recs.js)

struct MoodPreset: Sendable {
    let genres: [String]
    let moods: [String]
    let bpmLo: Double
    let bpmHi: Double
    let queries: [String]
    let tags: [String]?

    static let order: [String] = ["Энергия", "Спокойствие", "Фокус", "Ночь", "Грусть", "Phonk", "Drift Phonk"]

    static let all: [String: MoodPreset] = [
        "Энергия": MoodPreset(genres: ["Electronic", "Drum & Bass", "House", "Trap", "Hip-Hop/Rap"], moods: ["Energizing", "Upbeat", "Excited", "Fiery", "Empowering"], bpmLo: 118, bpmHi: 180, queries: ["energy", "workout"], tags: nil),
        "Спокойствие": MoodPreset(genres: ["Ambient", "Lo-Fi", "Acoustic", "Jazz"], moods: ["Peaceful", "Easygoing", "Tender", "Cool"], bpmLo: 55, bpmHi: 100, queries: ["chill", "calm"], tags: nil),
        "Фокус": MoodPreset(genres: ["Lo-Fi", "Ambient", "Electronic", "Classical"], moods: ["Peaceful", "Cool", "Sophisticated", "Serious"], bpmLo: 65, bpmHi: 110, queries: ["lofi study", "focus"], tags: nil),
        "Ночь": MoodPreset(genres: ["Deep House", "Techno", "Downtempo", "R&B/Soul"], moods: ["Brooding", "Cool", "Sensual", "Melancholy"], bpmLo: 88, bpmHi: 126, queries: ["night", "late night"], tags: nil),
        "Грусть": MoodPreset(genres: ["Alternative", "Acoustic", "Ambient", "Lo-Fi"], moods: ["Melancholy", "Sentimental", "Yearning"], bpmLo: 55, bpmHi: 105, queries: ["sad", "melancholy"], tags: nil),
        "Phonk": MoodPreset(genres: ["Trap", "Hip-Hop/Rap", "Electronic"], moods: ["Aggressive", "Fiery", "Defiant", "Energizing", "Gritty"], bpmLo: 115, bpmHi: 175, queries: ["phonk", "memphis phonk", "phonk house", "dark phonk"], tags: ["phonk", "memphis", "cowbell"]),
        "Drift Phonk": MoodPreset(genres: ["Trap", "Electronic", "Hip-Hop/Rap"], moods: ["Aggressive", "Energizing", "Fiery"], bpmLo: 110, bpmHi: 170, queries: ["drift phonk", "phonk drift", "brazilian phonk", "funk phonk"], tags: ["driftphonk", "drift", "phonk", "brazilianphonk"]),
    ]
}

struct WaveConfig: Codable, Equatable, Sendable {
    var novelty: Double = 50
    var popularity: Double = 50
    var tempo: String = "any"      // any | slow | mid | fast
    var vocals: String = "any"     // any | instrumental | vocal
    var onlyDl: Bool = false
}

let STARTER_GENRES: [String] = ["Electronic", "Hip-Hop/Rap", "Lo-Fi", "House", "Rock", "Pop", "Ambient", "Alternative", "Drum & Bass", "Techno", "Jazz", "R&B/Soul", "Trap", "Classical"]
let GENRE_LIST: [String] = ["Electronic", "Hip-Hop/Rap", "Lo-Fi", "House", "Deep House", "Techno", "Drum & Bass", "Ambient", "Pop", "Rock", "Alternative", "Jazz", "R&B/Soul", "Classical", "Acoustic", "Trap", "Downtempo", "Synthwave"]
let MOOD_LIST: [String] = ["Peaceful", "Easygoing", "Cool", "Melancholy", "Energizing", "Upbeat", "Fiery", "Romantic", "Brooding", "Sophisticated"]

struct ScoreContext {
    var mood: String? = nil
    var cfg: WaveConfig = WaveConfig()
    /// Overrides the noise spread (mixes, AI and charts pass a lower value).
    var explore: Double? = nil
    var plan: AIPlan? = nil
    /// Track the new ones will follow (bpm / key flow).
    var current: Track? = nil
    /// Already queued tracks (for "no artist within last 8" and genre streaks).
    var recent: [Track] = []
    /// Number of slots already filled before this batch (for "every 5th slot is discovery").
    var slotOffset: Int = 0
}

// MARK: - Camelot wheel

enum Camelot {
    private static let notes: [Character: Int] = ["C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11]

    static func majorNumber(_ pc: Int) -> Int {
        (((pc * 7) % 12) + 7) % 12 + 1
    }

    /// Parses "C major", "F# minor", "Bbm", "A min" … into (wheel number 1…12, isMajor).
    static func code(_ key: String) -> (Int, Bool)? {
        let s = key.trimmingCharacters(in: .whitespaces)
        guard let first = s.first, let upper = String(first).uppercased().first, let base = notes[upper] else { return nil }
        var pc = base
        var rest = s.dropFirst()
        if let c = rest.first, c == "#" || c == "♯" {
            pc += 1
            rest = rest.dropFirst()
        } else if let c = rest.first, c == "b" || c == "♭" {
            pc += 11
            rest = rest.dropFirst()
        }
        pc = pc % 12
        let r = rest.trimmingCharacters(in: .whitespaces).lowercased()
        let minor = r.hasPrefix("min") || r == "m" || r.hasPrefix("m ") || r.hasPrefix("moll")
        if minor { return (majorNumber((pc + 3) % 12), false) }
        return (majorNumber(pc), true)
    }

    static func compatible(_ a: String, _ b: String) -> Bool {
        guard let x = code(a), let y = code(b) else { return false }
        if x.0 == y.0 { return true } // same key or relative major/minor
        if x.1 == y.1 {
            let d = abs(x.0 - y.0)
            return d == 1 || d == 11
        }
        return false
    }
}

// MARK: - Recommendation engine v2

/// Taste profile, learning, session vector, scoring and re-ranking («Моя волна 2»).
@MainActor
final class Recs: ObservableObject {
    static let shared = Recs()

    @Published private(set) var P: Profile

    private struct SessionEntry {
        let id: String
        let genre: String
        let mood: String
        let artistId: String
        var value: Double
    }

    private var session: [SessionEntry] = []
    private var sessionPlayed: Set<String> = []
    private var lastActivity = Date()

    private static let day: Double = 86_400_000
    private static let tempo: [String: (Double, Double)] = ["slow": (0, 95), "mid": (95, 125), "fast": (125, 999)]

    private init() {
        P = Recs.loadFromDisk()
        applyDecay(force: true)
    }

    // MARK: persistence

    private static var fileURL: URL {
        let dir = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir.appendingPathComponent("echo.profile.json")
    }

    private static func loadFromDisk() -> Profile {
        guard let data = try? Data(contentsOf: fileURL) else { return Profile() }
        return Profile(json: JSON.parse(data))
    }

    private func persist() {
        if let data = JSONOut.data(P.jsonObject) {
            try? data.write(to: Recs.fileURL, options: .atomic)
        }
    }

    /// Saves locally and schedules a push to /api/profile.
    func save() {
        P.updated = nowMs()
        persist()
        AppModel.shared.pushProfile()
    }

    private func mutate(_ change: (inout Profile) -> Void) {
        var p = P
        change(&p)
        P = p
        save()
    }

    func replace(_ p: Profile) {
        P = p
        applyDecay(force: true)
        persist()
    }

    /// Keeps likes and playlists, forgets the rest.
    func resetProfile() {
        var fresh = Profile()
        fresh.likes = P.likes
        fresh.playlists = P.playlists
        fresh.decayAt = nowMs()
        P = fresh
        resetSession()
        save()
    }

    func clearProfile() {
        P = Profile()
        P.decayAt = nowMs()
        resetSession()
        try? FileManager.default.removeItem(at: Recs.fileURL)
    }

    // MARK: decay

    /// Halves g/m/a/t every 30 days.
    func applyDecay(force: Bool = false) {
        let now = nowMs()
        if P.decayAt <= 0 {
            P.decayAt = now
            return
        }
        let dt = now - P.decayAt
        if dt <= 0 { return }
        if !force && dt < 60_000 { return }
        let f = pow(0.5, dt / (30 * Recs.day))
        var p = P
        p.g = p.g.mapValues { $0 * f }
        p.m = p.m.mapValues { $0 * f }
        p.a = p.a.mapValues { $0 * f }
        p.t = p.t.mapValues { $0 * f }
        p.decayAt = now
        P = p
        persist()
    }

    // MARK: learning

    private static func clamp(_ v: Double) -> Double { max(-25, min(40, v)) }

    private static func bump(_ map: inout [String: Double], _ key: String, _ v: Double) {
        if key.isEmpty || key.hasSuffix(":") { return }
        map[key] = clamp((map[key] ?? 0) + v)
    }

    private static func learn(_ p: inout Profile, _ t: Track, _ w: Double) {
        bump(&p.g, t.genre, w)
        bump(&p.m, t.mood, w * 0.7)
        bump(&p.a, t.artistId, w * 0.8)
        if !t.artistId.isEmpty && !t.artistId.hasSuffix(":") { p.an[t.artistId] = t.artist }
        for tag in t.tags.prefix(4) { bump(&p.t, tag, w * 0.3) }
    }

    func learn(_ t: Track, _ w: Double) {
        mutate { Recs.learn(&$0, t, w) }
    }

    static func slim(_ t: Track) -> Track {
        var s = t
        s.tags = Array(t.tags.prefix(6))
        s.at = nil
        s.likedAt = nil
        s.link = ""
        s.plays = 0
        return s
    }

    func isLiked(_ t: Track?) -> Bool {
        guard let t = t else { return false }
        return P.likes[t.id] != nil
    }

    /// like +3 / unlike −2. Returns the new state.
    @discardableResult
    func toggleLike(_ t: Track) -> Bool {
        if P.likes[t.id] != nil {
            mutate { p in
                p.likes[t.id] = nil
                Recs.learn(&p, t, -2)
            }
            return false
        }
        mutate { p in
            var s = Recs.slim(t)
            s.likedAt = nowMs()
            p.likes[t.id] = s
            Recs.learn(&p, t, 3)
        }
        sessionMark(t, 1)
        return true
    }

    /// «Не то»: −5, ban the track, and the artist after the second time.
    func ban(_ t: Track) {
        mutate { p in
            p.ban[t.id] = 1
            Recs.learn(&p, t, -5)
            if !t.artistId.isEmpty && !t.artistId.hasSuffix(":") {
                let n = (p.banC[t.artistId] ?? 0) + 1
                p.banC[t.artistId] = n
                if n >= 2 { p.banA[t.artistId] = 1 }
            }
        }
        sessionMark(t, -1.5)
    }

    /// Skip: <10 s −1.5, 10–30 s −0.8.
    func skipped(_ t: Track, at seconds: Double) {
        let w = seconds < 10 ? -1.5 : -0.8
        mutate { p in
            p.skip[t.id] = nowMs()
            if p.skip.count > 500 {
                let keep = p.skip.sorted { $0.value > $1.value }.prefix(400)
                p.skip = Dictionary(uniqueKeysWithValues: keep.map { ($0.key, $0.value) })
            }
            Recs.learn(&p, t, w)
        }
        sessionMark(t, -1.5)
    }

    /// Finished (≥90 %): +1.5.
    func finished(_ t: Track) {
        learn(t, 1.5)
        sessionMark(t, 1)
    }

    /// Listened for more than 30 s: +0.5 and goes to history.
    func listened(_ t: Track) {
        mutate { p in
            var s = Recs.slim(t)
            s.at = nowMs()
            p.hist = Array(([s] + p.hist.filter { $0.id != t.id }).prefix(100))
            Recs.learn(&p, t, 0.5)
        }
        sessionMark(t, 1)
    }

    func seedGenre(_ g: String, on: Bool) {
        mutate { Recs.bump(&$0.g, g, on ? 4 : -4) }
    }

    func addFavArtist(id: String, name: String, genre: String) {
        mutate { p in
            p.fav[id] = name
            p.an[id] = name
            Recs.bump(&p.a, id, 8)
            if !genre.isEmpty { Recs.bump(&p.g, genre, 2) }
        }
    }

    func removeFavArtist(_ id: String) {
        mutate { p in
            p.fav[id] = nil
            Recs.bump(&p.a, id, -8)
        }
    }

    func savePlaylist(title: String, desc: String, tracks: [Track]) {
        mutate { p in
            let id = String(Int64(nowMs()), radix: 36)
            let pl = Playlist(id: id, title: title, desc: desc, tracks: tracks.prefix(50).map { Recs.slim($0) }, at: nowMs())
            p.playlists = Array(([pl] + p.playlists).prefix(40))
        }
    }

    func deletePlaylist(_ id: String) {
        mutate { p in p.playlists.removeAll { $0.id == id } }
    }

    /// Combines the server profile with the local one: nothing liked or saved is lost (web mergeProfile).
    func merge(server s: Profile) {
        let local = P
        let serverNewer = s.updated > local.updated
        var base = serverNewer ? s : local
        let other = serverNewer ? local : s
        base.likes = other.likes.merging(base.likes) { _, b in b }
        base.fav = other.fav.merging(base.fav) { _, b in b }
        base.an = other.an.merging(base.an) { _, b in b }
        base.ban = other.ban.merging(base.ban) { _, b in b }
        base.banA = other.banA.merging(base.banA) { _, b in b }
        let ids = Set(base.playlists.map { $0.id })
        base.playlists = Array((base.playlists + other.playlists.filter { !ids.contains($0.id) }).prefix(40))
        var seen = Set<String>()
        let hist = (base.hist + other.hist).sorted { ($0.at ?? 0) > ($1.at ?? 0) }
        base.hist = Array(hist.filter { seen.insert($0.id).inserted }.prefix(120))
        for (key, v) in other.g where base.g[key] == nil { base.g[key] = v }
        for (key, v) in other.m where base.m[key] == nil { base.m[key] = v }
        for (key, v) in other.a where base.a[key] == nil { base.a[key] = v }
        for (key, v) in other.t where base.t[key] == nil { base.t[key] = v }
        if base.decayAt <= 0 { base.decayAt = other.decayAt }
        P = base
        applyDecay(force: true)
        save()
    }

    // MARK: session vector

    private func checkIdle() {
        if Date().timeIntervalSince(lastActivity) > 30 * 60 { resetSession() }
        lastActivity = Date()
    }

    func resetSession() {
        session = []
        sessionPlayed = []
        lastActivity = Date()
    }

    /// +1 listened / finished / liked, −1.5 skipped — one value per track, last 8 tracks.
    private func sessionMark(_ t: Track, _ value: Double) {
        checkIdle()
        session.removeAll { $0.id == t.id }
        session.append(SessionEntry(id: t.id, genre: t.genre, mood: t.mood, artistId: t.artistId, value: value))
        if session.count > 8 { session.removeFirst(session.count - 8) }
    }

    func markPlayed(_ t: Track) {
        checkIdle()
        sessionPlayed.insert(t.id)
    }

    func sessionScore(_ t: Track) -> Double {
        var s = 0.0
        for e in session {
            if !e.genre.isEmpty && e.genre == t.genre { s += e.value }
            if !e.mood.isEmpty && e.mood == t.mood { s += e.value }
            if !e.artistId.isEmpty && e.artistId == t.artistId { s += e.value }
        }
        return s * 1.5
    }

    // MARK: tops

    private func top(_ map: [String: Double], _ n: Int, min: Double) -> [String] {
        map.filter { $0.value > min }.sorted { $0.value > $1.value }.prefix(n).map { $0.key }
    }

    func topGenres(_ n: Int = 3) -> [String] { top(P.g, n, min: 0.5) }
    func topArtists(_ n: Int = 3) -> [String] { top(P.a, n, min: 1.5) }
    func topTags(_ n: Int = 3) -> [String] { top(P.t, n, min: 0.8) }
    func topMoods(_ n: Int = 3) -> [String] { top(P.m, n, min: 0.5) }

    var hasTaste: Bool { !topGenres(1).isEmpty || !P.likes.isEmpty || !P.fav.isEmpty }

    func tasteBars(_ map: [String: Double], _ n: Int = 6) -> [(String, Double)] {
        map.filter { $0.value > 0 }.sorted { $0.value > $1.value }.prefix(n).map { ($0.key, $0.value) }
    }

    func isDiscovery(_ t: Track) -> Bool { abs(P.g[t.genre] ?? 0) < 1 }

    // MARK: score

    func score(_ t: Track, _ ctx: ScoreContext, now: Double, histAt: [String: Double]) -> Double {
        if P.ban[t.id] != nil { return -1e9 }
        if !t.artistId.isEmpty && P.banA[t.artistId] != nil { return -1e9 }
        let cfg = ctx.cfg
        if cfg.onlyDl && !t.downloadable { return -1e9 }
        let nov = cfg.novelty
        let fam = 1.5 - nov / 100

        var taste = (P.g[t.genre] ?? 0) + 0.8 * (P.m[t.mood] ?? 0) + 0.9 * (P.a[t.artistId] ?? 0)
        for tag in t.tags { taste += 0.3 * (P.t[tag] ?? 0) }
        var s = fam * taste

        s += sessionScore(t)
        s += log10(max(0, t.plays) + 10) * (0.5 + (cfg.popularity - 50) / 30)

        // flow from the track it follows
        if let cur = ctx.current, cur.id != t.id {
            if let b1 = cur.bpm, let b2 = t.bpm, b1 > 0, b2 > 0 { s -= min(abs(b1 - b2), 40) / 10 }
            if Camelot.compatible(cur.key, t.key) { s += 1.2 }
        }

        // fresh releases
        if let rel = t.releaseTime {
            let age = now - rel.timeIntervalSince1970 * 1000
            if age >= -Recs.day && age < 30 * Recs.day { s += 1.5 * nov / 100 }
        }

        if P.likes[t.id] != nil { s += nov < 40 ? 2.5 : (nov > 70 ? -4 : 0) }

        let text = (t.title + " " + t.tags.joined(separator: " ")).lowercased()
        if let range = Recs.tempo[cfg.tempo], let b = t.bpm {
            s += (b >= range.0 && b < range.1) ? 3 : -4
        }
        let instr = t.instrumental || ["instrumental", "lofi", "lo-fi", "ambient", "beat"].contains { text.contains($0) }
        if cfg.vocals == "instrumental" { s += instr ? 3 : -2.5 }
        if cfg.vocals == "vocal" { s += instr ? -3 : 1 }

        if let mood = ctx.mood, let pre = MoodPreset.all[mood] {
            if pre.genres.contains(t.genre) { s += pre.tags != nil ? 1.5 : 4 }
            if pre.moods.contains(t.mood) { s += 3 }
            if let tags = pre.tags {
                if tags.contains(where: { text.contains($0) }) { s += 9 } else { s -= 4 }
            }
            if let b = t.bpm { s += (b >= pre.bpmLo && b <= pre.bpmHi) ? 2.5 : -2 }
        }

        if let pl = ctx.plan {
            if pl.genres.contains(t.genre) { s += 4 }
            if pl.moods.contains(t.mood) { s += 3 }
            if let lo = pl.bpmMin, let b = t.bpm { s += (b >= lo && b <= (pl.bpmMax ?? 999)) ? 2.5 : -2.5 }
            if pl.instrumental && t.instrumental { s += 2 }
        }

        if let at = histAt[t.id], now - at < 3 * Recs.day { s -= 6 }
        if sessionPlayed.contains(t.id) { s -= 20 }
        if P.skip[t.id] != nil { s -= 3 }

        let spread = ctx.explore ?? (0.6 + nov / 100 * 4.5)
        s += Double.random(in: -0.5...0.5) * spread
        return s
    }

    // MARK: re-rank

    /// Greedy re-rank: no artist within the last 8 queued, ≤ perArtist per batch, genre streak of 3 → −2,
    /// every 5th slot is a discovery track (|g| < 1) when one exists.
    func pick(_ cands: [Track], _ n: Int, _ ctx: ScoreContext, perArtist: Int = 2) -> [Track] {
        applyDecay()
        let now = nowMs()
        var histAt: [String: Double] = [:]
        for h in P.hist { if let at = h.at, histAt[h.id] == nil { histAt[h.id] = at } }

        var ranked: [(Track, Double)] = []
        ranked.reserveCapacity(cands.count)
        for t in cands {
            let s = score(t, ctx, now: now, histAt: histAt)
            if s > -1e8 { ranked.append((t, s)) }
        }
        ranked.sort { $0.1 > $1.1 }

        var out: [Track] = []
        var count: [String: Int] = [:]
        let recentArtists = ctx.recent.suffix(8).map { $0.artistId }
        let recentGenres = ctx.recent.suffix(3).map { $0.genre }

        while out.count < n && !ranked.isEmpty {
            let slot = ctx.slotOffset + out.count + 1
            let lastArtists = Set((recentArtists + out.map { $0.artistId }).suffix(8))
            let previousArtist = out.last?.artistId ?? ctx.recent.last?.artistId
            let lastGenres = Array((recentGenres + out.map { $0.genre }).suffix(3))
            var streak: String? = nil
            if lastGenres.count == 3, let g = lastGenres.first, !g.isEmpty, lastGenres.allSatisfy({ $0 == g }) { streak = g }

            func strictOK(_ t: Track) -> Bool {
                !lastArtists.contains(t.artistId) && (count[t.artistId] ?? 0) < perArtist
            }
            func looseOK(_ t: Track) -> Bool {
                t.artistId != previousArtist && (count[t.artistId] ?? 0) < perArtist
            }

            let wantDiscovery = slot % 5 == 0 && ranked.contains { isDiscovery($0.0) && strictOK($0.0) }

            var best: Int? = nil
            var bestScore = -Double.infinity
            for (i, item) in ranked.enumerated() {
                let t = item.0
                if !strictOK(t) { continue }
                if wantDiscovery && !isDiscovery(t) { continue }
                var s = item.1
                if let sg = streak, t.genre == sg { s -= 2 }
                if s > bestScore {
                    bestScore = s
                    best = i
                }
            }
            if best == nil {
                // small pools: relax "last 8" to "not the same artist twice in a row"
                for (i, item) in ranked.enumerated() where looseOK(item.0) {
                    var s = item.1
                    if let sg = streak, item.0.genre == sg { s -= 2 }
                    if s > bestScore {
                        bestScore = s
                        best = i
                    }
                }
            }
            guard let idx = best else { break }
            let t = ranked.remove(at: idx).0
            out.append(t)
            count[t.artistId, default: 0] += 1
        }
        return out
    }
}
