package space.maxsim.echo.player

import android.content.ComponentName
import android.content.Context
import androidx.core.content.ContextCompat
import androidx.media3.common.MediaItem
import androidx.media3.common.Player
import androidx.media3.session.MediaController
import androidx.media3.session.SessionToken
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import space.maxsim.echo.data.Api
import space.maxsim.echo.data.Profile
import space.maxsim.echo.data.Sources
import space.maxsim.echo.data.Track
import space.maxsim.echo.data.Wave

/**
 * The phone's queue. Songs with plain audio go to the playback service (background, lock screen, car); YouTube songs
 * play in the official YouTube player that the screen shows (YouTube's rule: its music only with its visible player).
 */
object Hub {
    val queue = MutableStateFlow<List<Track>>(emptyList())
    val index = MutableStateFlow(-1)
    val playing = MutableStateFlow(false)
    val position = MutableStateFlow(0.0)
    val duration = MutableStateFlow(0.0)
    val mode = MutableStateFlow("list")
    /** Video id the YouTube player should show, or null. */
    val ytVideo = MutableStateFlow<String?>(null)
    /** Commands for the YouTube player: "load:<id>", "play", "pause", "seek:<s>". */
    val ytCmd = MutableSharedFlow<String>(extraBufferCapacity = 8)
    val toast = MutableSharedFlow<String>(extraBufferCapacity = 4)
    val busy = MutableStateFlow(false)

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private var controller: MediaController? = null
    private var segStart = 0
    private var segLen = 0
    private var ytFails = 0
    private var refilling = false

    val current: Track? get() = queue.value.getOrNull(index.value)

    fun connect(ctx: Context) {
        val token = SessionToken(ctx, ComponentName(ctx, PlaybackService::class.java))
        val f = MediaController.Builder(ctx, token).buildAsync()
        f.addListener({
            val c = runCatching { f.get() }.getOrNull() ?: return@addListener
            controller = c
            c.addListener(object : Player.Listener {
                override fun onIsPlayingChanged(isPlaying: Boolean) { if (ytVideo.value == null) playing.value = isPlaying }
                override fun onMediaItemTransition(mediaItem: MediaItem?, reason: Int) {
                    if (ytVideo.value != null || segLen == 0) return
                    val i = segStart + c.currentMediaItemIndex
                    if (i in queue.value.indices) { index.value = i; afterIndexChange() }
                }
                override fun onPlaybackStateChanged(state: Int) {
                    if (state == Player.STATE_ENDED && ytVideo.value == null && segLen > 0) {
                        current?.let { Profile.finished(it) }
                        load(segStart + segLen)
                    }
                }
            })
        }, ContextCompat.getMainExecutor(ctx))
        scope.launch {
            while (true) {
                delay(500)
                val c = controller
                if (ytVideo.value == null && c != null && c.mediaItemCount > 0) {
                    position.value = c.currentPosition / 1000.0
                    duration.value = (c.duration.takeIf { it > 0 } ?: 0L) / 1000.0
                }
            }
        }
    }

    fun play(list: List<Track>, start: Int = 0, m: String = "list") {
        if (list.isEmpty()) { toast.tryEmit("Здесь пока нечего играть"); return }
        mode.value = m
        queue.value = list.map { Sources.refresh(it) }
        load(start)
    }

    private fun load(i: Int) {
        val q = queue.value
        if (i !in q.indices) {
            if (mode.value == "wave") refill(thenPlay = i) else { pauseAll(); playing.value = false }
            return
        }
        index.value = i
        val t = q[i]
        if (t.catalog) {
            busy.value = true
            scope.launch {
                val r = Sources.resolve(t, allowYouTube = true)
                busy.value = false
                if (index.value != i) return@launch
                if (r == null) {
                    toast.tryEmit("Полной версии «${t.title}» не нашлось")
                    queue.value = queue.value.toMutableList().also { it.removeAt(i) }
                    load(i)
                } else {
                    queue.value = queue.value.toMutableList().also { it[i] = r }
                    load(i)
                }
            }
            return
        }
        if (t.isYouTube) {
            controller?.run { pause(); clearMediaItems() }
            segLen = 0
            ytVideo.value = t.rawId
            position.value = 0.0; duration.value = t.duration
            ytCmd.tryEmit("load:" + t.rawId)
            Profile.listened(t)
        } else {
            if (ytVideo.value != null) { ytCmd.tryEmit("pause"); ytVideo.value = null }
            // a run of plain-audio songs goes to the player at once, so lock screen and headphones can skip
            val seg = q.drop(i).takeWhile { it.isDirect && !it.catalog }.take(40)
            segStart = i; segLen = seg.size
            val c = controller
            if (c == null) { toast.tryEmit("Плеер ещё запускается, нажмите ещё раз"); return }
            c.setMediaItems(seg.map { TrackCache.item(it) }, 0, 0)
            c.prepare(); c.play()
        }
        afterIndexChange()
    }

    private fun afterIndexChange() {
        if (mode.value == "wave" && queue.value.size - index.value <= 3) refill(thenPlay = null)
    }

    private fun refill(thenPlay: Int?) {
        if (refilling) return
        refilling = true
        scope.launch {
            val more = runCatching { Wave.next(6, queue.value.takeLast(10)) }.getOrDefault(emptyList())
            refilling = false
            if (more.isEmpty()) { if (thenPlay != null) toast.tryEmit("Волна не нашла новых треков, попробуйте позже"); return@launch }
            val before = queue.value.size
            queue.value = queue.value + more
            // extend the running audio run when the new songs continue it
            val c = controller
            if (thenPlay == null && ytVideo.value == null && c != null && segLen > 0 && segStart + segLen == before) {
                val add = more.takeWhile { it.isDirect && !it.catalog }
                if (add.isNotEmpty()) { c.addMediaItems(add.map { TrackCache.item(it) }); segLen += add.size }
            }
            if (thenPlay != null) load(thenPlay)
        }
    }

    fun startWave(mood: String? = Wave.mood.value) {
        if (mode.value == "wave" && current != null && Wave.mood.value == mood) { toggle(); return }
        Wave.mood.value = mood
        busy.value = true
        scope.launch {
            val batch = runCatching { Wave.next(6, Profile.history.take(8)) }.getOrDefault(emptyList())
            busy.value = false
            if (batch.isEmpty()) toast.tryEmit("Каталог не ответил, попробуйте через пару секунд")
            else play(batch, 0, "wave")
        }
    }

    fun toggle() {
        val t = current ?: run { startWave(); return }
        if (t.isYouTube) { ytCmd.tryEmit(if (playing.value) "pause" else "play"); return }
        val c = controller ?: return
        if (c.isPlaying) c.pause() else { if (c.mediaItemCount == 0) load(index.value) else c.play() }
    }

    fun next(skip: Boolean = true) {
        val t = current
        if (t != null && skip && position.value < 30) Profile.skipped(t, position.value)
        val c = controller
        if (t != null && !t.isYouTube && c != null && c.hasNextMediaItem()) c.seekToNextMediaItem() else load(index.value + 1)
    }

    fun prev() {
        val c = controller
        if (position.value > 4) { seek(0.0); return }
        if (current?.isYouTube == false && c != null && c.hasPreviousMediaItem()) c.seekToPreviousMediaItem() else if (index.value > 0) load(index.value - 1)
    }

    fun seek(sec: Double) {
        if (current?.isYouTube == true) ytCmd.tryEmit("seek:$sec") else controller?.seekTo((sec * 1000).toLong())
    }

    fun playAt(i: Int) = load(i)

    fun ban() {
        val t = current ?: return
        Profile.ban(t)
        toast.tryEmit("Больше не покажем «${t.title}»")
        next(skip = false)
    }

    private fun pauseAll() { controller?.pause(); ytCmd.tryEmit("pause") }

    // ---- events from the YouTube player ----
    fun onYtState(state: Int) {
        if (ytVideo.value == null) return
        when (state) {
            1 -> { playing.value = true; ytFails = 0 }
            2, -1, 5 -> playing.value = false
            0 -> { playing.value = false; current?.let { Profile.finished(it) }; load(index.value + 1) }
        }
    }

    fun onYtTime(cur: Double, dur: Double) {
        if (ytVideo.value == null) return
        position.value = cur; if (dur > 0) duration.value = dur
    }

    fun onYtError(code: Int) {
        val t = current ?: return
        if (code == 100 || code == 101 || code == 150) scope.launch {
            runCatching { Api.send("/api/yt/bad", JsonObject(mapOf("id" to JsonPrimitive(t.rawId)))) }
        }
        ytFails++
        // the same song without YouTube, if it exists; otherwise the next one
        scope.launch {
            val alt = Sources.sameSong(t)
            if (alt != null && current?.id == t.id) {
                queue.value = queue.value.toMutableList().also { it[index.value] = alt.copy(extLink = t.link, extName = "YouTube") }
                load(index.value)
            } else {
                if (ytFails <= 3) toast.tryEmit("Этот клип YouTube не даёт встроить, включаю следующий")
                load(index.value + 1)
            }
        }
    }
}
