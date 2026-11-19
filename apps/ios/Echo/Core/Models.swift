import Foundation

/// One track from any source, normalized like `fromAudius` / `fromJamendo` / `fromLocal` in the web api.js.
/// URLs are kept as server-relative paths ("/api/au/stream/…") so the profile stays compatible with the web.
struct Track: Identifiable, Hashable, Sendable {
    var id: String = ""
    var src: String = ""
    var rawId: String = ""
    var title: String = ""
    var artist: String = ""
    var artistId: String = ""
    var cover: String = ""
    var coverSm: String = ""
    var duration: Double = 0
    var genre: String = ""
    var mood: String = ""
    var tags: [String] = []
    var bpm: Double? = nil
    var key: String = ""
    var releaseDate: String = ""
    var plays: Double = 0
    var downloadable: Bool = false
    var quality: String = ""
    var stream: String = ""
    var dlOrig: String = ""
    var dlMp3: String = ""
    var ext: String = ""
    var instrumental: Bool = false
    var link: String = ""
    var size: Double? = nil
    var at: Double? = nil
    var likedAt: Double? = nil

    init() {}

    static func == (a: Track, b: Track) -> Bool { a.id == b.id }

    func hash(into hasher: inout Hasher) { hasher.combine(id) }

    init(json j: JSON) {
        id = j["id"].str
        src = j["src"].str
        rawId = j["rawId"].str
        title = j["title"].str
        artist = j["artist"].str
        artistId = j["artistId"].str
        cover = j["cover"].str
        coverSm = j["coverSm"].str
        duration = j["duration"].num
        genre = j["genre"].str
        mood = j["mood"].str
        if j["tags"].raw is [Any] {
            tags = j["tags"].strings
        } else {
            tags = j["tags"].str.split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces).lowercased() }.filter { !$0.isEmpty }
        }
        if let b = j["bpm"].double, b > 0 { bpm = b }
        key = j["key"].str
        releaseDate = j["releaseDate"].str
        plays = j["plays"].num
        downloadable = j["downloadable"].bool
        quality = j["quality"].str
        stream = j["stream"].str
        dlOrig = j["dlOrig"].str
        dlMp3 = j["dlMp3"].str
        ext = j["ext"].str
        instrumental = j["instrumental"].bool
        link = j["link"].str
        size = j["size"].double
        at = j["at"].double
        likedAt = j["likedAt"].double
    }

    /// Same shape as web `slim()` (+ at / likedAt). Empty values are left out.
    var jsonObject: [String: Any] {
        var o: [String: Any] = ["id": id]
        func put(_ k: String, _ v: String) { if !v.isEmpty { o[k] = v } }
        put("src", src)
        put("rawId", rawId)
        put("title", title)
        put("artist", artist)
        put("artistId", artistId)
        put("cover", cover)
        put("coverSm", coverSm)
        put("genre", genre)
        put("mood", mood)
        put("key", key)
        put("releaseDate", releaseDate)
        put("quality", quality)
        put("stream", stream)
        put("dlOrig", dlOrig)
        put("dlMp3", dlMp3)
        put("ext", ext)
        put("link", link)
        if duration > 0 && duration.isFinite { o["duration"] = duration }
        if !tags.isEmpty { o["tags"] = Array(tags.prefix(6)) }
        if let b = bpm, b.isFinite { o["bpm"] = b }
        if plays > 0 && plays.isFinite { o["plays"] = plays }
        o["downloadable"] = downloadable
        if instrumental { o["instrumental"] = true }
        if let s = size, s.isFinite { o["size"] = s }
        if let a = at, a.isFinite { o["at"] = a }
        if let l = likedAt, l.isFinite { o["likedAt"] = l }
        return o
    }

    var durationText: String { fmtTime(duration) }

    private static let isoFull: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return f
    }()

    private static let dayFormatter: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(identifier: "UTC")
        f.dateFormat = "yyyy-MM-dd"
        return f
    }()

    /// Release date parsed from Audius `release_date` (ISO or "yyyy-MM-dd…").
    var releaseTime: Date? {
        let s = releaseDate.trimmingCharacters(in: .whitespaces)
        if s.isEmpty { return nil }
        if let d = Track.isoFull.date(from: s) { return d }
        if s.count >= 10 { return Track.dayFormatter.date(from: String(s.prefix(10))) }
        return nil
    }
}

func fmtTime(_ s: Double) -> String {
    guard s > 0, s.isFinite else { return "—" }
    let total = Int(s)
    let m = total / 60
    let sec = total % 60
    return sec < 10 ? "\(m):0\(sec)" : "\(m):\(sec)"
}

struct Playlist: Identifiable, Sendable {
    var id: String = ""
    var title: String = ""
    var desc: String = ""
    var tracks: [Track] = []
    var at: Double = 0

    init(id: String, title: String, desc: String, tracks: [Track], at: Double) {
        self.id = id
        self.title = title
        self.desc = desc
        self.tracks = tracks
        self.at = at
    }

    init(json j: JSON) {
        id = j["id"].str
        title = j["title"].str
        desc = j["desc"].str
        tracks = j["tracks"].array.map { Track(json: $0) }.filter { !$0.id.isEmpty }
        at = j["at"].num
    }

    var jsonObject: [String: Any] {
        ["id": id, "title": title, "desc": desc, "tracks": tracks.map { $0.jsonObject }, "at": at]
    }
}

struct Me: Equatable {
    let email: String
    let role: String
    var isAdmin: Bool { role == "admin" }
    var roleText: String { isAdmin ? "администратор" : "слушатель" }
}

struct AIPlan: Sendable {
    var title: String = ""
    var description: String = ""
    var queries: [String] = []
    var genres: [String] = []
    var moods: [String] = []
    var bpmMin: Double? = nil
    var bpmMax: Double? = nil
    var instrumental: Bool = false
    var size: Int = 25
    var source: String = ""

    init() {}

    init(json j: JSON) {
        title = j["title"].str
        description = j["description"].str
        queries = j["queries"].strings
        genres = j["genres"].strings
        moods = j["moods"].strings
        if let b = j["bpm_min"].double, b > 0 { bpmMin = b }
        if let b = j["bpm_max"].double, b > 0 { bpmMax = b }
        instrumental = j["instrumental"].bool
        size = j["size"].int ?? 25
        if size <= 0 { size = 25 }
        source = j["source"].str
    }
}

struct ChartInfo: Identifiable, Hashable, Sendable {
    let id: String
    let name: String
}

struct ChartItem: Identifiable, Sendable {
    var pos: Int
    var change: String
    var artist: String
    var with: [String]
    var title: String
    var spotify: String
    var streams: Double

    var id: Int { pos }

    init(json j: JSON) {
        pos = j["pos"].int ?? 0
        change = j["change"].str
        artist = j["artist"].str
        with = j["with"].strings
        title = j["title"].str
        spotify = j["spotify"].str
        streams = j["streams"].num
    }

    var artists: String { ([artist] + with).filter { !$0.isEmpty }.joined(separator: ", ") }

    var streamsText: String {
        guard streams > 0 else { return "" }
        let f = NumberFormatter()
        f.locale = Locale(identifier: "ru_RU")
        f.numberStyle = .decimal
        return f.string(from: NSNumber(value: streams)) ?? ""
    }
}

struct ChartData: Sendable {
    var country: String
    var name: String
    var date: String
    var items: [ChartItem]
}

func nowMs() -> Double { Date().timeIntervalSince1970 * 1000 }
