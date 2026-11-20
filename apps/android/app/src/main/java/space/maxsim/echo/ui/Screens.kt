package space.maxsim.echo.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Tune
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import space.maxsim.echo.data.Profile
import space.maxsim.echo.data.Sources
import space.maxsim.echo.data.Track
import space.maxsim.echo.data.Wave
import space.maxsim.echo.data.WaveConfig
import space.maxsim.echo.player.Hub

private val listPad = PaddingValues(bottom = 170.dp)

// ======================= Home =======================
@Composable
fun HomeScreen() {
    var spotify by remember { mutableStateOf<List<Track>?>(null) }
    var apple by remember { mutableStateOf<List<Track>?>(null) }
    val ver by Profile.version.collectAsState()
    LaunchedEffect(Unit) {
        val near = if (space.maxsim.echo.data.Locale.isRu()) "kz" else "global"
        spotify = Sources.spotifyChart(near).take(30)
        apple = Sources.appleTop().take(30)
    }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = listPad) {
        item { WaveCard() }
        val likes = Profile.likes
        if (likes.isNotEmpty()) {
            item { SectionTitle("Любимое") }
            item { Shelf(likes.take(20)) { i -> Hub.play(likes, i) } }
        }
        item { SectionTitle("Чарт Apple Music") }
        item { apple?.let { l -> Shelf(l) { i -> Hub.play(l, i) } } ?: Loading() }
        item { SectionTitle("Чарт Spotify") }
        item { spotify?.let { l -> Shelf(l) { i -> Hub.play(l, i) } } ?: Loading() }
        val hist = Profile.history
        if (hist.isNotEmpty()) {
            item { SectionTitle("Недавно слушали") }
            itemsIndexed(hist.take(8), key = { _, t -> "h" + t.id + ver }) { i, t -> TrackRow(t, i) { Hub.play(hist, i) } }
        }
    }
}

@Composable
private fun Shelf(list: List<Track>, onPlay: (Int) -> Unit) {
    LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        itemsIndexed(list, key = { _, t -> t.id }) { i, t ->
            Column(Modifier.width(140.dp).clickable { onPlay(i) }) {
                Cover(t.art, 140.dp, 16.dp, i)
                Gap(8.dp)
                Text(t.title, color = C.fg, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis, fontSize = 14.sp)
                Text(t.artist, color = C.muted, maxLines = 1, overflow = TextOverflow.Ellipsis, fontSize = 12.sp)
            }
        }
    }
}

@Composable
fun WaveCard() {
    val idx by Hub.index.collectAsState()
    val q by Hub.queue.collectAsState()
    val playing by Hub.playing.collectAsState()
    val mode by Hub.mode.collectAsState()
    val mood by Wave.mood.collectAsState()
    val busy by Hub.busy.collectAsState()
    val yt by Hub.ytVideo.collectAsState()
    var settings by remember { mutableStateOf(false) }
    val t = q.getOrNull(idx)
    val waveOn = mode == "wave" && t != null
    Column(Modifier.padding(16.dp).fillMaxWidth().glass(RoundedCornerShape(32.dp)).padding(20.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text((if (mood != null) "МОЯ ВОЛНА · ${mood!!.uppercase()}" else "ПОДОБРАНО ПОД ВАС"), style = MaterialTheme.typography.labelSmall, color = C.muted, modifier = Modifier.weight(1f))
            Icon(Icons.Default.Tune, "Настроить", tint = if (settings) C.fg else C.muted, modifier = Modifier.clip(CircleShape).clickable { settings = !settings }.padding(6.dp))
        }
        Gap(4.dp)
        Text("Моя волна", style = MaterialTheme.typography.displaySmall, color = C.fg)
        Gap(12.dp)
        Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            for (m in Wave.MOODS) Pill(m.name, mood == m.name) { Hub.startWave(if (mood == m.name) null else m.name) }
        }
        if (settings) { Gap(14.dp); WaveSettings() }
        Gap(18.dp)
        Row(verticalAlignment = Alignment.CenterVertically) {
            Box(
                Modifier.size(76.dp).clip(CircleShape).background(C.fg).clickable { if (waveOn) Hub.toggle() else Hub.startWave(mood) },
                contentAlignment = Alignment.Center,
            ) { Icon(if (waveOn && playing) Icons.Default.Pause else Icons.Default.PlayArrow, "Играть", tint = C.bg, modifier = Modifier.size(38.dp)) }
            Column(Modifier.weight(1f).padding(horizontal = 14.dp)) {
                Text(if (busy) "Подбираю…" else if (waveOn) t!!.title else "Нажмите, чтобы начать", color = C.fg, fontWeight = FontWeight.Bold, fontSize = 18.sp, maxLines = 2, overflow = TextOverflow.Ellipsis)
                Text(if (waveOn) t!!.artist + (t.reason.takeIf { it.isNotEmpty() }?.let { " · $it" } ?: "") else "Подстроится под ваш вкус", color = C.muted, fontSize = 13.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                if (waveOn) Row(Modifier.padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Pill("Не то", false) { Hub.ban() }
                    Pill("Дальше", false) { Hub.next() }
                }
            }
            if (waveOn && yt == null) Cover(t!!.art, 96.dp, 18.dp)
        }
    }
}

@Composable
private fun WaveSettings() {
    val c by Wave.cfg.collectAsState()
    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(22.dp)).background(C.glass).border(1.dp, C.line, RoundedCornerShape(22.dp)).padding(16.dp)) {
        Text("ОТКУДА БРАТЬ МУЗЫКУ", style = MaterialTheme.typography.labelSmall, color = C.muted)
        Gap(10.dp)
        Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Pill("YouTube Music", c.youtube, check = true) { Wave.setCfg(c.copy(youtube = !c.youtube)) }
            Pill("Spotify", c.spotify, check = true) { Wave.setCfg(c.copy(spotify = !c.spotify)) }
            Pill("Apple Music", c.apple, check = true) { Wave.setCfg(c.copy(apple = !c.apple)) }
            Pill("Независимые артисты", c.indie, check = true) { Wave.setCfg(c.copy(indie = !c.indie)) }
        }
        Gap(14.dp)
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Знакомое и новое", color = C.fg, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
            Text(if (c.novelty < 35) "больше любимого" else if (c.novelty > 65) "больше нового" else "поровну", color = C.muted, fontSize = 12.sp)
        }
        Slider(
            value = c.novelty.toFloat(), onValueChange = { Wave.setCfg(c.copy(novelty = it.toInt())) }, valueRange = 0f..100f,
            colors = SliderDefaults.colors(thumbColor = C.fg, activeTrackColor = C.fg, inactiveTrackColor = C.line),
        )
        Text("В машине (Android Auto) играют только треки с прямым звуком: YouTube разрешает свою музыку лишь с видимым плеером.", color = C.muted, fontSize = 12.sp)
    }
}

// ======================= Search =======================
@Composable
fun SearchScreen() {
    var q by remember { mutableStateOf("") }
    var src by remember { mutableStateOf("all") }
    var res by remember { mutableStateOf<List<Track>?>(emptyList()) }
    var err by remember { mutableStateOf<String?>(null) }
    LaunchedEffect(q, src) {
        if (q.trim().length < 2) { res = emptyList(); return@LaunchedEffect }
        delay(600)
        res = null; err = null
        res = runCatching { Sources.search(q.trim(), src) }.onFailure { err = it.message }.getOrDefault(emptyList())
    }
    Column(Modifier.fillMaxSize()) {
        Text("Поиск", style = MaterialTheme.typography.displaySmall, color = C.fg, modifier = Modifier.padding(16.dp))
        OutlinedTextField(
            value = q, onValueChange = { q = it }, singleLine = true, placeholder = { Text("Трек или исполнитель") },
            leadingIcon = { Icon(Icons.Default.Search, null) },
            shape = RoundedCornerShape(20.dp), modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp),
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            colors = OutlinedTextFieldDefaults.colors(focusedBorderColor = C.fg, unfocusedBorderColor = C.line, focusedContainerColor = C.glass, unfocusedContainerColor = C.glass),
        )
        Row(Modifier.horizontalScroll(rememberScrollState()).padding(16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            for ((id, name) in listOf("all" to "Все", "youtube" to "YouTube Music", "apple" to "Apple Music", "indie" to "Независимые")) Pill(name, src == id) { src = id }
        }
        val list = res
        when {
            err != null -> Loading(err!!)
            list == null -> Loading("Ищу…")
            list.isEmpty() && q.length >= 2 -> Loading("Ничего не нашлось")
            else -> LazyColumn(contentPadding = listPad) { itemsIndexed(list, key = { _, t -> t.id }) { i, t -> TrackRow(t, i) { Hub.play(list, i) } } }
        }
    }
}

// ======================= Charts =======================
@Composable
fun ChartsScreen() {
    var tab by remember { mutableStateOf("spotify") }
    var cc by remember { mutableStateOf(if (space.maxsim.echo.data.Locale.isRu()) "kz" else "global") }
    var charts by remember { mutableStateOf<List<Sources.ChartInfo>>(emptyList()) }
    var list by remember { mutableStateOf<List<Track>?>(null) }
    LaunchedEffect(Unit) { charts = Sources.charts() }
    LaunchedEffect(tab, cc) { list = null; list = if (tab == "apple") Sources.appleTop() else Sources.spotifyChart(cc) }
    Column(Modifier.fillMaxSize()) {
        Text("Чарты", style = MaterialTheme.typography.displaySmall, color = C.fg, modifier = Modifier.padding(16.dp))
        Row(Modifier.padding(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Pill("Spotify", tab == "spotify") { tab = "spotify" }
            Pill("Apple Music", tab == "apple") { tab = "apple" }
        }
        if (tab == "spotify") Row(Modifier.horizontalScroll(rememberScrollState()).padding(16.dp, 12.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            for (c in charts) Pill(c.name, cc == c.id) { cc = c.id }
        }
        Gap(4.dp)
        val l = list
        if (l == null) Loading() else LazyColumn(contentPadding = listPad) {
            item {
                Row(Modifier.padding(horizontal = 16.dp, vertical = 6.dp)) { Pill("Слушать чарт", true) { Hub.play(l, 0) } }
            }
            itemsIndexed(l, key = { _, t -> t.id }) { i, t ->
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text("${i + 1}", color = C.muted, fontSize = 13.sp, modifier = Modifier.width(36.dp).padding(start = 16.dp))
                    Box(Modifier.weight(1f)) { TrackRow(t, i) { Hub.play(l, i) } }
                }
            }
        }
    }
}

// ======================= Library =======================
@Composable
fun LibraryScreen() {
    val ver by Profile.version.collectAsState()
    var open by remember { mutableStateOf<String?>(null) }
    val likes = remember(ver) { Profile.likes }
    val pls = remember(ver) { Profile.playlists }
    val pl = pls.firstOrNull { it.id == open }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = listPad) {
        item { Text(if (pl != null) pl.title else "Коллекция", style = MaterialTheme.typography.displaySmall, color = C.fg, modifier = Modifier.padding(16.dp)) }
        if (pl != null) {
            item {
                Row(Modifier.padding(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Pill("Слушать", true) { Hub.play(pl.tracks, 0) }
                    Pill("Назад", false) { open = null }
                    Pill("Удалить", false) { Profile.deletePlaylist(pl.id); open = null }
                }
            }
            itemsIndexed(pl.tracks, key = { _, t -> "p" + t.id }) { i, t -> TrackRow(t, i) { Hub.play(pl.tracks, i) } }
            return@LazyColumn
        }
        if (pls.isNotEmpty()) {
            item { SectionTitle("Плейлисты") }
            item {
                LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    items(pls, key = { it.id }) { p ->
                        Column(Modifier.width(140.dp).clickable { open = p.id }) {
                            Cover(p.tracks.firstOrNull()?.art ?: "", 140.dp, 16.dp, p.id.hashCode())
                            Gap(8.dp)
                            Text(p.title, color = C.fg, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            Text("${p.tracks.size} треков", color = C.muted, fontSize = 12.sp)
                        }
                    }
                }
            }
        }
        item {
            Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                Text("Любимое", style = MaterialTheme.typography.headlineSmall, color = C.fg, modifier = Modifier.weight(1f))
                if (likes.isNotEmpty()) Pill("Слушать", true) { Hub.play(likes, 0) }
            }
        }
        if (likes.isEmpty()) item { Loading("Ставьте лайки трекам, и они появятся здесь") }
        itemsIndexed(likes, key = { _, t -> "l" + t.id }) { i, t -> TrackRow(t, i) { Hub.play(likes, i) } }
    }
}

// ======================= Profile =======================
@Composable
fun ProfileScreen() {
    val me by Profile.me.collectAsState()
    val scope = rememberCoroutineScope()
    var email by remember { mutableStateOf("") }
    var code by remember { mutableStateOf("") }
    var sent by remember { mutableStateOf(false) }
    var msg by remember { mutableStateOf<String?>(null) }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = listPad) {
        item { Text("Профиль", style = MaterialTheme.typography.displaySmall, color = C.fg, modifier = Modifier.padding(16.dp)) }
        item {
            Column(Modifier.padding(16.dp).fillMaxWidth().glass().padding(20.dp)) {
                if (me != null) {
                    Text("Вы вошли", color = C.muted, fontSize = 13.sp)
                    Text(me!!, color = C.fg, fontWeight = FontWeight.Bold, fontSize = 18.sp)
                    Gap(8.dp)
                    Text("Лайки, плейлисты и вкус волны общие с сайтом music.maxsim.space.", color = C.muted, fontSize = 13.sp)
                    Gap(14.dp)
                    Pill("Выйти", false) { scope.launch { Profile.logout() } }
                } else {
                    Text("Вход по почте", color = C.fg, fontWeight = FontWeight.Bold, fontSize = 18.sp)
                    Text("Тот же аккаунт, что на сайте: лайки и плейлисты будут общими.", color = C.muted, fontSize = 13.sp)
                    Gap(12.dp)
                    val fieldColors = OutlinedTextFieldDefaults.colors(focusedBorderColor = C.fg, unfocusedBorderColor = C.line)
                    OutlinedTextField(email, { email = it.trim() }, singleLine = true, label = { Text("Почта") }, modifier = Modifier.fillMaxWidth(),
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email), colors = fieldColors, shape = RoundedCornerShape(16.dp))
                    if (sent) {
                        Gap(8.dp)
                        OutlinedTextField(code, { code = it.filter { c -> c.isDigit() }.take(6) }, singleLine = true, label = { Text("Код из письма") }, modifier = Modifier.fillMaxWidth(),
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword, imeAction = ImeAction.Done),
                            keyboardActions = KeyboardActions(onDone = {}), colors = fieldColors, shape = RoundedCornerShape(16.dp))
                    }
                    Gap(12.dp)
                    Pill(if (sent) "Войти" else "Получить код", true) {
                        scope.launch {
                            msg = null
                            runCatching { if (!sent) { Profile.requestCode(email); sent = true; msg = "Код отправлен на $email" } else Profile.verify(email, code) }
                                .onFailure { msg = it.message }
                        }
                    }
                    msg?.let { Gap(8.dp); Text(it, color = C.muted, fontSize = 13.sp) }
                }
            }
        }
        item {
            Column(Modifier.padding(16.dp).fillMaxWidth().glass().padding(20.dp)) {
                Text("Android Auto", color = C.fg, fontWeight = FontWeight.Bold, fontSize = 18.sp)
                Gap(6.dp)
                Text(
                    "Эхо появится в машине среди музыкальных приложений: Моя волна, настроения, Любимое, плейлисты, недавние и поиск голосом.\n\n" +
                        "Android Auto показывает приложения не из Google Play только с включённым режимом разработчика: откройте настройки Android Auto, " +
                        "десять раз нажмите на «Версия», затем в меню ⋮ «Для разработчиков» включите «Неизвестные источники».\n\n" +
                        "В машине играют треки с прямым звуком (Audius, Jamendo, загрузки): песни с YouTube играют только на телефоне с видимым плеером, таково правило YouTube.",
                    color = C.muted, fontSize = 13.sp,
                )
            }
        }
        item {
            val ctx = androidx.compose.ui.platform.LocalContext.current
            val ready by space.maxsim.echo.data.Updater.ready.collectAsState()
            var state by remember { mutableStateOf<String?>(null) }
            Column(Modifier.padding(16.dp).fillMaxWidth().glass().padding(20.dp)) {
                Text("Обновления", color = C.fg, fontWeight = FontWeight.Bold, fontSize = 18.sp)
                Gap(6.dp)
                Text("Версия 1.0.${space.maxsim.echo.data.Updater.currentBuild(ctx)}. Новые версии скачиваются сами, установка — одним нажатием.", color = C.muted, fontSize = 13.sp)
                Gap(12.dp)
                if (ready != null) Pill("Установить 1.0.${ready!!.build}", true) { space.maxsim.echo.data.Updater.install(ctx) }
                else Pill(state ?: "Проверить обновления", false) {
                    state = "Проверяю…"
                    scope.launch { state = if (space.maxsim.echo.data.Updater.check(ctx.applicationContext) == null) "У вас последняя версия" else null }
                }
            }
        }
    }
}
