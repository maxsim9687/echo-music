package space.maxsim.echo.data

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

/** One song from any source. Same fields as the site's tracks, so likes and playlists are shared. */
data class Track(
    val id: String,
    val src: String,
    val rawId: String = "",
    val title: String,
    val artist: String,
    val artistId: String = "",
    val cover: String = "",
    val coverSm: String = "",
    val duration: Double = 0.0,
    val genre: String = "",
    val mood: String = "",
    val tags: List<String> = emptyList(),
    val plays: Double = 0.0,
    val downloadable: Boolean = false,
    val quality: String = "",
    val stream: String = "",
    val link: String = "",
    val via: String = "",
    val extLink: String = "",
    val extName: String = "",
    val catalog: Boolean = false,
    val releaseDate: String = "",
    val reason: String = "",
) {
    val isYouTube get() = src == "youtube"
    /** Plays as plain audio (needed for the car and for background playback). */
    val isDirect get() = src in DIRECT && stream.isNotEmpty()
    val art get() = Api.url(cover.ifEmpty { coverSm })
    val artSm get() = Api.url(coverSm.ifEmpty { cover })

    fun toJson(): JsonObject {
        val m = linkedMapOf<String, JsonElement>()
        fun put(k: String, v: String) { if (v.isNotEmpty()) m[k] = JsonPrimitive(v) }
        put("id", id); put("src", src); put("rawId", rawId); put("title", title); put("artist", artist); put("artistId", artistId)
        put("cover", cover); put("coverSm", coverSm); put("genre", genre); put("mood", mood); put("quality", quality)
        put("stream", stream); put("link", link); put("via", via); put("extLink", extLink); put("extName", extName); put("releaseDate", releaseDate)
        if (duration > 0) m["duration"] = JsonPrimitive(duration)
        if (plays > 0) m["plays"] = JsonPrimitive(plays)
        if (downloadable) m["downloadable"] = JsonPrimitive(true)
        if (catalog) m["catalog"] = JsonPrimitive(true)
        if (tags.isNotEmpty()) m["tags"] = JsonArray(tags.take(6).map { JsonPrimitive(it) })
        return JsonObject(m)
    }

    companion object {
        val DIRECT = setOf("audius", "jamendo", "local")

        fun fromJson(e: JsonElement): Track? {
            val o = e as? JsonObject ?: return null
            val id = o.str("id"); val title = o.str("title")
            if (id.isEmpty() || title.isEmpty()) return null
            return Sources.refresh(
                Track(
                    id = id, src = o.str("src"), rawId = o.str("rawId"), title = title, artist = o.str("artist"), artistId = o.str("artistId"),
                    cover = o.str("cover"), coverSm = o.str("coverSm"), duration = o.num("duration"), genre = o.str("genre"), mood = o.str("mood"),
                    tags = o["tags"].arr.map { it.asStr() }, plays = o.num("plays"), downloadable = o.bool("downloadable"), quality = o.str("quality"),
                    stream = o.str("stream"), link = o.str("link"), via = o.str("via"), extLink = o.str("extLink"), extName = o.str("extName"),
                    catalog = o.bool("catalog"), releaseDate = o.str("releaseDate"),
                )
            )
        }
    }
}

fun fmtTime(sec: Double): String {
    if (sec <= 0 || sec.isNaN()) return "0:00"
    val s = sec.toInt()
    return "%d:%02d".format(s / 60, s % 60)
}

/** "artist|title" with the same normalisation as the site and the server (for matching charts to videos). */
fun normKey(artist: String, title: String): String {
    fun nk(s: String) = s.lowercase()
        .replace(Regex("\\(.*?\\)|\\[.*?\\]"), " ")
        .replace(Regex("feat\\.?.*$|ft\\..*$", RegexOption.IGNORE_CASE), " ")
        .replace(Regex("[^a-z0-9а-яё]+"), " ")
        .trim()
    return nk(artist.split(",").first()) + "|" + nk(title)
}
