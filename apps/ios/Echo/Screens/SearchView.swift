import SwiftUI

/// ИИшка: prompt → /api/ai/playlist → gather candidates → rank (web aiForm handler).
enum AIPlaylist {
    struct Result {
        let title: String
        let desc: String
        let tracks: [Track]
        let note: String
    }

    @MainActor
    static func build(_ prompt: String) async throws -> Result {
        let d = try await API.shared.send("/api/ai/playlist", json: ["prompt": prompt])
        let plan = AIPlan(json: d["plan"])
        var jobs: [TrackJob] = []
        for q in plan.queries { jobs.append({ try await Sources.auSearch(q, limit: 25) }) }
        for g in plan.genres { jobs.append({ try await Sources.auTrending(genre: g, limit: 30) }) }
        if Sources.jamendoOn {
            for q in plan.queries.prefix(2) { jobs.append({ try await Sources.jaSearch(q, limit: 20) }) }
            if !plan.genres.isEmpty {
                let tags = plan.genres.map { Sources.jTag($0) }.joined(separator: "+")
                let instr = plan.instrumental
                jobs.append({ try await Sources.jaPopular(tags: tags, instrumental: instr) })
            }
        }
        let genres = plan.genres
        let moods = plan.moods
        jobs.append({
            let l = await Sources.localTracks()
            return l.filter { genres.contains($0.genre) || moods.contains($0.mood) }
        })
        let results = await Sources.runAll(jobs)
        var cands: [Track] = []
        for r in results {
            if case .success(let l) = r { cands += l }
        }
        var ctx = ScoreContext()
        ctx.plan = plan
        ctx.explore = 1.2
        let list = Recs.shared.pick(Sources.dedupe(cands), plan.size, ctx, perArtist: 2)
        let mins = Int((list.reduce(0) { $0 + $1.duration } / 60).rounded())
        let note = "\(list.count) треков · \(mins) мин · " + (plan.source == "rules" ? "подбор по правилам" : "подобрал ИИ")
        return Result(title: plan.title.isEmpty ? prompt : plan.title, desc: plan.description, tracks: list, note: note)
    }
}

struct AICard: View {
    @EnvironmentObject var app: AppModel
    @EnvironmentObject var recs: Recs
    @EnvironmentObject var player: Player
    @State private var prompt = ""
    @State private var busy = false
    @State private var result: AIPlaylist.Result? = nil
    @State private var errorText: String? = nil
    @State private var needLogin = false
    @State private var title = ""

    static let examples = ["Под учёбу, без слов", "На пробежку", "Ночная дорога", "Перед сном", "Вечеринка дома", "Когда грустно"]

    var body: some View {
        let th = app.theme
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 10) {
                HStack(spacing: 6) {
                    Image(systemName: "sparkles")
                    Text("ИИшка")
                }
                .font(.system(size: 12, weight: .semibold))
                .foregroundStyle(th.bg)
                .padding(.vertical, 5)
                .padding(.horizontal, 10)
                .background(Capsule().fill(th.fg))
                Text("Соберу плейлист под задачу")
                    .font(th.heading(20))
                    .foregroundStyle(th.fg)
                    .lineLimit(2)
            }
            HStack(spacing: 10) {
                EchoField(placeholder: "Например: под учёбу, без слов, часа на два", text: $prompt)
                    .submitLabel(.go)
                    .onSubmit { run() }
                PillButton(title: busy ? "Собираю…" : "Собрать", solid: true, busy: busy) { run() }
                    .disabled(busy)
            }
            FlowLayout(spacing: 8) {
                ForEach(AICard.examples, id: \.self) { x in
                    Chip(text: x) {
                        prompt = x
                        run()
                    }
                }
            }
        }
        .padding(18)
        .echoCard(radius: th.cardRadius)

        if busy || result != nil || errorText != nil {
            VStack(alignment: .leading, spacing: 12) {
                Text(busy ? "Думаю над подборкой…" : (needLogin ? "Нужно войти" : (errorText != nil ? "Не получилось собрать" : title)))
                    .font(th.heading(24))
                    .foregroundStyle(th.fg)
                if let r = result, !busy {
                    Text(r.note).font(.caption).foregroundStyle(th.muted)
                    HStack(spacing: 10) {
                        PillButton(title: "Слушать", icon: "play.fill", solid: true) { player.play(r.tracks, start: 0, mode: .list) }
                        PillButton(title: "Сохранить", icon: "plus") {
                            guard !r.tracks.isEmpty else { return }
                            recs.savePlaylist(title: r.title, desc: r.desc, tracks: r.tracks)
                            app.toast("Плейлист «\(r.title)» сохранён в коллекцию")
                        }
                    }
                }
                if needLogin {
                    HStack {
                        Text("ИИшка работает для вошедших.").font(.subheadline).foregroundStyle(th.fg)
                        Button("Войти по почте") { app.showLogin = true }
                            .font(.subheadline.weight(.semibold))
                            .foregroundStyle(th.fg)
                            .underline()
                    }
                } else if let e = errorText {
                    StatusText(text: e + ". Попробуйте ещё раз.", error: true)
                }
                if busy {
                    TrackList(tracks: [], loading: true)
                } else if let r = result {
                    TrackList(tracks: r.tracks, empty: "Не нашлось подходящих треков. Переформулируйте запрос.")
                }
            }
        }
    }

    private func run() {
        let p = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !p.isEmpty, !busy else { return }
        busy = true
        errorText = nil
        needLogin = false
        Task { @MainActor in
            do {
                let r = try await AIPlaylist.build(p)
                result = r
                title = r.title
            } catch {
                result = nil
                if let e = error as? APIError, e.status == 401 {
                    needLogin = true
                    errorText = nil
                } else {
                    errorText = error.localizedDescription
                }
            }
            busy = false
        }
    }
}

/// Поиск: Audius + Jamendo + uploads in parallel, debounce 350 ms, filters, plus ИИшка.
struct SearchView: View {
    @EnvironmentObject var app: AppModel
    @State private var query = ""
    @State private var src = "all"
    @State private var onlyDl = false
    @State private var results: [Track] = []
    @State private var failed = false
    @State private var loading = false
    @State private var searched = ""

    var body: some View {
        let th = app.theme
        ScrollView {
            VStack(alignment: .leading, spacing: 22) {
                Text("Поиск")
                    .font(th.display(40))
                    .textCase(th.uppercase ? .uppercase : nil)
                    .foregroundStyle(th.fg)
                    .padding(.top, 8)

                EchoField(placeholder: "Трек, исполнитель или жанр", text: $query, icon: "magnifyingglass")
                    .submitLabel(.search)

                FlowLayout(spacing: 8) {
                    Chip(text: "Только со скачиванием", icon: "arrow.down.circle", on: onlyDl) { onlyDl.toggle() }
                    Chip(text: "Все источники", on: src == "all") { src = "all" }
                    Chip(text: "Audius", on: src == "audius") { src = "audius" }
                    Chip(text: "Jamendo", on: src == "jamendo") {
                        if Sources.jamendoOn {
                            src = "jamendo"
                        } else {
                            app.toast("Jamendo не подключён: добавьте JAMENDO_CLIENT_ID на сервере")
                        }
                    }
                    Chip(text: "Ваша музыка", on: src == "local") { src = "local" }
                }

                VStack(alignment: .leading, spacing: 12) {
                    SectionHeader(title: searched.isEmpty ? "Результаты" : "«\(searched)»",
                                  note: (!searched.isEmpty && !loading && !failed) ? "найдено: \(results.count)" : nil)
                    if query.trimmingCharacters(in: .whitespaces).isEmpty {
                        StatusText(text: "Введите название трека, имя артиста или жанр")
                    } else if failed {
                        StatusText(text: "Поиск не ответил. Проверьте интернет.", error: true)
                    } else {
                        TrackList(tracks: results,
                                  empty: onlyDl ? "Нет треков со скачиванием по этому запросу" : "По запросу «\(searched)» ничего нет. Попробуйте на английском или другой жанр.",
                                  loading: loading)
                    }
                }

                AICard()
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 24)
        }
        .scrollDismissesKeyboard(.interactively)
        .screenBackground()
        .toolbar(.hidden, for: .navigationBar)
        .task(id: "\(query)|\(src)|\(onlyDl)") {
            let q = query.trimmingCharacters(in: .whitespacesAndNewlines)
            if q.isEmpty {
                results = []
                searched = ""
                failed = false
                loading = false
                return
            }
            try? await Task.sleep(nanoseconds: 350_000_000)
            if Task.isCancelled { return }
            loading = true
            failed = false
            results = []
            searched = q
            let r = await Sources.searchAll(q, src: src, onlyDl: onlyDl)
            if Task.isCancelled { return }
            results = r.list
            failed = r.failed
            loading = false
        }
    }
}
