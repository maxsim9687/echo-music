import Foundation

/// Taste profile `P`. Same JSON keys as the web recs.js so /api/profile is shared between web and app.
struct Profile {
    var g: [String: Double] = [:]      // genres
    var m: [String: Double] = [:]      // moods
    var a: [String: Double] = [:]      // artists
    var t: [String: Double] = [:]      // tags
    var k: [String: Double] = [:]      // musical keys
    var an: [String: String] = [:]     // artist names
    var fav: [String: String] = [:]    // favourite artists
    var hist: [Track] = []             // ≤100 slim tracks with `at`
    var skip: [String: Double] = [:]
    var ban: [String: Double] = [:]
    var banA: [String: Double] = [:]   // banned artists
    var banC: [String: Double] = [:]   // «Не то» count per artist
    var likes: [String: Track] = [:]
    var playlists: [Playlist] = []
    var decayAt: Double = 0
    var updated: Double = 0
    /// Keys this app does not know (written by a newer web version) are kept as they are.
    var extra: [String: JSON] = [:]

    init() {}

    private static let known: Set<String> = ["g", "m", "a", "t", "k", "an", "fav", "hist", "skip", "ban", "banA", "banC", "likes", "playlists", "decayAt", "updated"]

    static func numMap(_ j: JSON) -> [String: Double] {
        var out: [String: Double] = [:]
        for (key, v) in j.object {
            if let d = v.double, d.isFinite { out[key] = d }
        }
        return out
    }

    static func strMap(_ j: JSON) -> [String: String] {
        var out: [String: String] = [:]
        for (key, v) in j.object {
            if let s = v.string { out[key] = s }
        }
        return out
    }

    init(json j: JSON) {
        g = Profile.numMap(j["g"])
        m = Profile.numMap(j["m"])
        a = Profile.numMap(j["a"])
        t = Profile.numMap(j["t"])
        k = Profile.numMap(j["k"])
        an = Profile.strMap(j["an"])
        fav = Profile.strMap(j["fav"])
        hist = j["hist"].array.map { Track(json: $0) }.filter { !$0.id.isEmpty }
        skip = Profile.numMap(j["skip"])
        ban = Profile.numMap(j["ban"])
        banA = Profile.numMap(j["banA"])
        banC = Profile.numMap(j["banC"])
        var lk: [String: Track] = [:]
        for (key, v) in j["likes"].object {
            var tr = Track(json: v)
            if tr.id.isEmpty { tr.id = key }
            lk[key] = tr
        }
        likes = lk
        playlists = j["playlists"].array.map { Playlist(json: $0) }.filter { !$0.id.isEmpty }
        decayAt = j["decayAt"].num
        updated = j["updated"].num
        var ex: [String: JSON] = [:]
        for (key, v) in j.object where !Profile.known.contains(key) { ex[key] = v }
        extra = ex
    }

    var jsonObject: [String: Any] {
        var o: [String: Any] = [:]
        for (key, v) in extra {
            if let r = v.raw { o[key] = r }
        }
        o["g"] = Profile.finite(g)
        o["m"] = Profile.finite(m)
        o["a"] = Profile.finite(a)
        o["t"] = Profile.finite(t)
        o["k"] = Profile.finite(k)
        o["an"] = an
        o["fav"] = fav
        o["hist"] = hist.map { $0.jsonObject }
        o["skip"] = Profile.finite(skip)
        o["ban"] = Profile.finite(ban)
        o["banA"] = Profile.finite(banA)
        o["banC"] = Profile.finite(banC)
        var lk: [String: Any] = [:]
        for (key, v) in likes { lk[key] = v.jsonObject }
        o["likes"] = lk
        o["playlists"] = playlists.map { $0.jsonObject }
        o["decayAt"] = decayAt
        o["updated"] = updated
        return o
    }

    private static func finite(_ d: [String: Double]) -> [String: Double] {
        d.filter { $0.value.isFinite }
    }
}
