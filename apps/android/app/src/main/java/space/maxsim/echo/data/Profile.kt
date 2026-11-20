package space.maxsim.echo.data

import android.content.Context
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import java.io.File

data class Playlist(val id: String, val title: String, val desc: String, val tracks: List<Track>, val at: Double)

/**
 * The listener's profile: the same JSON as the site (likes, playlists, history, taste maps, served memory), so a like
 * on the phone shows up on the site. Keys this app does not use are kept untouched.
 */
object Profile {
    private lateinit var file: File
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private var pushJob: Job? = null
    private var raw: MutableMap<String, JsonElement> = linkedMapOf()

    private val _version = MutableStateFlow(0)
    val version = _version.asStateFlow()

    private val _me = MutableStateFlow<String?>(null)
    val me = _me.asStateFlow()

    fun init(ctx: Context) {
        file = File(ctx.filesDir, "profile.json")
        raw = runCatching { Api.json.parseToJsonElement(file.readText()).obj.toMutableMap() }.getOrDefault(linkedMapOf())
    }

    // ---------- reading ----------
    val likes: List<Track>
        get() = raw["likes"].obj.values.mapNotNull { Track.fromJson(it) }.reversed()
    fun isLiked(t: Track) = raw["likes"].obj.containsKey(t.id)

    val playlists: List<Playlist>
        get() = raw["playlists"].arr.map { it.obj }.map { o ->
            Playlist(o.str("id"), o.str("title"), o.str("desc"), o["tracks"].arr.mapNotNull { Track.fromJson(it) }, o.num("at"))
        }

    val history: List<Track>
        get() = raw["hist"].arr.mapNotNull { Track.fromJson(it) }

    fun map(name: String): Map<String, Double> = raw[name].obj.mapValues { (it.value as? JsonPrimitive)?.content?.toDoubleOrNull() ?: 0.0 }
    fun served(): Map<String, Double> = map("served")
    fun skipAt(id: String): Double = map("skip")[id] ?: 0.0
    fun banned(t: Track) = raw["ban"].obj.containsKey(t.id) || (t.artistId.isNotEmpty() && raw["banA"].obj.containsKey(t.artistId))

    // ---------- writing ----------
    private fun putMap(name: String, k: String, v: Double?) {
        val m = raw[name].obj.toMutableMap()
        if (v == null) m.remove(k) else m[k] = JsonPrimitive(v)
        raw[name] = JsonObject(m)
    }

    private fun bump(name: String, k: String, by: Double) {
        if (k.isEmpty()) return
        val v = ((map(name)[k] ?: 0.0) + by).coerceIn(-20.0, 20.0)
        putMap(name, k, v)
    }

    /** Taste learning, same weights as the site: like 3, full listen 1.5, early skip −1.5, «Не то» −5. */
    fun learn(t: Track, w: Double) {
        bump("g", t.genre, w)
        bump("a", t.artistId, w * 0.8)
        if (t.artistId.isNotEmpty()) {
            val an = raw["an"].obj.toMutableMap(); an[t.artistId] = JsonPrimitive(t.artist); raw["an"] = JsonObject(an)
        }
        changed()
    }

    fun toggleLike(t: Track): Boolean {
        val m = raw["likes"].obj.toMutableMap()
        val on = if (m.containsKey(t.id)) { m.remove(t.id); false } else {
            m[t.id] = JsonObject(t.toJson() + ("likedAt" to JsonPrimitive(System.currentTimeMillis()))); true
        }
        raw["likes"] = JsonObject(m)
        learn(t, if (on) 3.0 else -2.0)
        return on
    }

    fun ban(t: Track) {
        putMap("ban", t.id, 1.0)
        learn(t, -5.0)
    }

    fun skipped(t: Track, seconds: Double) {
        putMap("skip", t.id, System.currentTimeMillis().toDouble())
        learn(t, if (seconds < 10) -1.5 else -0.8)
    }

    fun finished(t: Track) = learn(t, 1.5)

    fun listened(t: Track) {
        val h = mutableListOf<JsonElement>(JsonObject(t.toJson() + ("at" to JsonPrimitive(System.currentTimeMillis()))))
        h += raw["hist"].arr.filter { it.obj.str("id") != t.id }
        raw["hist"] = JsonArray(h.take(100))
        learn(t, 0.5)
    }

    fun markServed(list: List<Track>) {
        val now = System.currentTimeMillis().toDouble()
        val m = raw["served"].obj.toMutableMap()
        for (t in list) m[t.id] = JsonPrimitive(now)
        // keep two weeks
        val cut = now - 14 * 86_400_000.0
        raw["served"] = JsonObject(m.filterValues { ((it as? JsonPrimitive)?.content?.toDoubleOrNull() ?: 0.0) > cut })
        changed()
    }

    fun createPlaylist(title: String, tracks: List<Track> = emptyList()): String {
        val id = System.currentTimeMillis().toString(36) + (1000..9999).random().toString(36)
        val pl = JsonObject(
            mapOf(
                "id" to JsonPrimitive(id), "title" to JsonPrimitive(title.take(80)), "desc" to JsonPrimitive(""),
                "tracks" to JsonArray(tracks.take(1000).map { it.toJson() }), "at" to JsonPrimitive(System.currentTimeMillis()),
            )
        )
        raw["playlists"] = JsonArray(listOf<JsonElement>(pl) + raw["playlists"].arr)
        changed()
        return id
    }

    fun addToPlaylist(id: String, t: Track): Boolean {
        var added = false
        raw["playlists"] = JsonArray(raw["playlists"].arr.map { e ->
            val o = e.obj
            if (o.str("id") != id || o["tracks"].arr.any { it.obj.str("id") == t.id }) e
            else { added = true; JsonObject(o + ("tracks" to JsonArray(o["tracks"].arr + t.toJson())) + ("at" to JsonPrimitive(System.currentTimeMillis()))) }
        })
        if (added) changed()
        return added
    }

    fun deletePlaylist(id: String) {
        raw["playlists"] = JsonArray(raw["playlists"].arr.filter { it.obj.str("id") != id })
        changed()
    }

    private fun changed() {
        raw["updated"] = JsonPrimitive(System.currentTimeMillis())
        _version.value++
        scope.launch { runCatching { file.writeText(JsonObject(raw).toString()) } }
        schedulePush()
    }

    // ---------- account ----------
    suspend fun refreshMe() {
        _me.value = runCatching { Api.get("/api/me").obj["user"].obj.str("email").ifEmpty { null } }.getOrNull()
        if (_me.value != null) pull()
    }

    /** Server and phone profiles are merged: nothing liked or saved on either side is lost. */
    private suspend fun pull() {
        val server = runCatching { Api.get("/api/profile").obj["data"] as? JsonObject }.getOrNull() ?: return
        val serverNewer = server.num("updated") > (raw["updated"] as? JsonPrimitive)?.content?.toDoubleOrNull() ?: 0.0
        val base = if (serverNewer) server.toMutableMap() else raw.toMutableMap()
        val other = if (serverNewer) raw else server
        for (k in listOf("likes", "ban", "banA", "an", "fav", "served")) base[k] = JsonObject(other[k].obj + base[k].obj)
        val ids = base["playlists"].arr.map { it.obj.str("id") }.toSet()
        base["playlists"] = JsonArray(base["playlists"].arr + other["playlists"].arr.filter { it.obj.str("id") !in ids })
        for (k in listOf("g", "a", "m", "t")) base[k] = JsonObject(other[k].obj + base[k].obj)
        raw = base
        _version.value++
        runCatching { file.writeText(JsonObject(raw).toString()) }
        schedulePush()
    }

    private fun schedulePush() {
        if (_me.value == null) return
        pushJob?.cancel()
        pushJob = scope.launch {
            delay(2500)
            runCatching { Api.send("/api/profile", JsonObject(mapOf("data" to JsonObject(raw))), "PUT") }
        }
    }

    suspend fun requestCode(email: String) { Api.send("/api/auth/code", JsonObject(mapOf("email" to JsonPrimitive(email)))) }

    suspend fun verify(email: String, code: String) {
        Api.send("/api/auth/verify", JsonObject(mapOf("email" to JsonPrimitive(email), "code" to JsonPrimitive(code))))
        refreshMe()
    }

    suspend fun logout() {
        runCatching { Api.send("/api/auth/logout") }
        Api.clearSession()
        _me.value = null
    }
}
