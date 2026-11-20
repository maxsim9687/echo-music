package space.maxsim.echo.data

import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject

/** Where songs come from. Mirrors public/api.js on the site. */
object Sources {
    private const val AU = "/api/au"
    private val EXCLUDE = setOf("Podcasts", "Spoken Word", "Comedy", "Audiobooks", "Kids", "Devotional")
    private val CATALOG_JUNK = Regex(
        "cantajuego|cocomelon|pinkfong|baby shark|nursery|kids song|for kids|детск|для детей|full video|full song|video song|lyrical|jukebox|#|\\bstatus\\b|songs? 20\\d\\d|new .*songs?\\b|in cinemas|official song|official videos|\\bfilm\\b|movie|haryanvi|bhojpuri|punjabi|bollywood|telugu|tamil|nonstop|\\bmix\\b|\\b(?:top|best) \\d+",
        RegexOption.IGNORE_CASE,
    )
    private val FOREIGN_SCRIPT = Regex("[\\u0600-\\u06FF\\u0900-\\u0DFF\\u0E00-\\u0E7F\\u1000-\\u109F]")

    // ---------- normalisation ----------
    private fun g0(g: String) = g.split(",").first().trim()

    fun fromAudius(o: JsonObject): Track {
        val id = o.str("id")
        val user = o["user"].obj
        val hasArt = o["artwork"] != null && o["artwork"].toString() != "null"
        return Track(
            id = "au:$id", src = "audius", rawId = id, title = o.str("title"), artist = user.str("name").ifEmpty { "Неизвестный артист" },
            artistId = "au:" + user.str("id"), cover = if (hasArt) "$AU/art/$id/480" else "", coverSm = if (hasArt) "$AU/art/$id/150" else "",
            duration = o.num("duration"), genre = g0(o.str("genre")), mood = o.str("mood"),
            tags = o.str("tags").split(",").map { it.trim().lowercase() }.filter { it.isNotEmpty() }.take(8),
            plays = o.num("play_count"), downloadable = o.bool("is_downloadable"), quality = "MP3 320", stream = "$AU/stream/$id",
            link = o.str("permalink").let { if (it.isEmpty()) "" else "https://audius.co$it" }, releaseDate = o.str("release_date"),
        )
    }

    private fun okAudius(o: JsonObject): Boolean {
        val d = o.num("duration")
        return (o["is_streamable"]?.toString() != "false") && !o.bool("is_stream_gated") && d in 60.0..1500.0 && g0(o.str("genre")) !in EXCLUDE
    }

    private fun auList(d: JsonElement) = d.obj["data"].arr.map { it.obj }.filter { okAudius(it) }.map { fromAudius(it) }

    fun fromJamendo(o: JsonObject): Track {
        val id = o.str("id")
        val tagsObj = o["musicinfo"].obj["tags"].obj
        val genres = tagsObj["genres"].arr.map { it.asStr() }
        return Track(
            id = "ja:$id", src = "jamendo", rawId = id, title = o.str("name"), artist = o.str("artist_name"), artistId = "ja:" + o.str("artist_id"),
            cover = "/api/ja/art/$id", coverSm = "/api/ja/art/$id", duration = o.num("duration"),
            genre = genres.firstOrNull()?.replaceFirstChar { it.uppercase() } ?: "", tags = (genres + tagsObj["vartags"].arr.map { it.asStr() }).take(8),
            downloadable = o.bool("audiodownload_allowed"), quality = "FLAC", stream = "/api/ja/stream/$id", link = o.str("shareurl"),
        )
    }

    fun fromLocal(o: JsonObject): Track {
        val id = o.str("id")
        val hasCover = o.str("cover").isNotEmpty()
        return Track(
            id = "lo:$id", src = "local", rawId = id, title = o.str("title"), artist = o.str("artist"), artistId = "lo:" + o.str("artist").lowercase(),
            cover = if (hasCover) "/media/$id/cover" else "", coverSm = if (hasCover) "/media/$id/cover" else "", duration = o.num("duration"),
            genre = o.str("genre"), mood = o.str("mood"), downloadable = true, quality = o.str("ext").uppercase(), stream = "/media/$id",
        )
    }

    /** A hit from the public YouTube music lists: played only through the official YouTube player. */
    fun fromYouTubeCatalog(o: JsonObject, list: String): Track {
        val vid = o.str("videoId")
        val artist = o.str("artist").ifEmpty { "YouTube" }
        return Track(
            id = "yt:$vid", src = "youtube", rawId = vid, title = o.str("title"), artist = artist,
            artistId = "yt:" + artist.split(",").first().trim().lowercase(), cover = "/api/yt/thumb/$vid", coverSm = "/api/yt/thumb/$vid",
            plays = o.num("views") / 50, quality = "YouTube Music", link = "https://www.youtube.com/watch?v=$vid",
            tags = listOf("youtube", if (list == "top") "classic hit" else "hit"),
        )
    }

    fun fromYouTube(o: JsonObject, title: String? = null, artist: String? = null): Track {
        val vid = o.str("videoId")
        val a = artist ?: o.str("artist").ifEmpty { o.str("channel") }
        return Track(
            id = "yt:$vid", src = "youtube", rawId = vid, title = title ?: o.str("track").ifEmpty { o.str("title") }, artist = a,
            artistId = "yt:" + a.split(",").first().trim().lowercase(), cover = "/api/yt/thumb/$vid", coverSm = "/api/yt/thumb/$vid",
            quality = "YouTube Music", link = "https://www.youtube.com/watch?v=$vid",
        )
    }

    /** Apple Music / Spotify: only the name and cover come from there; the sound is found elsewhere when it starts. */
    fun fromCatalog(o: JsonObject, src: String): Track {
        val name = if (src == "apple") "Apple Music" else "Spotify"
        val id = o.str("id")
        val cover = o.str("cover").let { if (it.isNotEmpty()) "/api/img?u=" + Api.enc(it) else if (src == "spotify" && id.length == 22) "/api/sp/cover/$id" else "" }
        return Track(
            id = "$src:$id", src = src, rawId = id, title = o.str("title"), artist = o.str("artist"),
            artistId = "cat:" + o.str("artist").split(",").first().trim().lowercase(), cover = cover, coverSm = cover,
            duration = o.num("duration"), genre = o.str("genre"), quality = name, link = o.str("link"), extLink = o.str("link"), extName = name,
            catalog = true, plays = o.num("popularity"),
        )
    }

    fun refresh(t: Track): Track = when (t.src) {
        "audius" -> if (t.rawId.isNotEmpty()) t.copy(stream = "$AU/stream/${t.rawId}") else t
        "jamendo" -> if (t.rawId.isNotEmpty()) t.copy(stream = "/api/ja/stream/${t.rawId}", cover = "/api/ja/art/${t.rawId}", coverSm = "/api/ja/art/${t.rawId}") else t
        else -> t
    }

    // ---------- catalogs ----------
    suspend fun auTrending(genre: String? = null, time: String = "week", limit: Int = 40, offset: Int = 0) =
        auList(Api.get(Api.path("$AU/tracks/trending", "time" to time, "limit" to limit, "offset" to offset.takeIf { it > 0 }, "genre" to genre), 600_000))

    suspend fun auSearch(q: String, limit: Int = 30) = auList(Api.get(Api.path("$AU/tracks/search", "query" to q, "limit" to limit), 300_000))

    suspend fun jaSearch(q: String, limit: Int = 20): List<Track> = runCatching {
        Api.get(Api.path("/api/ja/tracks", "search" to q, "limit" to limit), 600_000).obj["results"].arr.map { fromJamendo(it.obj) }
    }.getOrDefault(emptyList())

    suspend fun uploads(q: String = ""): List<Track> = runCatching {
        Api.get(Api.path("/api/uploads", "q" to q), 60_000).obj["data"].arr.map { fromLocal(it.obj) }
    }.getOrDefault(emptyList())

    suspend fun ytCatalog(list: String): List<Track> = runCatching {
        Api.get("/api/yt/catalog?list=$list", 3 * 3_600_000L).obj["data"].arr.map { it.obj }
            .filter { v ->
                val all = "${v.str("artist")} ${v.str("title")} ${v.str("full")}"
                v.str("artist").isNotEmpty() && !CATALOG_JUNK.containsMatchIn(all) && !FOREIGN_SCRIPT.containsMatchIn(all) && v.str("full").split(" | ").size < 3
            }
            .map { fromYouTubeCatalog(it, list) }
    }.getOrDefault(emptyList())

    suspend fun ytSearch(q: String): List<JsonObject> = Api.get(Api.path("/api/yt/search", "q" to q), 3_600_000).obj["data"].arr.map { it.obj }

    suspend fun appleSearch(q: String): List<Track> =
        Api.get(Api.path("/api/apple/search", "q" to q, "cc" to Locale.country()), 1_800_000).obj["data"].arr.map { fromCatalog(it.obj, "apple") }

    suspend fun appleTop(): List<Track> = runCatching {
        Api.get("/api/apple/top?cc=${Locale.country()}", 3 * 3_600_000L).obj["data"].arr.map { fromCatalog(it.obj, "apple") }
    }.getOrDefault(emptyList())

    data class ChartInfo(val id: String, val name: String)

    suspend fun charts(): List<ChartInfo> = runCatching {
        Api.get("/api/charts", 3_600_000).obj["charts"].arr.map { ChartInfo(it.obj.str("id"), it.obj.str("name")) }
    }.getOrDefault(emptyList())

    suspend fun spotifyChart(cc: String): List<Track> = runCatching {
        Api.get("/api/charts/$cc", 3_600_000).obj["items"].arr.map { it.obj }.map { o ->
            val with = o["with"].arr.map { it.asStr() }
            fromCatalog(
                JsonObject(
                    mapOf(
                        "id" to kotlinx.serialization.json.JsonPrimitive(o.str("spotify")),
                        "title" to kotlinx.serialization.json.JsonPrimitive(o.str("title")),
                        "artist" to kotlinx.serialization.json.JsonPrimitive((listOf(o.str("artist")) + with).joinToString(", ")),
                        "link" to kotlinx.serialization.json.JsonPrimitive("https://open.spotify.com/track/" + o.str("spotify")),
                        "popularity" to kotlinx.serialization.json.JsonPrimitive(o.num("streams") / 50),
                    )
                ),
                "spotify",
            )
        }
    }.getOrDefault(emptyList())

    /** Chart songs whose original video the server already found: "artist|title" → YouTube track. */
    suspend fun matchedHits(): Map<String, Track> = runCatching {
        Api.get("/api/hits/matched", 1_800_000).obj["map"].obj.mapValues { (_, v) ->
            fromYouTubeCatalog(v.obj, "chart")
        }
    }.getOrDefault(emptyMap())

    /** Everything we know as "artist|title" → playable YouTube track (charts lists and the server's matches). */
    suspend fun hitIndex(): Map<String, Track> = coroutineScope {
        val lists = listOf("daily", "trending", "top").map { async { ytCatalog(it) } }
        val matched = async { matchedHits() }
        val m = LinkedHashMap<String, Track>()
        for (l in lists.awaitAll()) for (t in l) m.putIfAbsent(normKey(t.artist, t.title), t)
        for ((k, v) in matched.await()) m.putIfAbsent(k, v)
        m
    }

    /** Combined search. src: all | youtube | apple | indie */
    suspend fun search(q: String, src: String): List<Track> = coroutineScope {
        val jobs = mutableListOf<kotlinx.coroutines.Deferred<List<Track>>>()
        if (src == "all" || src == "indie") {
            jobs += async { uploads(q) }
            jobs += async { runCatching { auSearch(q, 30) }.getOrDefault(emptyList()) }
            jobs += async { jaSearch(q, 15) }
        }
        if (src == "all" || src == "youtube") jobs += async {
            runCatching { ytSearch(q).filter { it.str("videoId").isNotEmpty() }.map { fromYouTube(it) } }.getOrDefault(emptyList())
        }
        if (src == "apple") jobs += async { runCatching { appleSearch(q) }.getOrDefault(emptyList()) }
        val all = jobs.awaitAll()
        // YouTube Music first in "all": real songs before independent uploads; then rank by how well they match
        val ordered = if (src == "all") (all.drop(3).flatten() + all.take(3).flatten()) else all.flatten()
        rank(dedupe(ordered), q)
    }

    fun dedupe(list: List<Track>): List<Track> {
        val seen = HashSet<String>()
        return list.filter { seen.add(it.id) && seen.add("k:" + normKey(it.artist, it.title) + it.src) }
    }

    private fun rank(list: List<Track>, q: String): List<Track> {
        val ql = q.lowercase().trim()
        val words = ql.split(Regex("\\s+"))
        fun sc(t: Track): Double {
            val title = t.title.lowercase(); val artist = t.artist.lowercase()
            var s = if (t.src == "local") 6.0 else if (t.src == "youtube") 2.0 else 0.0
            if (title == ql || artist == ql) s += 10
            if (title.startsWith(ql)) s += 4
            if ("$artist $title".contains(ql) || "$title $artist".contains(ql)) s += 3
            s += words.count { title.contains(it) || artist.contains(it) }
            s += kotlin.math.log10(t.plays + 1) * 0.3
            return s
        }
        return list.withIndex().sortedWith(compareByDescending<IndexedValue<Track>> { sc(it.value) }.thenBy { it.index }).map { it.value }
    }

    // ---------- playable version of a catalog song ----------
    private val resolved = HashMap<String, Track?>()

    /**
     * Apple Music / Spotify song → something that plays: the original on YouTube (phone only), or the same song from
     * Audius / Jamendo / uploads by the same artist. Covers and remixes never stand in for the original.
     */
    suspend fun resolve(t: Track, allowYouTube: Boolean): Track? {
        if (!t.catalog) return t
        val key = t.id + allowYouTube
        resolved[key]?.let { return it }
        val k = normKey(t.artist, t.title)
        var out: Track? = null
        if (allowYouTube) {
            val hit = runCatching { hitIndex()[k] }.getOrNull()
            if (hit != null) out = hit.copy(title = t.title, artist = t.artist, cover = t.cover.ifEmpty { hit.cover }, coverSm = t.coverSm.ifEmpty { hit.coverSm })
        }
        if (out == null) out = sameSong(t)
        if (out == null && allowYouTube) out = runCatching {
            ytSearch("${t.artist.split(",").first()} ${t.title}").firstOrNull()?.let { fromYouTube(it, t.title, t.artist) }
        }.getOrNull()
        out = out?.copy(extLink = t.link, extName = t.extName, via = t.src)
        resolved[key] = out
        return out
    }

    /** The same song (same artist, not a cover or remix) from a source with plain audio. */
    suspend fun sameSong(t: Track): Track? {
        val a = normKey(t.artist, "x").substringBefore("|")
        val title = normKey("x", t.title).substringAfter("|")
        if (title.isEmpty()) return null
        val bad = Regex("remix|cover|кавер|sped|slowed|nightcore|karaoke|instrumental", RegexOption.IGNORE_CASE)
        val cands = uploads(t.title) + runCatching { auSearch("${t.artist.split(",").first()} ${t.title}", 15) }.getOrDefault(emptyList())
        return cands.firstOrNull { c ->
            val ca = normKey(c.artist, "x").substringBefore("|")
            val ct = normKey("x", c.title).substringAfter("|")
            a.isNotEmpty() && (ca == a || ca.contains(a)) && (ct.contains(title) || title.contains(ct)) && !bad.containsMatchIn(c.title)
        }
    }
}

object Locale {
    fun country(): String {
        val l = java.util.Locale.getDefault()
        val c = l.country.lowercase()
        return if (c.length == 2) c else if (l.language == "ru") "ru" else "us"
    }
    fun isRu(): Boolean = java.util.Locale.getDefault().language == "ru" || country() in setOf("ru", "by", "kz", "ua", "kg", "uz", "am", "az", "ge", "md", "tj")
}
