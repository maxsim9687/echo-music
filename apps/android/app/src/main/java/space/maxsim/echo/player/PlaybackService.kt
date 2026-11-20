package space.maxsim.echo.player

import android.app.PendingIntent
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import androidx.media3.common.Player
import androidx.media3.datasource.okhttp.OkHttpDataSource
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import space.maxsim.echo.data.Api
import androidx.media3.session.LibraryResult
import androidx.media3.session.MediaLibraryService
import androidx.media3.session.MediaLibraryService.LibraryParams
import androidx.media3.session.MediaLibraryService.MediaLibrarySession
import androidx.media3.session.MediaSession
import com.google.common.collect.ImmutableList
import com.google.common.util.concurrent.ListenableFuture
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.guava.future
import kotlinx.coroutines.launch
import space.maxsim.echo.data.Profile
import space.maxsim.echo.data.Sources
import space.maxsim.echo.data.Track
import space.maxsim.echo.data.Wave
import space.maxsim.echo.ui.MainActivity
import java.util.concurrent.ConcurrentHashMap

/** Tracks the player knows by media id ("t:<track id>"), shared by the phone UI, the car and the lock screen. */
object TrackCache {
    private val map = ConcurrentHashMap<String, Track>()
    fun put(t: Track): String { map["t:" + t.id] = t; return "t:" + t.id }
    fun get(mediaId: String): Track? = map[mediaId]

    fun item(t: Track): MediaItem {
        val id = put(t)
        val md = MediaMetadata.Builder()
            .setTitle(t.title).setArtist(t.artist).setDisplayTitle(t.title).setSubtitle(t.artist)
            .setIsPlayable(true).setIsBrowsable(false).setMediaType(MediaMetadata.MEDIA_TYPE_MUSIC)
        if (t.art.isNotEmpty()) md.setArtworkUri(Uri.parse(t.art))
        return MediaItem.Builder().setMediaId(id).setUri(space.maxsim.echo.data.Api.url(t.stream)).setMediaMetadata(md.build()).build()
    }

    fun folder(id: String, title: String, subtitle: String = "", playable: Boolean = false): MediaItem =
        MediaItem.Builder().setMediaId(id).setMediaMetadata(
            MediaMetadata.Builder().setTitle(title).setSubtitle(subtitle).setIsBrowsable(!playable).setIsPlayable(playable)
                .setMediaType(if (playable) MediaMetadata.MEDIA_TYPE_PLAYLIST else MediaMetadata.MEDIA_TYPE_FOLDER_MIXED).build()
        ).build()
}

/**
 * Background playback, lock-screen controls and Android Auto. The car sees: «Моя волна» (and a wave per mood),
 * «Любимое», «Плейлисты», «Недавние» and search. Only songs with plain audio are offered there: YouTube allows its
 * music only with its own visible player, which a car screen does not have.
 */
class PlaybackService : MediaLibraryService() {
    private var session: MediaLibrarySession? = null
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private var carWave = false
    private val lastSearch = ConcurrentHashMap<String, List<Track>>()

    override fun onCreate() {
        super.onCreate()
        val player = ExoPlayer.Builder(this)
            .setMediaSourceFactory(DefaultMediaSourceFactory(OkHttpDataSource.Factory(Api.http)))
            .setAudioAttributes(AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_MUSIC).build(), true)
            .setHandleAudioBecomingNoisy(true)
            .setWakeMode(C.WAKE_MODE_NETWORK)
            .build()
        player.addListener(object : Player.Listener {
            override fun onMediaItemTransition(mediaItem: MediaItem?, reason: Int) {
                // the car's wave keeps going on its own
                if (carWave && player.mediaItemCount - player.currentMediaItemIndex <= 3) scope.launch {
                    val recent = (0 until player.mediaItemCount).mapNotNull { TrackCache.get(player.getMediaItemAt(it).mediaId) }
                    val more = runCatching { Wave.next(6, recent, car = true) }.getOrDefault(emptyList())
                    player.addMediaItems(more.map { TrackCache.item(it) })
                }
                mediaItem?.let { TrackCache.get(it.mediaId) }?.let { Profile.listened(it) }
            }
        })
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        session = MediaLibrarySession.Builder(this, player, Callback()).setSessionActivity(open).build()
    }

    override fun onGetSession(controllerInfo: MediaSession.ControllerInfo): MediaLibrarySession? = session

    override fun onTaskRemoved(rootIntent: Intent?) {
        val p = session?.player
        if (p == null || !p.playWhenReady || p.mediaItemCount == 0) stopSelf()
    }

    override fun onDestroy() {
        scope.cancel()
        session?.run { player.release(); release() }
        session = null
        super.onDestroy()
    }

    private inner class Callback : MediaLibrarySession.Callback {
        override fun onGetLibraryRoot(session: MediaLibrarySession, browser: MediaSession.ControllerInfo, params: LibraryParams?): ListenableFuture<LibraryResult<MediaItem>> =
            scope.future { LibraryResult.ofItem(TrackCache.folder("root", "Эхо"), params) }

        override fun onGetChildren(
            session: MediaLibrarySession, browser: MediaSession.ControllerInfo, parentId: String, page: Int, pageSize: Int, params: LibraryParams?,
        ): ListenableFuture<LibraryResult<ImmutableList<MediaItem>>> = scope.future {
            val items: List<MediaItem> = when {
                parentId == "root" -> listOf(
                    TrackCache.folder("wave", "Моя волна", "Подобрано под вас", playable = true),
                    TrackCache.folder("moods", "Настроение"),
                    TrackCache.folder("likes", "Любимое"),
                    TrackCache.folder("playlists", "Плейлисты"),
                    TrackCache.folder("recent", "Недавние"),
                )
                parentId == "moods" -> Wave.MOODS.map { TrackCache.folder("wave:" + it.name, it.name, "Волна под настроение", playable = true) }
                parentId == "likes" -> Profile.likes.filter { it.isDirect }.take(200).map { TrackCache.item(it) }
                parentId == "recent" -> Profile.history.filter { it.isDirect }.take(100).map { TrackCache.item(it) }
                parentId == "playlists" -> Profile.playlists.map { TrackCache.folder("pl:" + it.id, it.title, "${it.tracks.count { t -> t.isDirect }} треков") }
                parentId.startsWith("pl:") -> Profile.playlists.firstOrNull { it.id == parentId.removePrefix("pl:") }?.tracks.orEmpty().filter { it.isDirect }.map { TrackCache.item(it) }
                else -> emptyList()
            }
            LibraryResult.ofItemList(ImmutableList.copyOf(items), params)
        }

        override fun onGetItem(session: MediaLibrarySession, browser: MediaSession.ControllerInfo, mediaId: String): ListenableFuture<LibraryResult<MediaItem>> =
            scope.future {
                val t = TrackCache.get(mediaId)
                if (t != null) LibraryResult.ofItem(TrackCache.item(t), null) else LibraryResult.ofError(LibraryResult.RESULT_ERROR_BAD_VALUE)
            }

        override fun onSearch(session: MediaLibrarySession, browser: MediaSession.ControllerInfo, query: String, params: LibraryParams?): ListenableFuture<LibraryResult<Void>> =
            scope.future {
                val found = runCatching { Sources.search(query, "indie") }.getOrDefault(emptyList()).filter { it.isDirect }.take(50)
                val liked = Profile.likes.filter { it.isDirect && (it.title + " " + it.artist).contains(query, ignoreCase = true) }
                val all = Sources.dedupe(liked + found)
                lastSearch[query] = all
                session.notifySearchResultChanged(browser, query, all.size, params)
                LibraryResult.ofVoid()
            }

        override fun onGetSearchResult(
            session: MediaLibrarySession, browser: MediaSession.ControllerInfo, query: String, page: Int, pageSize: Int, params: LibraryParams?,
        ): ListenableFuture<LibraryResult<ImmutableList<MediaItem>>> = scope.future {
            val list = lastSearch[query].orEmpty()
            val from = (page * pageSize).coerceAtMost(list.size)
            LibraryResult.ofItemList(ImmutableList.copyOf(list.subList(from, (from + pageSize).coerceAtMost(list.size)).map { TrackCache.item(it) }), params)
        }

        /** The car and the phone send media ids; here they become playable items (a wave is built on the spot). */
        override fun onAddMediaItems(session: MediaSession, controller: MediaSession.ControllerInfo, mediaItems: MutableList<MediaItem>): ListenableFuture<MutableList<MediaItem>> =
            scope.future {
                val out = mutableListOf<MediaItem>()
                for (mi in mediaItems) {
                    val id = mi.mediaId
                    when {
                        id == "wave" || id.startsWith("wave:") -> {
                            Wave.mood.value = if (id.startsWith("wave:")) id.removePrefix("wave:") else null
                            carWave = true
                            out += runCatching { Wave.next(8, emptyList(), car = true) }.getOrDefault(emptyList()).map { TrackCache.item(it) }
                        }
                        TrackCache.get(id) != null -> {
                            if (!isPhone(controller)) carWave = false
                            out += TrackCache.item(TrackCache.get(id)!!)
                        }
                    }
                }
                if (isPhone(controller)) carWave = false
                out
            }
    }

    private fun isPhone(c: MediaSession.ControllerInfo) = c.packageName == packageName

    companion object {
        val EMPTY: Bundle = Bundle.EMPTY
    }
}
