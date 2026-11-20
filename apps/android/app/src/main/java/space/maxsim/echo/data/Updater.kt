package space.maxsim.echo.data

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.core.content.FileProvider
import androidx.core.content.pm.PackageInfoCompat
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import okhttp3.Request
import java.io.File
import java.security.MessageDigest

/** Self-update from music.maxsim.space: finds a newer build, downloads it in the background and verifies its
 *  SHA-256; installing then takes one tap (Android never lets an app replace itself silently). */
object Updater {
    data class Ready(val build: Int, val file: File)

    val ready = MutableStateFlow<Ready?>(null)
    @Volatile private var busy = false

    fun currentBuild(ctx: Context): Int =
        PackageInfoCompat.getLongVersionCode(ctx.packageManager.getPackageInfo(ctx.packageName, 0)).toInt()

    /** Returns the newer build number if one was found (and downloaded), null when up to date. */
    suspend fun check(ctx: Context): Int? = withContext(Dispatchers.IO) {
        if (busy) return@withContext ready.value?.build
        busy = true
        try {
            val info = Api.get("/api/apps").jsonObject["android"]?.jsonObject ?: return@withContext null
            val build = info["tag"]?.jsonPrimitive?.content?.substringAfter("apps-")?.toIntOrNull() ?: return@withContext null
            val sha = info["sha256"]?.jsonPrimitive?.content.orEmpty()
            if (build <= currentBuild(ctx)) return@withContext null
            val dir = File(ctx.cacheDir, "updates").apply { mkdirs() }
            dir.listFiles()?.forEach { if (it.name != "Echo-$build.apk") it.delete() }
            val apk = File(dir, "Echo-$build.apk")
            if (!(apk.isFile && sha256(apk) == sha)) {
                val part = File(dir, "Echo-$build.part")
                Api.http.newCall(Request.Builder().url(Api.BASE + "/dl/android").build()).execute().use { r ->
                    if (!r.isSuccessful) return@withContext null
                    part.outputStream().use { out -> r.body!!.byteStream().copyTo(out, 1 shl 16) }
                }
                if (sha.isNotEmpty() && sha256(part) != sha) { part.delete(); return@withContext null }
                part.renameTo(apk)
            }
            ready.value = Ready(build, apk)
            build
        } catch (e: Exception) {
            null
        } finally {
            busy = false
        }
    }

    /** Opens the system installer (or, once, the permission screen for installing from this app). */
    fun install(ctx: Context) {
        val r = ready.value ?: return
        if (Build.VERSION.SDK_INT >= 26 && !ctx.packageManager.canRequestPackageInstalls()) {
            ctx.startActivity(Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + ctx.packageName))
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
            return
        }
        val uri = if (Build.VERSION.SDK_INT >= 24) FileProvider.getUriForFile(ctx, ctx.packageName + ".updates", r.file) else Uri.fromFile(r.file)
        ctx.startActivity(Intent(Intent.ACTION_VIEW).setDataAndType(uri, "application/vnd.android.package-archive")
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK))
    }

    private fun sha256(f: File): String {
        val md = MessageDigest.getInstance("SHA-256")
        f.inputStream().use { s -> val b = ByteArray(1 shl 16); while (true) { val n = s.read(b); if (n < 0) break; md.update(b, 0, n) } }
        return md.digest().joinToString("") { "%02x".format(it) }
    }
}
