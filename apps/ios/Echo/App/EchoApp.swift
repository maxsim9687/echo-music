import SwiftUI

@main
@MainActor
struct EchoApp: App {
    var body: some Scene {
        WindowGroup {
            RootView()
                .echoEnvironment()
                .onOpenURL { url in
                    // Files app / share sheet opened an audio file with Эхо: show it in «Скачанное».
                    if url.isFileURL { Downloads.shared.reload() }
                }
        }
    }
}
