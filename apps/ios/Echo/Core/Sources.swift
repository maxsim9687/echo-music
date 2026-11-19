import Foundation

typealias TrackJob = @Sendable () async throws -> [Track]

/// Music sources: Audius (through the site's /api/au proxy), Jamendo (/api/ja) and admin uploads.
/// Port of public/api.js.
enum Sources {
    static let AU = "/api/au"
    /// Set from /api/me `jamendo`.
    static var jamendoOn = false

    static let exclude: Set<String> = ["Podcasts", "Spoken Word", "Comedy", "Audiobooks", "Kids", "Devotional"]

    static func g0(_ g: String) -> String {
        guard let first = g.split(separator: ",").first else { return "" }
        return String(first).trimmingCharacters(in: .whitespaces)
    }

    // MARK: Audius

    static func fromAudius(_ j: JSON) -> Track {
        var t = Track()
        let id = j["id"].str
        t.id = "au:" + id
        t.src = "audius"
        t.rawId = id
        t.title = j["title"].str
        let name = j["user"]["name"].str
        t.artist = name.isEmpty ? "Неизвестный артист" : name
        t.artistId = "au:" + j["user"]["id"].str
        let hasArt = j["artwork"].isObject || j["artwork"].bool
        t.cover = hasArt ? "\(AU)/art/\(id)/480" : ""
        t.coverSm = hasArt ? "\(AU)/art/\(id)/150" : ""
        t.duration = j["duration"].num
        t.genre = g0(j["genre"].str)
        t.mood = j["mood"].str
        t.tags = Array(j["tags"].str.split(separator: ",")
            .map { $0.trimmingCharacters(in: .whitespaces).lowercased() }
            .filter { !$0.isEmpty }
            .prefix(8))
        if let b = j["bpm"].double, b > 0 { t.bpm = b }
        t.key = j["musical_key"].str
        t.releaseDate = j["release_date"].str
        t.plays = j["play_count"].num
        t.downloadable = j["is_downloadable"].bool
        t.quality = "MP3 320"
        t.stream = "\(AU)/stream/\(id)"
        let permalink = j["permalink"].str
        t.link = permalink.isEmpty ? "" : "https://audius.co" + permalink
        return t
    }

    static func okAudius(_ j: JSON) -> Bool {
        guard j.isObject else { return false }
        if j["is_streamable"].exists && !j["is_streamable"].bool { return false }
        if j["is_stream_gated"].bool { return false }
        let d = j["duration"].num
        if d < 60 || d > 1500 { return false }
        return !exclude.contains(g0(j["genre"].str))
    }

    static func auList(_ d: JSON) -> [Track] {
        d["data"].array.filter { okAudius($0) }.map { fromAudius($0) }
    }

    static func auTrending(genre: String? = nil, time: String = "week", limit: Int = 40) async throws -> [Track] {
        let p = API.path("\(AU)/tracks/trending", [("time", time), ("limit", String(limit)), ("genre", genre)])
        return auList(try await API.shared.get(p, ttl: 600))
    }

    static func auUnderground(limit: Int = 30) async throws -> [Track] {
        let p = API.path("\(AU)/tracks/trending/underground", [("limit", String(limit))])
        return auList(try await API.shared.get(p, ttl: 600))
    }

    static func auSearch(_ query: String, limit: Int = 30, onlyDl: Bool = false) async throws -> [Track] {
        let p = API.path("\(AU)/tracks/search", [("query", query), ("limit", String(limit)), ("only_downloadable", onlyDl ? "true" : nil)])
        return auList(try await API.shared.get(p, ttl: 300))
    }

    static func auArtist(_ artistId: String, limit: Int = 20) async throws -> [Track] {
        let id = artistId.hasPrefix("au:") ? String(artistId.dropFirst(3)) : artistId
        if id.isEmpty { return [] }
        let p = API.path("\(AU)/users/\(API.enc(id))/tracks", [("limit", String(limit))])
        return auList(try await API.shared.get(p, ttl: 600))
    }

    // MARK: Jamendo

    static func jTag(_ genre: String) -> String {
        let low = genre.lowercased().replacingOccurrences(of: "&", with: "and")
        return String(low.unicodeScalars.filter { ($0 >= "a" && $0 <= "z") || ($0 >= "0" && $0 <= "9") }.map { Character($0) })
    }

    static func fromJamendo(_ j: JSON) -> Track {
        var t = Track()
        let id = j["id"].str
        let genres = j["musicinfo"]["tags"]["genres"].strings
        let vartags = j["musicinfo"]["tags"]["vartags"].strings
        t.id = "ja:" + id
        t.src = "jamendo"
        t.rawId = id
        t.title = j["name"].str
        t.artist = j["artist_name"].str
        t.artistId = "ja:" + j["artist_id"].str
        t.cover = "/api/ja/art/\(id)"
        t.coverSm = t.cover
        t.duration = j["duration"].num
        if let g = genres.first, let f = g.first {
            t.genre = String(f).uppercased() + String(g.dropFirst())
        }
        t.tags = Array((genres + vartags).map { $0.lowercased() }.prefix(8))
        t.instrumental = j["musicinfo"]["vocalinstrumental"].str == "instrumental"
        t.downloadable = j["audiodownload_allowed"].bool && !j["audiodownload"].str.isEmpty
        t.quality = "FLAC"
        t.stream = "/api/ja/stream/\(id)"
        t.dlOrig = "/api/ja/dl/\(id)"
        t.dlMp3 = "/api/ja/stream/\(id)"
        t.link = j["shareurl"].str
        return t
    }

    static func jamendo(_ params: [(String, String?)]) async throws -> [Track] {
        if !jamendoOn { return [] }
        var items = params
        if !items.contains(where: { $0.0 == "limit" }) { items.insert(("limit", "30"), at: 0) }
        let d = try await API.shared.get(API.path("/api/ja/tracks", items), ttl: 600)
        return d["results"].array
            .filter { !$0["audio"].str.isEmpty && $0["duration"].num >= 60 }
            .map { fromJamendo($0) }
    }

    static func jaSearch(_ query: String, limit: Int = 30) async throws -> [Track] {
        try await jamendo([("search", query), ("limit", String(limit)), ("order", "relevance")])
    }

    static func jaPopular(tags: String? = nil, limit: Int = 30, instrumental: Bool = false) async throws -> [Track] {
        var p: [(String, String?)] = [("limit", String(limit)), ("order", "popularity_month")]
        if let tags = tags, !tags.isEmpty { p.append(("fuzzytags", tags)) }
        if instrumental { p.append(("vocalinstrumental", "instrumental")) }
        return try await jamendo(p)
    }

    // MARK: uploads

    static func fromLocal(_ u: JSON) -> Track {
        var t = Track()
        let id = u["id"].str
        let ext = u["ext"].str.lowercased()
        t.id = "lo:" + id
        t.src = "local"
        t.rawId = id
        t.title = u["title"].str
        t.artist = u["artist"].str
        t.artistId = "lo:" + t.artist.lowercased()
        let hasCover = u["cover"].bool || !u["cover"].str.isEmpty && u["cover"].str != "0"
        t.cover = hasCover ? "/media/\(id)/cover" : ""
        t.coverSm = t.cover
        t.duration = u["duration"].num
        t.genre = u["genre"].str
        t.mood = u["mood"].str
        t.tags = t.genre.isEmpty ? [] : [t.genre.lowercased()]
        if let b = u["bpm"].double, b > 0 { t.bpm = b }
        t.downloadable = true
        t.quality = ext.uppercased()
        t.ext = ext
        t.stream = "/media/\(id)"
        t.dlOrig = "/media/\(id)?download=1"
        t.size = u["size"].double
        return t
    }

    static func localTracks() async -> [Track] {
        do {
            return try await API.shared.get("/api/uploads")["data"].array.map { fromLocal($0) }
        } catch {
            return []
        }
    }

    static func uploads(query: String) async throws -> [Track] {
        let d = try await API.shared.get(API.path("/api/uploads", [("q", query)]))
        let list = d["data"].array.map { fromLocal($0) }
        let q = query.lowercased()
        return list.filter { "\($0.title) \($0.artist) \($0.genre)".lowercased().contains(q) }
    }

    // MARK: combined

    /// Runs jobs in parallel, keeps their order, and reports which failed.
    static func runAll(_ jobs: [TrackJob]) async -> [Result<[Track], Error>] {
        await withTaskGroup(of: (Int, Result<[Track], Error>).self) { group in
            for (i, job) in jobs.enumerated() {
                group.addTask {
                    do {
                        let list = try await job()
                        return (i, .success(list))
                    } catch {
                        return (i, .failure(error))
                    }
                }
            }
            var out: [Result<[Track], Error>?] = Array(repeating: nil, count: jobs.count)
            for await pair in group {
                out[pair.0] = pair.1
            }
            return out.compactMap { $0 }
        }
    }

    static func searchAll(_ query: String, src: String = "all", onlyDl: Bool = false) async -> (list: [Track], failed: Bool) {
        var jobs: [TrackJob] = []
        if src == "all" || src == "audius" {
            jobs.append({ try await Sources.auSearch(query, limit: 40, onlyDl: onlyDl) })
        }
        if (src == "all" || src == "jamendo") && Sources.jamendoOn {
            jobs.append({ try await Sources.jaSearch(query, limit: 30) })
        }
        if src == "all" || src == "local" {
            jobs.append({ try await Sources.uploads(query: query) })
        }
        let results = await runAll(jobs)
        var failed = 0
        var list: [Track] = []
        for r in results {
            switch r {
            case .success(let l): list += l
            case .failure: failed += 1
            }
        }
        if onlyDl { list = list.filter { $0.downloadable } }
        return (dedupe(list), failed == results.count && !results.isEmpty)
    }

    static func dedupe(_ list: [Track]) -> [Track] {
        var seen = Set<String>()
        var out: [Track] = []
        out.reserveCapacity(list.count)
        for t in list where !seen.contains(t.id) {
            seen.insert(t.id)
            out.append(t)
        }
        return out
    }

    /// Saved likes/history may hold URLs from older versions: rebuild them for the current routes.
    static func refresh(_ t: Track) -> Track {
        var t = t
        if t.src == "audius" && !t.rawId.isEmpty {
            t.stream = "\(AU)/stream/\(t.rawId)"
            if !t.cover.isEmpty && !t.cover.hasPrefix("/") {
                t.cover = "\(AU)/art/\(t.rawId)/480"
                t.coverSm = "\(AU)/art/\(t.rawId)/150"
            }
        }
        if t.src == "jamendo" && !t.rawId.isEmpty {
            t.stream = "/api/ja/stream/\(t.rawId)"
            t.dlMp3 = t.stream
            t.dlOrig = "/api/ja/dl/\(t.rawId)"
            t.cover = "/api/ja/art/\(t.rawId)"
            t.coverSm = t.cover
        }
        return t
    }
}
