package space.maxsim.echo.ui

import android.annotation.SuppressLint
import android.webkit.JavascriptInterface
import android.webkit.WebChromeClient
import android.webkit.WebView
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Favorite
import androidx.compose.material.icons.filled.FavoriteBorder
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import coil.compose.AsyncImage
import space.maxsim.echo.data.Profile
import space.maxsim.echo.data.Track
import space.maxsim.echo.data.fmtTime
import space.maxsim.echo.player.Hub

private val placeholders = listOf(
    listOf(Color(0xFF3A3A40), Color(0xFF121214)), listOf(Color(0xFF2B2B33), Color(0xFF0B0B0D)),
    listOf(Color(0xFF444448), Color(0xFF18181A)), listOf(Color(0xFF26262B), Color(0xFF050506)),
)

@Composable
fun Cover(url: String, size: Dp, radius: Dp = 10.dp, seed: Int = 0, modifier: Modifier = Modifier) {
    val shape = RoundedCornerShape(radius)
    Box(modifier.size(size).clip(shape).background(Brush.linearGradient(placeholders[Math.floorMod(seed, placeholders.size)])).border(1.dp, C.line, shape)) {
        if (url.isNotEmpty()) AsyncImage(model = url, contentDescription = null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
    }
}

@Composable
fun Pill(text: String, on: Boolean, modifier: Modifier = Modifier, check: Boolean = false, onClick: () -> Unit) {
    val shape = RoundedCornerShape(50)
    Row(
        modifier.clip(shape)
            .background(if (on) C.fg else C.glass)
            .border(1.dp, if (on) C.fg else C.line, shape)
            .clickable(onClick = onClick)
            .padding(horizontal = if (check) 12.dp else 16.dp, vertical = 9.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (check) {
            Box(
                Modifier.size(20.dp).clip(CircleShape).background(if (on) C.bg else Color.Transparent).border(2.dp, if (on) C.bg else C.muted, CircleShape),
                contentAlignment = Alignment.Center,
            ) { if (on) Icon(Icons.Default.Check, null, tint = C.fg, modifier = Modifier.size(13.dp)) }
            Spacer(Modifier.width(8.dp))
        }
        Text(text, color = if (on) C.bg else C.fg, fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
    }
}

@Composable
fun SectionTitle(text: String, modifier: Modifier = Modifier) {
    Text(text, style = androidx.compose.material3.MaterialTheme.typography.headlineSmall, color = C.fg, modifier = modifier.padding(horizontal = 16.dp, vertical = 10.dp))
}

@Composable
fun TrackRow(t: Track, n: Int, onPlay: () -> Unit) {
    val ver by Profile.version.collectAsState()
    val idx by Hub.index.collectAsState()
    val q by Hub.queue.collectAsState()
    val isCur = q.getOrNull(idx)?.id == t.id
    var menu by remember { mutableStateOf(false) }
    Row(
        Modifier.fillMaxWidth().clickable(onClick = onPlay).padding(horizontal = 16.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Cover(t.artSm, 48.dp, 10.dp, n)
        Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
            Text(t.title, color = if (isCur) C.fg else C.fg.copy(alpha = 0.95f), fontWeight = if (isCur) FontWeight.Bold else FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(t.artist, color = C.muted, fontSize = 13.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
                if (t.quality.isNotEmpty()) {
                    Spacer(Modifier.width(6.dp))
                    Text(t.quality, color = C.muted, fontSize = 10.sp, modifier = Modifier.border(1.dp, C.line, RoundedCornerShape(6.dp)).padding(horizontal = 5.dp, vertical = 1.dp))
                }
            }
        }
        if (t.duration > 0) Text(fmtTime(t.duration), color = C.muted, fontSize = 12.sp)
        val liked = remember(ver, t.id) { Profile.isLiked(t) }
        IconButton(onClick = { Profile.toggleLike(t) }) {
            Icon(if (liked) Icons.Default.Favorite else Icons.Default.FavoriteBorder, "Нравится", tint = if (liked) C.fg else C.muted)
        }
        Box {
            IconButton(onClick = { menu = true }) { Icon(Icons.Default.Add, "В плейлист", tint = C.muted) }
            DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                DropdownMenuItem(text = { Text("Новый плейлист") }, onClick = { menu = false; Profile.createPlaylist("Плейлист ${Profile.playlists.size + 1}", listOf(t)); Hub.toast.tryEmit("Создан плейлист") })
                for (p in Profile.playlists.take(12)) DropdownMenuItem(text = { Text(p.title) }, onClick = {
                    menu = false
                    Hub.toast.tryEmit(if (Profile.addToPlaylist(p.id, t)) "Добавлено в «${p.title}»" else "Уже есть в «${p.title}»")
                })
            }
        }
    }
}

@Composable
fun Loading(text: String = "Загружаю…") {
    Box(Modifier.fillMaxWidth().height(120.dp), contentAlignment = Alignment.Center) { Text(text, color = C.muted) }
}

/** JavaScript → app events from the YouTube player. */
class YtBridge {
    @JavascriptInterface fun state(s: Int) { android.os.Handler(android.os.Looper.getMainLooper()).post { Hub.onYtState(s) } }
    @JavascriptInterface fun error(code: Int) { android.os.Handler(android.os.Looper.getMainLooper()).post { Hub.onYtError(code) } }
    @JavascriptInterface fun time(cur: Double, dur: Double) { android.os.Handler(android.os.Looper.getMainLooper()).post { Hub.onYtTime(cur, dur) } }
}

private const val YT_HTML = """<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1">
<style>html,body{margin:0;height:100%;background:#000;overflow:hidden}#p{position:fixed;inset:0;width:100%;height:100%}</style></head>
<body><div id="p"></div><script>
var p,ready=false,pending=null;
function onYouTubeIframeAPIReady(){p=new YT.Player('p',{host:'https://www.youtube-nocookie.com',width:'100%',height:'100%',
playerVars:{playsinline:1,rel:0,modestbranding:1,autoplay:1},
events:{onReady:function(){ready=true;if(pending){load(pending);pending=null}},
onStateChange:function(e){Echo.state(e.data)},onError:function(e){Echo.error(e.data)}}});
setInterval(function(){if(ready&&p.getCurrentTime){Echo.time(p.getCurrentTime()||0,p.getDuration()||0)}},500);}
function load(id){if(!ready){pending=id;return}p.loadVideoById(id)}
function play(){if(ready)p.playVideo()} function pause(){if(ready)p.pauseVideo()} function seek(s){if(ready)p.seekTo(s,true)}
</script><script src="https://www.youtube.com/iframe_api"></script></body></html>"""

/**
 * The official YouTube player (youtube-nocookie). It is always visible while a YouTube song plays — that is the rule
 * for using YouTube's music in other apps, so nothing is downloaded and the video is never hidden.
 */
@SuppressLint("SetJavaScriptEnabled")
@Composable
fun YouTubePlayer(modifier: Modifier = Modifier) {
    val web = remember { mutableStateOf<WebView?>(null) }
    AndroidView(
        modifier = modifier,
        factory = { ctx ->
            WebView(ctx).apply {
                settings.javaScriptEnabled = true
                settings.domStorageEnabled = true
                settings.mediaPlaybackRequiresUserGesture = false
                webChromeClient = WebChromeClient()
                setBackgroundColor(android.graphics.Color.BLACK)
                addJavascriptInterface(YtBridge(), "Echo")
                // the site's address as origin: YouTube embeds need a real page origin
                loadDataWithBaseURL("https://music.maxsim.space/", YT_HTML, "text/html", "utf-8", null)
                web.value = this
            }
        },
    )
    LaunchedEffect(Unit) {
        Hub.ytCmd.collect { cmd ->
            val w = web.value ?: return@collect
            val js = when {
                cmd.startsWith("load:") -> "load('${cmd.removePrefix("load:").filter { it.isLetterOrDigit() || it == '-' || it == '_' }}')"
                cmd.startsWith("seek:") -> "seek(${cmd.removePrefix("seek:").toDoubleOrNull() ?: 0.0})"
                cmd == "play" -> "play()"
                else -> "pause()"
            }
            w.evaluateJavascript(js, null)
        }
    }
    // the player may be created after the first load command: replay the current video
    LaunchedEffect(web.value) {
        val id = Hub.ytVideo.value
        if (web.value != null && id != null) Hub.ytCmd.tryEmit("load:$id")
    }
}

@Composable
fun Gap(h: Dp) = Spacer(Modifier.height(h))

@Composable
fun RowGap(w: Dp) = Spacer(Modifier.width(w))

val rowSpacing = Arrangement.spacedBy(8.dp)
