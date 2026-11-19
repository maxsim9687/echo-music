import Foundation

struct ChartMatch {
    let list: [Track]
    /// "orig" | "ver" | "none"
    let kind: String

    var label: String {
        switch kind {
        case "orig": return "Оригинал"
        case "ver": return "Версия"
        default: return "Нет на Audius"
        }
    }
}

/// Spotify daily charts matched against Audius (web `matchChart`).
@MainActor
final class ChartsModel: ObservableObject {
    static let shared = ChartsModel()

    @Published var charts: [ChartInfo] = []
    @Published var country: String {
        didSet { UserDefaults.standard.set(country, forKey: "echo.chart") }
    }
    @Published var data: ChartData? = nil
    @Published var error: String? = nil
    @Published var loading = false
    @Published var matches: [String: ChartMatch] = [:]
    @Published var checking: Set<Int> = []
    @Published var expanded: Set<Int> = []
    @Published var progress = ""
    @Published var playingAll = false

    private var loadGen = 0

    private init() {
        country = UserDefaults.standard.string(forKey: "echo.chart") ?? "global"
    }

    func key(_ it: ChartItem) -> String { "\(country):\(it.pos)" }

    func match(for it: ChartItem) -> ChartMatch? { matches[key(it)] }

    static func norm(_ s: String) -> String {
        var x = s.lowercased()
        x = x.replacingOccurrences(of: "\\(.*?\\)|\\[.*?\\]", with: " ", options: .regularExpression)
        x = x.replacingOccurrences(of: "feat\\.?.*$", with: " ", options: [.regularExpression, .caseInsensitive])
        x = x.replacingOccurrences(of: "[^a-z0-9а-яёäöüéèáíóúñç ]", with: " ", options: [.regularExpression, .caseInsensitive])
        x = x.replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression)
        return x.trimmingCharacters(in: .whitespaces)
    }

    func loadCharts() async {
        if charts.isEmpty {
            if let d = try? await API.shared.get("/api/charts", ttl: 3600) {
                charts = d["charts"].array.map { ChartInfo(id: $0["id"].str, name: $0["name"].str) }.filter { !$0.id.isEmpty }
            }
        }
    }

    func load(force: Bool = false) async {
        await loadCharts()
        if !force, let d = data, d.country == country { return }
        loadGen += 1
        let g = loadGen
        loading = true
        error = nil
        do {
            let cc = country
            let d = try await API.shared.get("/api/charts/\(API.enc(cc))", ttl: 1800)
            guard g == loadGen else { return }
            let items = d["items"].array.map { ChartItem(json: $0) }
            data = ChartData(country: cc, name: d["name"].str, date: d["date"].str, items: items)
            loading = false
            await checkTop(g)
        } catch {
            guard g == loadGen else { return }
            self.error = error.localizedDescription
            data = nil
            loading = false
        }
    }

    func select(_ cc: String) async {
        guard cc != country else { return }
        country = cc
        data = nil
        expanded = []
        progress = ""
        await load()
    }

    /// Checks the top 20 in the background, 4 at a time.
    private func checkTop(_ g: Int) async {
        guard let items = data?.items.prefix(20) else { return }
        let list = Array(items)
        var i = 0
        while i < list.count {
            if g != loadGen { return }
            let chunk = Array(list[i..<min(i + 4, list.count)])
            let tasks: [Task<Void, Never>] = chunk.map { it in
                Task { @MainActor in
                    _ = await ChartsModel.shared.matchChart(it)
                }
            }
            for t in tasks { await t.value }
            i += 4
        }
    }

    func matchChart(_ it: ChartItem) async -> ChartMatch {
        let k = key(it)
        if let m = matches[k] { return m }
        checking.insert(it.pos)
        defer { checking.remove(it.pos) }
        var list: [Track] = []
        do { list = try await Sources.auSearch("\(it.artist) \(it.title)", limit: 15) } catch { list = [] }
        let nt = ChartsModel.norm(it.title)
        let na = ChartsModel.norm(it.artist)
        struct Scored {
            let t: Track
            let sc: Int
            let orig: Bool
        }
        var scored: [Scored] = []
        for t in list {
            let tt = ChartsModel.norm(t.title)
            let ta = ChartsModel.norm(t.artist)
            var sc = 0
            if !nt.isEmpty && tt.contains(nt) { sc += 4 }
            if ta == na {
                sc += 5
            } else if !na.isEmpty && (tt.contains(na) || ta.contains(na)) {
                sc += 2
            }
            let orig = ta == na && !nt.isEmpty && tt.contains(nt)
            if sc >= 4 { scored.append(Scored(t: t, sc: sc, orig: orig)) }
        }
        scored.sort { a, b in
            if a.orig != b.orig { return a.orig && !b.orig }
            return a.sc > b.sc
        }
        let kind = scored.isEmpty ? "none" : (scored[0].orig ? "orig" : "ver")
        let res = ChartMatch(list: scored.map { $0.t }, kind: kind)
        matches[k] = res
        return res
    }

    func toggleExpanded(_ it: ChartItem) async {
        if expanded.contains(it.pos) {
            expanded.remove(it.pos)
            return
        }
        expanded.insert(it.pos)
        _ = await matchChart(it)
    }

    /// «Слушать то, что нашлось»: matches the top 50 five at a time and starts once 3 are found.
    func playFound() async {
        guard let d = data, !playingAll else { return }
        playingAll = true
        defer { playingAll = false }
        let items = Array(d.items.prefix(50))
        var queue: [Track] = []
        var started = false
        var done = 0
        var i = 0
        let cc = country
        while i < items.count {
            if cc != country { return }
            let chunk = Array(items[i..<min(i + 5, items.count)])
            var got: [Track?] = []
            for it in chunk {
                let r = await matchChart(it)
                got.append(r.list.first)
            }
            done += chunk.count
            for t in got {
                if let t = t, !queue.contains(where: { $0.id == t.id }), !Recs.shared.P.ban.keys.contains(t.id) { queue.append(t) }
            }
            progress = "проверено \(done) из \(items.count), нашлось \(queue.count)"
            let player = Player.shared
            if !started && queue.count >= 3 {
                started = true
                player.play(queue, start: 0, mode: .list)
            } else if started {
                player.append(queue)
            }
            i += 5
        }
        if !started && !queue.isEmpty { Player.shared.play(queue, start: 0, mode: .list) }
        if queue.isEmpty { AppModel.shared.toast("Из этого чарта на Audius ничего не нашлось") }
    }

    /// Home teaser: tap a chart line → play the best Audius match.
    func playItem(_ it: ChartItem) async {
        let r = await matchChart(it)
        if r.list.isEmpty {
            AppModel.shared.toast("На Audius этого трека нет. Оригинал есть только в Spotify.")
        } else {
            Player.shared.play(r.list, start: 0, mode: .list)
        }
    }
}
