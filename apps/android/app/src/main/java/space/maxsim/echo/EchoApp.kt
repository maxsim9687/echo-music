package space.maxsim.echo

import android.app.Application
import coil.ImageLoader
import coil.ImageLoaderFactory
import space.maxsim.echo.data.Api
import space.maxsim.echo.data.Profile
import space.maxsim.echo.data.Wave

class EchoApp : Application(), ImageLoaderFactory {
    override fun onCreate() {
        super.onCreate()
        Api.init(this)
        Profile.init(this)
        Wave.init(this)
    }

    // covers go through the same client, so old Android versions also trust the site's certificate
    override fun newImageLoader(): ImageLoader = ImageLoader.Builder(this).okHttpClient { Api.http }.crossfade(true).build()
}
