import SwiftUI

/// Tabs (Главная, Поиск, Чарты, Коллекция, Настройки) with the mini player above the tab bar.
struct RootView: View {
    @EnvironmentObject var app: AppModel
    @EnvironmentObject var player: Player
    @Environment(\.scenePhase) private var scenePhase

    var body: some View {
        let th = app.theme
        TabView(selection: $app.tab) {
            tab(AnyView(HomeView()), "Главная", "house", .home)
            tab(AnyView(SearchView()), "Поиск", "magnifyingglass", .search)
            tab(AnyView(ChartsView()), "Чарты", "chart.bar", .charts)
            tab(AnyView(LibraryView()), "Коллекция", "square.stack", .library)
            tab(AnyView(SettingsView()), "Настройки", "gearshape", .settings)
        }
        .tint(th.fg)
        .preferredColorScheme(th.light ? .light : .dark)
        .background(TouchTrackerInstaller().frame(width: 0, height: 0))
        .overlay(alignment: .bottom) {
            ToastStack().padding(.bottom, 132)
        }
        .fullScreenCover(isPresented: $app.showFullPlayer) {
            FullPlayerView().echoEnvironment()
        }
        .sheet(isPresented: $app.showLogin) {
            NavigationStack { LoginView(inSheet: true) }.echoEnvironment()
        }
        .sheet(isPresented: $app.showTasteSetup) {
            NavigationStack { TasteSetupView(inSheet: true) }.echoEnvironment()
        }
        .confirmationDialog("Скачать", isPresented: Binding(get: { app.dlTarget != nil && !app.showFullPlayer },
                                                            set: { if !$0 { app.dlTarget = nil } }),
                            titleVisibility: .visible, presenting: app.dlTarget) { t in
            downloadButtons(t)
        } message: { t in
            Text(t.artist + " — " + t.title)
        }
        .task { await app.boot() }
        .onChange(of: scenePhase) { phase in
            if phase == .active { Downloads.shared.reload() }
        }
        .onChange(of: app.dlTarget) { t in
            if let t = t, !t.downloadable {
                app.dlTarget = nil
                app.toast("Автор этого трека не разрешил скачивание")
            }
        }
    }

    private func tab(_ content: AnyView, _ title: String, _ icon: String, _ tag: AppTab) -> some View {
        let th = app.theme
        return NavigationStack {
            content
        }
        .safeAreaInset(edge: .bottom, spacing: 0) {
            MiniPlayer()
        }
        .toolbarBackground(.visible, for: .tabBar)
        .toolbarBackground(th.glass ? AnyShapeStyle(.ultraThinMaterial) : AnyShapeStyle(th.bg), for: .tabBar)
        .tabItem { Label(title, systemImage: icon) }
        .tag(tag)
    }
}
