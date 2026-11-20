# The YouTube player talks to the app through a JavaScript bridge: keep its methods.
-keepclassmembers class space.maxsim.echo.ui.YtBridge {
    @android.webkit.JavascriptInterface <methods>;
}
-dontwarn org.slf4j.**
