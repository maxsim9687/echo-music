import CarPlay
import UIKit

/// CarPlay: «Моя волна» with moods, liked tracks, playlists and downloads, plus the system Now Playing screen.
/// Playback goes through the same `Player`, so lock-screen controls, the wave and the profile stay in sync.
/// CarPlay needs the `com.apple.developer.carplay-audio` entitlement: it is in the TrollStore build of the app.
@MainActor
final class CarPlaySceneDelegate: UIResponder, CPTemplateApplicationSceneDelegate {
    private var ic: CPInterfaceController?

    func templateApplicationScene(_ scene: CPTemplateApplicationScene, didConnect interfaceController: CPInterfaceController) {
        ic = interfaceController
        let tabs = CPTabBarTemplate(templates: [waveTab(), likesTab(), playlistsTab(), downloadsTab()])
        interfaceController.setRootTemplate(tabs, animated: false, completion: nil)
    }

    func templateApplicationScene(_ scene: CPTemplateApplicationScene, didDisconnectInterfaceController interfaceController: CPInterfaceController) {
        ic = nil
    }

    // MARK: tabs

    private func waveTab() -> CPListTemplate {
        let wave = CPListItem(text: "Моя волна", detailText: "Подобрано под вас", image: UIImage(systemName: "waveform"))
        wave.handler = { [weak self] _, done in
            Task { @MainActor in
                if Wave.shared.mood != nil { await Wave.shared.selectMood(Wave.shared.mood ?? "") } // back to the plain wave
                else { await Wave.shared.start() }
                self?.showNowPlaying()
                done()
            }
        }
        let moods = MoodPreset.all.keys.sorted().map { name -> CPListItem in
            let it = CPListItem(text: name, detailText: "Волна под настроение", image: UIImage(systemName: "sparkles"))
            it.handler = { [weak self] _, done in
                Task { @MainActor in
                    if Wave.shared.mood != name { await Wave.shared.selectMood(name) } else { await Wave.shared.start() }
                    self?.showNowPlaying()
                    done()
                }
            }
            return it
        }
        let t = CPListTemplate(title: "Волна", sections: [CPListSection(items: [wave]), CPListSection(items: moods, header: "Настроение", sectionIndexTitle: nil)])
        t.tabImage = UIImage(systemName: "waveform")
        return t
    }

    private func likesTab() -> CPListTemplate {
        let t = CPListTemplate(title: "Любимое", sections: [CPListSection(items: trackItems(playableLikes()))])
        t.tabImage = UIImage(systemName: "heart.fill")
        t.emptyViewSubtitleVariants = ["Ставьте лайки трекам в приложении"]
        return t
    }

    private func playlistsTab() -> CPListTemplate {
        let items = Recs.shared.P.playlists.map { pl -> CPListItem in
            let it = CPListItem(text: pl.title, detailText: "\(pl.tracks.count) треков", image: UIImage(systemName: "music.note.list"))
            it.accessoryType = .disclosureIndicator
            it.handler = { [weak self] _, done in
                guard let self else { return done() }
                let list = CPListTemplate(title: pl.title, sections: [CPListSection(items: self.trackItems(pl.tracks.filter(Self.playable)))])
                self.ic?.pushTemplate(list, animated: true, completion: nil)
                done()
            }
            return it
        }
        let t = CPListTemplate(title: "Плейлисты", sections: [CPListSection(items: items)])
        t.tabImage = UIImage(systemName: "music.note.list")
        t.emptyViewSubtitleVariants = ["Создайте плейлист в приложении или на сайте"]
        return t
    }

    private func downloadsTab() -> CPListTemplate {
        let t = CPListTemplate(title: "Скачанное", sections: [CPListSection(items: trackItems(Downloads.shared.tracks))])
        t.tabImage = UIImage(systemName: "arrow.down.circle")
        t.emptyViewSubtitleVariants = ["Скачанные треки играют без интернета"]
        return t
    }

    // MARK: helpers

    /// The app plays Audius, Jamendo and the site's uploads; YouTube tracks from the web profile are skipped here.
    nonisolated private static func playable(_ t: Track) -> Bool { ["audius", "jamendo", "local"].contains(t.src) }

    private func playableLikes() -> [Track] {
        Recs.shared.P.likes.values.filter(Self.playable).sorted { $0.title.localizedCompare($1.title) == .orderedAscending }
    }

    private func trackItems(_ tracks: [Track]) -> [CPListItem] {
        let list = Array(tracks.prefix(CPListTemplate.maximumItemCount))
        return list.enumerated().map { i, t in
            let it = CPListItem(text: t.title, detailText: t.artist, image: UIImage(systemName: "music.note"))
            it.handler = { [weak self] _, done in
                Task { @MainActor in
                    Player.shared.play(list, start: i, mode: .list)
                    self?.showNowPlaying()
                    done()
                }
            }
            if i < 40, let url = API.url(t.coverSm.isEmpty ? t.cover : t.coverSm) { loadImage(url, into: it) }
            return it
        }
    }

    private func loadImage(_ url: URL, into item: CPListItem) {
        Task {
            guard let (data, _) = try? await URLSession.shared.data(from: url), let img = UIImage(data: data) else { return }
            await MainActor.run { item.setImage(img) }
        }
    }

    private func showNowPlaying() {
        guard let ic, !(ic.topTemplate is CPNowPlayingTemplate) else { return }
        ic.pushTemplate(CPNowPlayingTemplate.shared, animated: true, completion: nil)
    }
}
