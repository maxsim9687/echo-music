package space.maxsim.echo.ui

import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlin.math.cos
import kotlin.math.sin

/** «Стекло», the site's default theme: black, white, frosted glass and soft moving light spots. */
object C {
    val bg = Color(0xFF050506)
    val fg = Color(0xFFF5F5F7)
    val muted = Color(0xFF9A9AA2)
    val line = Color(0x24FFFFFF)
    val glass = Color(0x14FFFFFF)
    val glass2 = Color(0x22FFFFFF)
    val accent = Color(0xFFFFFFFF)
    val danger = Color(0xFFFF6B6B)
}

@Composable
fun EchoTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = darkColorScheme(
            primary = C.fg, onPrimary = C.bg, background = C.bg, surface = C.bg, onSurface = C.fg, onBackground = C.fg,
            surfaceVariant = C.glass2, onSurfaceVariant = C.muted, secondaryContainer = C.glass2, onSecondaryContainer = C.fg,
        ),
        typography = Typography(
            displaySmall = TextStyle(fontSize = 40.sp, fontWeight = FontWeight.Black, letterSpacing = (-1.5).sp),
            headlineSmall = TextStyle(fontSize = 24.sp, fontWeight = FontWeight.Bold, letterSpacing = (-0.5).sp),
            titleMedium = TextStyle(fontSize = 16.sp, fontWeight = FontWeight.SemiBold),
            bodyMedium = TextStyle(fontSize = 14.sp),
            labelSmall = TextStyle(fontSize = 11.sp, fontWeight = FontWeight.Bold, letterSpacing = 1.2.sp),
        ),
        content = content,
    )
}

fun Modifier.glass(shape: Shape = RoundedCornerShape(24.dp)) =
    this.clip(shape).background(Brush.verticalGradient(listOf(C.glass2, C.glass))).border(1.dp, C.line, shape)

/** The spots in the background drift slowly and glow brighter while music plays. */
@Composable
fun Spots(playing: Boolean, tint: Color = Color.White) {
    val tr = rememberInfiniteTransition(label = "spots")
    val t by tr.animateFloat(0f, (2 * Math.PI).toFloat(), infiniteRepeatable(tween(40_000, easing = LinearEasing), RepeatMode.Restart), label = "t")
    val pulse by tr.animateFloat(0.85f, 1.15f, infiniteRepeatable(tween(2_600), RepeatMode.Reverse), label = "p")
    Canvas(Modifier.fillMaxSize()) {
        val w = size.width; val h = size.height
        val k = if (playing) pulse else 1f
        val spots = listOf(
            Triple(0.2f, 0.18f, 0.55f), Triple(0.85f, 0.3f, 0.45f), Triple(0.5f, 0.75f, 0.6f), Triple(0.1f, 0.9f, 0.4f),
        )
        spots.forEachIndexed { i, (x, y, r) ->
            val cx = w * x + cos(t + i * 1.7f) * w * 0.08f
            val cy = h * y + sin(t * 0.8f + i) * h * 0.05f
            val rad = w * r * k
            drawCircle(
                Brush.radialGradient(listOf(tint.copy(alpha = if (playing) 0.16f else 0.09f), Color.Transparent), Offset(cx, cy), rad),
                radius = rad, center = Offset(cx, cy),
            )
        }
    }
}

