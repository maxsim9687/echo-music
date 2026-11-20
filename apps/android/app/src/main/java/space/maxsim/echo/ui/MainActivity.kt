package space.maxsim.echo.ui

import android.Manifest
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBars
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.BarChart
import androidx.compose.material.icons.filled.Favorite
import androidx.compose.material.icons.filled.FavoriteBorder
import androidx.compose.material.icons.filled.GraphicEq
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.LibraryMusic
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.Person
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.SkipNext
import androidx.compose.material.icons.filled.SkipPrevious
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import space.maxsim.echo.data.Profile
import space.maxsim.echo.data.Updater
import space.maxsim.echo.data.fmtTime
import space.maxsim.echo.player.Hub

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        Hub.connect(this)
        if (Build.VERSION.SDK_INT >= 33) requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 1)
        setContent { EchoTheme { Root() } }
    }
}

private enum class Tab(val title: String, val icon: ImageVector) {
    Home("Волна", Icons.Default.GraphicEq), Search("Поиск", Icons.Default.Search), Charts("Чарты", Icons.Default.BarChart),
    Library("Коллекция", Icons.Default.LibraryMusic), Me("Профиль", Icons.Default.Person),
}

@Composable
private fun Root() {
    var tab by remember { mutableStateOf(Tab.Home) }
    var full by remember { mutableStateOf(false) }
    var toast by remember { mutableStateOf<String?>(null) }
    val playing by Hub.playing.collectAsState()
    val yt by Hub.ytVideo.collectAsState()
    val idx by Hub.index.collectAsState()
    LaunchedEffect(Unit) { Profile.refreshMe() }
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val update by Updater.ready.collectAsState()
    var updateLater by remember { mutableStateOf(false) }
    // new versions are fetched on their own: at start and every 6 hours while the app is open
    LaunchedEffect(Unit) { while (true) { Updater.check(ctx.applicationContext); kotlinx.coroutines.delay(6 * 3600_000L) } }
    LaunchedEffect(Unit) { Hub.toast.collect { toast = it; kotlinx.coroutines.delay(3500); toast = null } }
    BackHandler(full) { full = false }

    Box(Modifier.fillMaxSize().background(C.bg)) {
        Spots(playing)
        Column(Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.statusBars)) {
            Box(Modifier.weight(1f)) {
                when (tab) {
                    Tab.Home -> HomeScreen()
                    Tab.Search -> SearchScreen()
                    Tab.Charts -> ChartsScreen()
                    Tab.Library -> LibraryScreen()
                    Tab.Me -> ProfileScreen()
                }
            }
        }
        // bottom: mini player + tabs
        Column(Modifier.align(Alignment.BottomCenter).fillMaxWidth().windowInsetsPadding(WindowInsets.navigationBars).padding(horizontal = 12.dp, vertical = 8.dp)) {
            if (idx >= 0) MiniPlayer { full = true }
            Row(
                Modifier.padding(top = 8.dp).fillMaxWidth().glass(RoundedCornerShape(26.dp)).padding(vertical = 6.dp),
                horizontalArrangement = Arrangement.SpaceEvenly,
            ) {
                for (t in Tab.entries) {
                    val on = t == tab
                    Column(Modifier.clip(RoundedCornerShape(18.dp)).clickable { tab = t; full = false }.padding(horizontal = 10.dp, vertical = 6.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                        Icon(t.icon, t.title, tint = if (on) C.fg else C.muted)
                        Text(t.title, color = if (on) C.fg else C.muted, fontSize = 10.sp, fontWeight = if (on) FontWeight.Bold else FontWeight.Normal)
                    }
                }
            }
        }
        AnimatedVisibility(full, enter = slideInVertically { it }, exit = slideOutVertically { it }) { NowPlaying { full = false } }

        // The YouTube player: always on screen while a YouTube song plays — over the cover in full screen,
        // otherwise as a small card above the bottom bar.
        if (yt != null) {
            val w = LocalConfiguration.current.screenWidthDp
            val mod = if (full) Modifier.align(Alignment.TopCenter).windowInsetsPadding(WindowInsets.statusBars).padding(top = 64.dp, start = 20.dp, end = 20.dp).fillMaxWidth().aspectRatio(16f / 9f)
            else Modifier.align(Alignment.BottomEnd).windowInsetsPadding(WindowInsets.navigationBars).padding(end = 12.dp, bottom = 168.dp).width(minOf(w - 24, 356).dp).aspectRatio(16f / 9f)
            Box(mod.clip(RoundedCornerShape(18.dp)).background(androidx.compose.ui.graphics.Color.Black)) { YouTubePlayer(Modifier.fillMaxSize()) }
        }

        update?.let { u ->
            if (!updateLater && !full) Row(
                Modifier.align(Alignment.TopCenter).windowInsetsPadding(WindowInsets.safeDrawing).padding(12.dp).fillMaxWidth()
                    .glass(RoundedCornerShape(18.dp)).padding(start = 16.dp, end = 6.dp, top = 6.dp, bottom = 6.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column(Modifier.weight(1f)) {
                    Text("Обновление готово", color = C.fg, fontSize = 14.sp, fontWeight = FontWeight.Bold)
                    Text("Версия 1.0.${u.build} уже скачана", color = C.muted, fontSize = 12.sp)
                }
                Text("Позже", color = C.muted, fontSize = 13.sp, modifier = Modifier.clip(RoundedCornerShape(14.dp)).clickable { updateLater = true }.padding(10.dp))
                Text("Установить", color = C.bg, fontSize = 13.sp, fontWeight = FontWeight.Bold,
                    modifier = Modifier.clip(RoundedCornerShape(14.dp)).background(C.fg).clickable { Updater.install(ctx) }.padding(horizontal = 14.dp, vertical = 10.dp))
            }
        }

        toast?.let {
            Box(Modifier.align(Alignment.TopCenter).windowInsetsPadding(WindowInsets.safeDrawing).padding(16.dp).glass(RoundedCornerShape(18.dp)).padding(horizontal = 16.dp, vertical = 12.dp)) {
                Text(it, color = C.fg, fontSize = 14.sp)
            }
        }
    }
}

@Composable
private fun MiniPlayer(onOpen: () -> Unit) {
    val idx by Hub.index.collectAsState()
    val q by Hub.queue.collectAsState()
    val playing by Hub.playing.collectAsState()
    val pos by Hub.position.collectAsState()
    val dur by Hub.duration.collectAsState()
    val t = q.getOrNull(idx) ?: return
    Column(Modifier.fillMaxWidth().glass(RoundedCornerShape(22.dp)).clickable(onClick = onOpen)) {
        Row(Modifier.padding(8.dp), verticalAlignment = Alignment.CenterVertically) {
            Cover(t.artSm, 46.dp, 12.dp)
            Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
                Text(t.title, color = C.fg, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(t.artist, color = C.muted, fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            IconButton(onClick = { Hub.toggle() }) { Icon(if (playing) Icons.Default.Pause else Icons.Default.PlayArrow, "Играть", tint = C.fg) }
            IconButton(onClick = { Hub.next() }) { Icon(Icons.Default.SkipNext, "Дальше", tint = C.fg) }
        }
        val p = if (dur > 0) (pos / dur).toFloat().coerceIn(0f, 1f) else 0f
        Box(Modifier.fillMaxWidth().height(2.dp).background(C.line)) { Box(Modifier.fillMaxWidth(p).height(2.dp).background(C.fg)) }
    }
}

@Composable
private fun NowPlaying(onClose: () -> Unit) {
    val idx by Hub.index.collectAsState()
    val q by Hub.queue.collectAsState()
    val playing by Hub.playing.collectAsState()
    val pos by Hub.position.collectAsState()
    val dur by Hub.duration.collectAsState()
    val yt by Hub.ytVideo.collectAsState()
    val ver by Profile.version.collectAsState()
    val t = q.getOrNull(idx)
    var drag by remember { mutableStateOf<Float?>(null) }
    Box(Modifier.fillMaxSize().background(C.bg)) {
        Spots(playing)
        Column(Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.statusBars).windowInsetsPadding(WindowInsets.navigationBars).padding(20.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                IconButton(onClick = onClose) { Icon(Icons.Default.KeyboardArrowDown, "Свернуть", tint = C.fg) }
                Text(if (t?.isYouTube == true) "Источник: YouTube" else "Сейчас играет", color = C.muted, style = androidx.compose.material3.MaterialTheme.typography.labelSmall,
                    modifier = Modifier.weight(1f).padding(start = 8.dp))
            }
            Box(Modifier.padding(top = 4.dp).fillMaxWidth(), contentAlignment = Alignment.Center) {
                // the YouTube video is laid over this space (see Root); otherwise the big cover
                if (yt != null) Box(Modifier.fillMaxWidth().aspectRatio(16f / 9f))
                else Cover(t?.art ?: "", (LocalConfiguration.current.screenWidthDp - 56).dp, 28.dp)
            }
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.Bottom) {
                if (t != null) {
                    Text(t.title, color = C.fg, fontSize = 26.sp, fontWeight = FontWeight.Black, maxLines = 2, overflow = TextOverflow.Ellipsis)
                    Text(t.artist, color = C.muted, fontSize = 16.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    val src = listOfNotNull(t.quality.takeIf { it.isNotEmpty() }, t.extName.takeIf { it.isNotEmpty() && it != "YouTube" }?.let { "из $it" }, t.reason.takeIf { it.isNotEmpty() })
                    if (src.isNotEmpty()) Text(src.joinToString(" · "), color = C.muted, fontSize = 12.sp, modifier = Modifier.padding(top = 4.dp))
                }
                Slider(
                    value = drag ?: (if (dur > 0) (pos / dur).toFloat().coerceIn(0f, 1f) else 0f),
                    onValueChange = { drag = it }, onValueChangeFinished = { drag?.let { Hub.seek(it * dur) }; drag = null },
                    colors = SliderDefaults.colors(thumbColor = C.fg, activeTrackColor = C.fg, inactiveTrackColor = C.line), modifier = Modifier.padding(top = 12.dp),
                )
                Row(Modifier.fillMaxWidth()) {
                    Text(fmtTime(pos), color = C.muted, fontSize = 12.sp, modifier = Modifier.weight(1f))
                    Text(fmtTime(dur), color = C.muted, fontSize = 12.sp)
                }
                Row(Modifier.fillMaxWidth().padding(vertical = 18.dp), horizontalArrangement = Arrangement.SpaceEvenly, verticalAlignment = Alignment.CenterVertically) {
                    val liked = remember(ver, t?.id) { t != null && Profile.isLiked(t) }
                    IconButton(onClick = { t?.let { Profile.toggleLike(it) } }) { Icon(if (liked) Icons.Default.Favorite else Icons.Default.FavoriteBorder, "Нравится", tint = C.fg) }
                    IconButton(onClick = { Hub.prev() }) { Icon(Icons.Default.SkipPrevious, "Назад", tint = C.fg, modifier = Modifier.size(34.dp)) }
                    Box(Modifier.size(78.dp).clip(CircleShape).background(C.fg).clickable { Hub.toggle() }, contentAlignment = Alignment.Center) {
                        Icon(if (playing) Icons.Default.Pause else Icons.Default.PlayArrow, "Играть", tint = C.bg, modifier = Modifier.size(40.dp))
                    }
                    IconButton(onClick = { Hub.next() }) { Icon(Icons.Default.SkipNext, "Дальше", tint = C.fg, modifier = Modifier.size(34.dp)) }
                    Box(Modifier.width(48.dp))
                }
            }
        }
    }
}
