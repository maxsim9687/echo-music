package space.maxsim.echo.data

import android.content.Context
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlin.math.abs
import kotlin.math.ln
import kotlin.math.log10
import kotlin.random.Random

/** Settings of «Моя волна» on this device. */
data class WaveConfig(
    val youtube: Boolean = true,
    val spotify: Boolean = true,
    val apple: Boolean = true,
    val indie: Boolean = false,
    val novelty: Int = 50,
)

class MoodPreset(val name: String, val genres: Set<String>, val tags: List<String>, val yt: List<String>, val ytArtists: List<String>, val queries: List<String>)

/**
 * «Моя волна» for the app: candidates from YouTube Music hits, the Spotify / Apple Music charts (only songs whose
 * original is known), independent artists if switched on, and your likes; ranked by your taste, freshness and variety.
 * In the car only songs with plain audio are used (YouTube needs its visible player).
 */
object Wave {
    private lateinit var prefs: android.content.SharedPreferences
    val cfg = MutableStateFlow(WaveConfig())
    val mood = MutableStateFlow<String?>(null)

    val MOODS = listOf(
        MoodPreset("Энергия", setOf("Electronic", "Drum & Bass", "House", "Trap", "Hip-Hop/Rap", "Pop", "Rock"), emptyList(),
            listOf("workout motivation songs", "energetic pop songs", "hype rap songs"), emptyList(), listOf("energy", "workout", "hype")),
        MoodPreset("Спокойствие", setOf("Ambient", "Lo-Fi", "Acoustic", "Jazz", "Folk", "Classical"), emptyList(),
            listOf("calm acoustic songs", "chill indie songs", "relaxing soft songs"), emptyList(), listOf("chill", "calm", "relax")),
        MoodPreset("Фокус", setOf("Lo-Fi", "Ambient", "Electronic", "Classical", "Downtempo"), emptyList(),
            listOf("lofi hip hop beat", "study music instrumental"), emptyList(), listOf("lofi study", "focus")),
        MoodPreset("Ночь", setOf("Deep House", "Techno", "Downtempo", "R&B/Soul", "Synthwave"), emptyList(),
            listOf("late night r&b songs", "night drive synthwave"), listOf("the weeknd", "lana del rey"), listOf("night drive", "late night")),
        MoodPreset("Грусть", setOf("Alternative", "Acoustic", "Ambient", "Lo-Fi", "Pop", "Folk"), emptyList(),
            listOf("sad songs", "heartbreak songs", "грустные песни"), listOf("billie eilish", "lewis capaldi", "olivia rodrigo"), listOf("sad", "heartbreak")),
        MoodPreset("Phonk", setOf("Trap", "Hip-Hop/Rap", "Electronic"), listOf("phonk", "memphis", "cowbell"),
            listOf("Kordhell phonk", "INTERWORLD phonk", "DVRST phonk", "memphis phonk"),
            listOf("kordhell", "dvrst", "interworld", "playaphonk", "kslv", "moondeity", "hensonn", "ghostface playa", "pharmacist", "dxrk"), listOf("phonk", "dark phonk")),
        MoodPreset("Drift Phonk", setOf("Trap", "Electronic", "Hip-Hop/Rap"), listOf("drift", "phonk", "brazilian"),
            listOf("drift phonk", "brazilian phonk", "MXZI phonk", "DJ FKU phonk"),
            listOf("mxzi", "dj fku", "sma\$her", "onimxru", "kaito shoma", "dyrtywork", "hxi", "slowboy", "dj samir"), listOf("drift phonk", "brazilian phonk")),
    )

    fun init(ctx: Context) {
        prefs = ctx.getSharedPreferences("echo.wave", Context.MODE_PRIVATE)
        cfg.value = WaveConfig(
            prefs.getBoolean("yt", true), prefs.getBoolean("sp", true), prefs.getBoolean("ap", true), prefs.getBoolean("indie", false), prefs.getInt("nov", 50),
        )
    }

    fun setCfg(c: WaveConfig) {
        cfg.value = c
        prefs.edit().putBoolean("yt", c.youtube).putBoolean("sp", c.spotify).putBoolean("ap", c.apple).putBoolean("indie", c.indie).putInt("nov", c.novelty).apply()
        pool = emptyList()
    }

    private var pool: List<Track> = emptyList()
    private var poolKey = ""
    private var poolAt = 0L

    private val NOT_SONG = Regex("\\b(mix|mixtape|playlist|compilation|hour|hours|nonstop|full album)\\b", RegexOption.IGNORE_CASE)
    private val YT_JUNK = Regex("#|\\btop\\s?\\d+|\\bbest\\b|\\bsongs\\b|\\bradio\\b|24/7|\\bmix\\b|playlist|compilation|\\bhours?\\b|\\?", RegexOption.IGNORE_CASE)

    private suspend fun buildPool(car: Boolean): List<Track> = coroutineScope {
        val c = cfg.value
        val m = MOODS.firstOrNull { it.name == mood.value }
        val yt = c.youtube && !car
        val indie = c.indie || car
        val jobs = mutableListOf<kotlinx.coroutines.Deferred<List<Track>>>()
        val idx = async { if (c.spotify || c.apple) Sources.hitIndex() else emptyMap() }

        if (m != null) {
            if (yt) for (q in m.yt.shuffled().take(2)) jobs += async {
                runCatching {
                    Sources.ytSearch(q).filter { v ->
                        val title = v.str("title")
                        !YT_JUNK.containsMatchIn(title) && (Regex("\\s[-–—]\\s").containsMatchIn(title) || Regex("-\\s*Topic$|VEVO$", RegexOption.IGNORE_CASE).containsMatchIn(v.str("channel")))
                    }.map { Sources.fromYouTube(it).copy(via = "mood") }
                }.getOrDefault(emptyList())
            }
            if (yt && m.ytArtists.isNotEmpty()) jobs += async {
                listOf("daily", "trending", "top").flatMap { Sources.ytCatalog(it) }.filter { t -> m.ytArtists.any { t.artist.lowercase().contains(it) } }.map { it.copy(via = "mood") }
            }
            if (indie) for (q in m.queries.shuffled().take(2)) jobs += async { runCatching { Sources.auSearch(q, 40) }.getOrDefault(emptyList()) }
        } else {
            if (yt) {
                jobs += async { Sources.ytCatalog("daily") }
                jobs += async { Sources.ytCatalog("trending") }
            }
            if (c.spotify) jobs += async {
                val near = if (Locale.isRu()) listOf("global", "kz", "by") else listOf("global", Locale.country())
                val ix = idx.await()
                near.distinct().flatMap { Sources.spotifyChart(it) }.mapNotNull { t -> playableChart(t, ix, yt) }
            }
            if (c.apple) jobs += async {
                val ix = idx.await()
                Sources.appleTop().mapNotNull { t -> playableChart(t, ix, yt) }
            }
            if (indie) {
                val topG = Profile.map("g").entries.filter { it.value > 0.5 }.sortedByDescending { it.value }.take(3).map { it.key }
                for (g in topG.ifEmpty { listOf(null) }) jobs += async {
                    runCatching { Sources.auTrending(g, "week", 40, listOf(0, 0, 20, 40).random()) }.getOrDefault(emptyList())
                }
                jobs += async { runCatching { Sources.auTrending(null, "month", 50) }.getOrDefault(emptyList()) }
            }
        }
        // your likes come back now and then; the site's uploads only if you like those artists
        jobs += async { Profile.likes.filter { !car || it.isDirect } }
        jobs += async { val a = Profile.map("a"); Sources.uploads().filter { (a[it.artistId] ?: 0.0) > 0.5 } }
        Sources.dedupe(jobs.awaitAll().flatten()).filter { (it.isDirect || (yt && it.isYouTube)) && !NOT_SONG.containsMatchIn(it.title) }
    }

    /** A chart song plays only as its original: the known YouTube video (phone), or nothing. */
    private fun playableChart(t: Track, idx: Map<String, Track>, yt: Boolean): Track? {
        if (!yt) return null
        val hit = idx[normKey(t.artist, t.title)] ?: return null
        return hit.copy(title = t.title, artist = t.artist, via = t.src, extLink = t.link, extName = t.extName, plays = maxOf(hit.plays, t.plays))
    }

    private fun score(t: Track, recent: List<Track>, rnd: Random): Double {
        if (Profile.banned(t)) return Double.NEGATIVE_INFINITY
        val g = Profile.map("g"); val a = Profile.map("a")
        val nov = cfg.value.novelty / 100.0
        var s = 0.0
        s += (g[t.genre] ?: 0.0) * 0.35
        val aa = a[t.artistId] ?: 0.0
        s += aa * (0.5 - nov * 0.35)
        if (aa == 0.0 && !Profile.isLiked(t)) s += (nov - 0.4) * 3
        if (Profile.isLiked(t)) s += if (nov < 0.4) 2.0 else if (nov > 0.7) -5.0 else -1.0
        s += log10(t.plays + 10) * 0.4
        if (t.via == "spotify" || t.via == "apple") s += 1.2
        if ("classic hit" in t.tags) s -= 1.2
        if (Locale.isRu() && Regex("[а-яё]", RegexOption.IGNORE_CASE).containsMatchIn(t.title + t.artist)) s += if (Profile.history.size < 15) 2.2 else 0.9
        val m = MOODS.firstOrNull { it.name == mood.value }
        if (m != null) {
            val text = (t.title + " " + t.tags.joinToString(" ")).lowercase()
            val fit = when {
                t.isYouTube -> m.ytArtists.any { t.artist.lowercase().contains(it) } || t.via == "mood"
                m.tags.isNotEmpty() -> m.tags.any { text.contains(it) }
                else -> t.genre in m.genres
            }
            if (!fit && !(t.isYouTube && m.ytArtists.isEmpty())) return Double.NEGATIVE_INFINITY
            s += 4
        }
        // freshness: what the wave already gave you recently is pushed away for days
        val served = Profile.served()[t.id]
        if (served != null) {
            val age = System.currentTimeMillis() - served
            s -= when { age < 86_400_000 -> 14.0; age < 3 * 86_400_000 -> 8.0; age < 7 * 86_400_000 -> 4.0; else -> 1.0 }
        }
        val skip = Profile.skipAt(t.id)
        if (skip > 0) s -= if (System.currentTimeMillis() - skip < 7 * 86_400_000) 5.0 else 2.0
        if (recent.any { it.id == t.id }) s -= 30
        // Gumbel noise: every batch is a fresh draw among the good ones
        s += -ln(-ln(rnd.nextDouble().coerceIn(1e-9, 1 - 1e-9))) * 0.9
        return s
    }

    /** Next batch: best scores with artist spacing (no artist twice in a row, at most two per batch). */
    suspend fun next(n: Int, recent: List<Track>, car: Boolean = false): List<Track> {
        val key = "${mood.value}|$car|${cfg.value}"
        if (key != poolKey || pool.isEmpty() || System.currentTimeMillis() - poolAt > 15 * 60_000) {
            pool = buildPool(car); poolKey = key; poolAt = System.currentTimeMillis()
        }
        val rnd = Random(System.nanoTime())
        val scored = pool.map { it to score(it, recent, rnd) }.filter { it.second.isFinite() }.sortedByDescending { it.second }
        val out = mutableListOf<Track>()
        val perArtist = HashMap<String, Int>()
        for ((t, _) in scored) {
            if (out.size >= n) break
            val last = (recent.takeLast(3) + out.takeLast(3)).map { it.artistId }
            if (t.artistId.isNotEmpty() && t.artistId in last) continue
            if ((perArtist[t.artistId] ?: 0) >= 2) continue
            if (out.any { abs(it.title.length - t.title.length) == 0 && it.title.equals(t.title, true) }) continue
            out += t
            perArtist[t.artistId] = (perArtist[t.artistId] ?: 0) + 1
        }
        Profile.markServed(out)
        return out
    }

    fun reset() { pool = emptyList() }
}
