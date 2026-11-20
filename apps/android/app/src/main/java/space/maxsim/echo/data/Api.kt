package space.maxsim.echo.data

import android.content.Context
import android.content.SharedPreferences
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.jsonPrimitive
import okhttp3.Cookie
import okhttp3.CookieJar
import okhttp3.HttpUrl
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import java.net.URLEncoder
import java.security.KeyStore
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManagerFactory
import javax.net.ssl.X509TrustManager
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit

class ApiError(message: String, val code: Int) : Exception(message)

/** Client for music.maxsim.space: the same API the site uses. Login is the site's `sid` cookie, kept on disk. */
object Api {
    const val BASE = "https://music.maxsim.space"
    val json = Json { ignoreUnknownKeys = true; isLenient = true; explicitNulls = false }
    lateinit var http: OkHttpClient
        private set
    private val cache = ConcurrentHashMap<String, Pair<Long, JsonElement>>()

    fun init(ctx: Context) {
        val tm = trustWithLetsEncrypt(ctx)
        val ssl = SSLContext.getInstance("TLS").apply { init(null, arrayOf(tm), null) }
        http = OkHttpClient.Builder()
            .sslSocketFactory(ssl.socketFactory, tm)
            .cookieJar(PrefsCookieJar(ctx.getSharedPreferences("echo.cookies", Context.MODE_PRIVATE)))
            .connectTimeout(15, TimeUnit.SECONDS)
            .readTimeout(40, TimeUnit.SECONDS)
            .build()
    }

    /** Android 5–7.0 do not know the Let's Encrypt root, so the site's certificate would be rejected there.
     *  Trust the system roots plus ISRG Root X1 shipped in the app (public certificate, checked by fingerprint). */
    private fun trustWithLetsEncrypt(ctx: Context): X509TrustManager {
        val ks = KeyStore.getInstance(KeyStore.getDefaultType()).apply { load(null, null) }
        val sys = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm()).apply { init(null as KeyStore?) }
        var n = 0
        sys.trustManagers.filterIsInstance<X509TrustManager>().forEach { m -> m.acceptedIssuers.forEach { ks.setCertificateEntry("sys${n++}", it) } }
        ctx.resources.openRawResource(space.maxsim.echo.R.raw.isrg_root_x1).use {
            ks.setCertificateEntry("isrg_x1", CertificateFactory.getInstance("X.509").generateCertificate(it) as X509Certificate)
        }
        val tmf = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm()).apply { init(ks) }
        return tmf.trustManagers.filterIsInstance<X509TrustManager>().first()
    }

    fun url(path: String): String = when {
        path.isEmpty() -> ""
        path.startsWith("http://") || path.startsWith("https://") -> path
        else -> BASE + path
    }

    fun enc(s: String): String = URLEncoder.encode(s, "UTF-8")

    fun path(p: String, vararg q: Pair<String, Any?>): String {
        val items = q.filter { it.second != null && it.second.toString().isNotEmpty() }
        if (items.isEmpty()) return p
        return p + "?" + items.joinToString("&") { "${enc(it.first)}=${enc(it.second.toString())}" }
    }

    suspend fun get(path: String, ttlMs: Long = 0): JsonElement {
        if (ttlMs > 0) cache[path]?.let { (at, v) -> if (System.currentTimeMillis() - at < ttlMs) return v }
        val v = call(Request.Builder().url(url(path)).get())
        if (ttlMs > 0) {
            if (cache.size > 400) cache.clear()
            cache[path] = System.currentTimeMillis() to v
        }
        return v
    }

    suspend fun send(path: String, body: JsonElement = JsonObject(emptyMap()), method: String = "POST"): JsonElement {
        val rb = body.toString().toRequestBody("application/json; charset=utf-8".toMediaType())
        return call(Request.Builder().url(url(path)).method(method, rb))
    }

    private suspend fun call(b: Request.Builder): JsonElement = withContext(Dispatchers.IO) {
        val req = b.header("X-Echo", "1").header("User-Agent", "EchoAndroid/1.0").header("Accept", "application/json").build()
        http.newCall(req).execute().use { r ->
            val text = r.body?.string().orEmpty()
            val parsed = runCatching { json.parseToJsonElement(text) }.getOrNull() ?: JsonObject(emptyMap())
            if (!r.isSuccessful) {
                val msg = (parsed as? JsonObject)?.str("error")?.takeIf { it.isNotEmpty() } ?: "Сервер ответил ${r.code}"
                throw ApiError(msg, r.code)
            }
            parsed
        }
    }

    fun hasSession(): Boolean = (http.cookieJar as? PrefsCookieJar)?.hasSid() == true
    fun clearSession() { (http.cookieJar as? PrefsCookieJar)?.clear() }
}

/** Keeps only the site's login cookie, on disk, so the app stays signed in. */
private class PrefsCookieJar(private val prefs: SharedPreferences) : CookieJar {
    override fun saveFromResponse(url: HttpUrl, cookies: List<Cookie>) {
        for (c in cookies) if (c.name == "sid") {
            if (c.expiresAt < System.currentTimeMillis() || c.value.isEmpty()) clear()
            else prefs.edit().putString("sid", c.value).putLong("exp", c.expiresAt).apply()
        }
    }

    override fun loadForRequest(url: HttpUrl): List<Cookie> {
        val v = prefs.getString("sid", null) ?: return emptyList()
        if (prefs.getLong("exp", 0) < System.currentTimeMillis()) { clear(); return emptyList() }
        if (url.host != "music.maxsim.space") return emptyList()
        return listOf(Cookie.Builder().name("sid").value(v).domain(url.host).path("/").secure().httpOnly().build())
    }

    fun hasSid() = prefs.getString("sid", null) != null && prefs.getLong("exp", 0) > System.currentTimeMillis()
    fun clear() { prefs.edit().clear().apply() }
}

// ---- small JSON helpers ----
val JsonElement?.obj: JsonObject get() = this as? JsonObject ?: JsonObject(emptyMap())
val JsonElement?.arr: List<JsonElement> get() = (this as? JsonArray) ?: emptyList()
fun JsonObject.str(k: String): String = (this[k] as? JsonPrimitive)?.takeIf { it !is JsonNull }?.let { if (it.isString) it.content else it.content } ?: ""
fun JsonObject.num(k: String): Double = (this[k] as? JsonPrimitive)?.doubleOrNull ?: 0.0
fun JsonObject.bool(k: String): Boolean = (this[k] as? JsonPrimitive)?.let { it.booleanOrNull ?: (it.content == "1") } ?: false
fun JsonElement.asStr(): String = runCatching { jsonPrimitive.content }.getOrDefault("")
