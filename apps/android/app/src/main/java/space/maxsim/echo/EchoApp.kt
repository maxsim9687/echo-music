package space.maxsim.echo

import android.app.Application
import space.maxsim.echo.data.Api
import space.maxsim.echo.data.Profile
import space.maxsim.echo.data.Wave

class EchoApp : Application() {
    override fun onCreate() {
        super.onCreate()
        Api.init(this)
        Profile.init(this)
        Wave.init(this)
    }
}
