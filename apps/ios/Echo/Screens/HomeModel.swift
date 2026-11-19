import Foundation

/// Data for Главная: «Для вас» mixes, «Новое», «Ваша музыка».
@MainActor
final class HomeModel: ObservableObject {
    static let shared = HomeModel()

    struct Mix: Identifiable {
        let id: Int
        let title: String
        let subtitle: String
        var tracks: [Track]? = nil
        var failed = false
        let load: @MainActor () async throws -> [Track]
    }

    @Published var mixes: [Mix] = []
    @Published var fresh: [Track] = []
    @Published var freshFailed = false
    @Published var freshLoading = false
    @Published var uploads: [Track] = []

    private var loadedAt: Date? = nil
    private var gen = 0
    private var mixKey = ""

    private init() {}

    func load(force: Bool = false) async {
        if !force, let at = loadedAt, Date().timeIntervalSince(at) < 600 {
            refreshMixesIfTasteChanged()
            return
        }
        loadedAt = Date()
        gen += 1
        let g = gen
        buildMixes(g)
        freshLoading = true
        freshFailed = false
        Task { @MainActor in
            let up = await Sources.localTracks()
            if HomeModel.shared.gen == g { HomeModel.shared.uploads = up }
        }
        do {
            let list = try await Sources.auTrending(time: "week", limit: 40)
            guard g == gen else { return }
            fresh = list.sorted { ($0.releaseTime ?? .distantPast) > ($1.releaseTime ?? .distantPast) }
        } catch {
            if g == gen { freshFailed = true }
        }
        if g == gen { freshLoading = false }
    }

    /// Mixes depend on the taste: rebuild when the top genres / artist changed.
    func refreshMixesIfTasteChanged() {
        if currentMixKey() != mixKey {
            gen += 1
            buildMixes(gen)
        }
    }

    private func currentMixKey() -> String {
        let r = Recs.shared
        return r.topGenres(2).joined(separator: ",") + "|" + (r.topArtists(1).first ?? "")
    }

    private func buildMixes(_ g: Int) {
        mixKey = currentMixKey()
        let defs = mixDefs()
        mixes = defs
        for def in defs {
            let id = def.id
            let loader = def.load
            Task { @MainActor in
                let model = HomeModel.shared
                do {
                    let raw = try await loader()
                    guard model.gen == g, let i = model.mixes.firstIndex(where: { $0.id == id }) else { return }
                    let per = model.mixes[i].title.hasPrefix("Похоже") ? 4 : 3
                    let ranked = Recs.shared.pick(raw, min(30, raw.count), ScoreContext(explore: 1.0), perArtist: per)
                    model.mixes[i].tracks = ranked.isEmpty ? raw : ranked
                } catch {
                    guard model.gen == g, let i = model.mixes.firstIndex(where: { $0.id == id }) else { return }
                    model.mixes[i].failed = true
                }
            }
        }
    }

    /// Same six as web `mixDefs`.
    private func mixDefs() -> [Mix] {
        let recs = Recs.shared
        var d: [Mix] = []
        let tg = recs.topGenres(2)
        if let g1 = tg.first {
            d.append(Mix(id: d.count, title: "В духе \(g1)", subtitle: "популярное за месяц", load: {
                try await Sources.auTrending(genre: g1, time: "month", limit: 30)
            }))
        }
        if let a = recs.topArtists(1).first, a.hasPrefix("au:") {
            let name = recs.P.an[a] ?? "любимого артиста"
            d.append(Mix(id: d.count, title: "Похоже на \(name)", subtitle: "артист и его жанр", load: {
                let own = try await Sources.auArtist(a, limit: 10)
                let genre = own.first?.genre ?? ""
                var more: [Track] = []
                if !genre.isEmpty { more = try await Sources.auTrending(genre: genre, limit: 25) }
                return Sources.dedupe(own + more)
            }))
        }
        if tg.count > 1 {
            let g2 = tg[1]
            d.append(Mix(id: d.count, title: "Немного \(g2)", subtitle: "за неделю", load: {
                try await Sources.auTrending(genre: g2, limit: 30)
            }))
        }
        d.append(Mix(id: d.count, title: "Андеграунд", subtitle: "новые имена, которые набирают", load: {
            try await Sources.auUnderground(limit: 30)
        }))
        d.append(Mix(id: d.count, title: "Под учёбу", subtitle: "без слов, спокойно", load: {
            let lofi = try await Sources.auTrending(genre: "Lo-Fi", limit: 30)
            let study = (try? await Sources.auSearch("study", limit: 20)) ?? []
            var ctx = ScoreContext(explore: 1.0)
            ctx.mood = "Фокус"
            return Recs.shared.pick(Sources.dedupe(lofi + study), 25, ctx)
        }))
        d.append(Mix(id: d.count, title: "Хиты месяца", subtitle: "что слушают все", load: {
            try await Sources.auTrending(time: "month", limit: 30)
        }))
        return Array(d.prefix(6))
    }

    func playMix(_ mix: Mix) async {
        if let list = mix.tracks, !list.isEmpty {
            Player.shared.play(list, start: 0, mode: .list)
            return
        }
        do {
            let list = try await mix.load()
            Player.shared.play(list, start: 0, mode: .list)
        } catch {
            AppModel.shared.toast("Подборка не загрузилась")
        }
    }
}
