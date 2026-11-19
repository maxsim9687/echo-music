import Foundation

/// «Моя волна»: progressive candidate pool, batches of 8, refill when < 4 left, mood presets, settings.
@MainActor
final class Wave: ObservableObject {
    static let shared = Wave()

    @Published var mood: String? = nil
    /// Genre tile from «Жанры и настроения» (acts like a plan with one genre).
    @Published var focusGenre: String? = nil
    @Published var cfg: WaveConfig {
        didSet {
            if cfg != oldValue {
                saveCfg()
                scheduleRetune()
            }
        }
    }
    @Published private(set) var starting = false
    /// Error shown in the hero when the catalog did not answer.
    @Published var failTitle: String? = nil
    @Published var failSubtitle: String? = nil

    private final class PoolRun {
        let key: String
        var firstDone = false
        var allDone = false
        var pending = 0
        var firstWaiters: [CheckedContinuation<[Track], Never>] = []
        var allWaiters: [CheckedContinuation<[Track], Never>] = []
        init(key: String) { self.key = key }
    }

    private var pool: [Track] = []
    private var poolKey = ""
    private var poolAt: Date? = nil
    private var run: PoolRun? = nil
    private var refillTask: Task<Void, Never>? = nil
    private var gen = 0
    private var retuneTask: Task<Void, Never>? = nil

    private static let cfgKey = "echo.wave"

    private init() {
        if let data = UserDefaults.standard.data(forKey: Wave.cfgKey),
           let c = try? JSONDecoder().decode(WaveConfig.self, from: data) {
            cfg = c
        } else {
            cfg = WaveConfig()
        }
    }

    private func saveCfg() {
        if let data = try? JSONEncoder().encode(cfg) {
            UserDefaults.standard.set(data, forKey: Wave.cfgKey)
        }
    }

    var eyebrow: String {
        if let m = mood { return "Моя волна · \(m)" }
        if let g = focusGenre { return "Моя волна · \(g)" }
        return "Подобрано под вас"
    }

    func context(current: Track?, recent: [Track], slotOffset: Int = 0, explore: Double? = nil) -> ScoreContext {
        var ctx = ScoreContext()
        ctx.mood = mood
        ctx.cfg = cfg
        ctx.explore = explore
        ctx.current = current
        ctx.recent = recent
        ctx.slotOffset = slotOffset
        if let g = focusGenre {
            var plan = AIPlan()
            plan.genres = [g]
            ctx.plan = plan
        }
        return ctx
    }

    // MARK: candidate generation

    private func poolJobs() -> [TrackJob] {
        let recs = Recs.shared
        var jobs: [TrackJob] = []
        let pre: MoodPreset? = mood.flatMap { MoodPreset.all[$0] }

        if let pre = pre, pre.tags != nil {
            // genre waves (Phonk…) live in search results more than in the genre charts
            for q in pre.queries { jobs.append({ try await Sources.auSearch(q, limit: 40) }) }
            for g in pre.genres.prefix(2) { jobs.append({ try await Sources.auTrending(genre: g, limit: 30) }) }
        } else {
            // taste
            for g in recs.topGenres(3) { jobs.append({ try await Sources.auTrending(genre: g, limit: 30) }) }
            for a in recs.topArtists(3) where a.hasPrefix("au:") { jobs.append({ try await Sources.auArtist(a) }) }
            for tag in recs.topTags(2) { jobs.append({ try await Sources.auSearch(tag, limit: 20) }) }
            // similar to liked
            let liked = Array(recs.P.likes.values).shuffled().prefix(3)
            for t in liked {
                if !t.tags.isEmpty {
                    let q = t.tags.prefix(2).joined(separator: " ")
                    jobs.append({ try await Sources.auSearch(q, limit: 20) })
                }
                if !t.genre.isEmpty {
                    let g = t.genre
                    jobs.append({ try await Sources.auTrending(genre: g, limit: 25) })
                }
            }
            // discovery
            jobs.append({ try await Sources.auUnderground(limit: 30) })
            jobs.append({ try await Sources.auTrending(time: "week", limit: 60) })
            jobs.append({ try await Sources.auTrending(time: "month", limit: 40) })
            let never = GENRE_LIST.filter { recs.P.g[$0] == nil }
            if let explore = never.randomElement() {
                jobs.append({ try await Sources.auTrending(genre: explore, limit: 25) })
            }
            // preset queries for the selected mood
            if let pre = pre {
                for g in pre.genres.prefix(3) { jobs.append({ try await Sources.auTrending(genre: g, limit: 30) }) }
                for q in pre.queries { jobs.append({ try await Sources.auSearch(q, limit: 25) }) }
            }
        }
        if let g = focusGenre {
            jobs.append({ try await Sources.auTrending(genre: g, limit: 60) })
            jobs.append({ try await Sources.auTrending(genre: g, time: "month", limit: 40) })
            jobs.append({ try await Sources.auSearch(g, limit: 30) })
        }
        jobs.append({ await Sources.localTracks() })
        if cfg.novelty < 60 {
            let likes = recs.P.likes.values.map { Sources.refresh($0) }
            jobs.append({ likes })
        }
        if Sources.jamendoOn {
            let base: [String]
            if let pre = pre {
                base = pre.tags ?? pre.genres.map { Sources.jTag($0) }
            } else if let g = focusGenre {
                base = [Sources.jTag(g)]
            } else {
                base = recs.topGenres(3).map { Sources.jTag($0) }
            }
            let tags = base.filter { !$0.isEmpty }.prefix(3).joined(separator: "+")
            let instrumental = cfg.vocals == "instrumental"
            jobs.append({ try await Sources.jaPopular(tags: tags.isEmpty ? nil : tags, limit: 40, instrumental: instrumental) })
        }
        return jobs
    }

    // MARK: pool

    private var currentKey: String {
        (mood ?? "") + "|" + (focusGenre ?? "") + "|" + String(Int(cfg.novelty))
    }

    /// `quick`: returns as soon as ≥6 rankable candidates exist; otherwise when every source answered.
    func ensurePool(quick: Bool) async -> [Track] {
        let key = currentKey
        if poolKey == key, let at = poolAt, Date().timeIntervalSince(at) < 600 { return pool }
        var needNew = true
        if let r = run, r.key == key {
            needNew = r.allDone && (poolAt.map { Date().timeIntervalSince($0) >= 600 } ?? true)
        }
        if needNew { startRun(key) }
        guard let r = run else { return pool }
        if r.allDone || (quick && r.firstDone) { return pool }
        if quick {
            return await withCheckedContinuation { (c: CheckedContinuation<[Track], Never>) in
                r.firstWaiters.append(c)
            }
        }
        return await withCheckedContinuation { (c: CheckedContinuation<[Track], Never>) in
            r.allWaiters.append(c)
        }
    }

    private func startRun(_ key: String) {
        if let old = run { finish(old, with: []) }
        pool = []
        poolAt = nil
        poolKey = key
        let r = PoolRun(key: key)
        run = r
        let jobs = poolJobs()
        r.pending = jobs.count
        if jobs.isEmpty {
            poolAt = Date()
            finish(r, with: pool)
            return
        }
        for job in jobs {
            Task { @MainActor in
                var list: [Track] = []
                do { list = try await job() } catch { list = [] }
                let w = Wave.shared
                guard w.run === r else { return }
                w.add(list, to: r)
                r.pending -= 1
                if r.pending <= 0 {
                    w.poolAt = Date()
                    w.finish(r, with: w.pool)
                }
            }
        }
    }

    private func add(_ list: [Track], to r: PoolRun) {
        pool = Sources.dedupe(pool + list)
        if !r.firstDone {
            let ready = Recs.shared.pick(pool, 6, context(current: nil, recent: [], explore: 0))
            if ready.count >= 6 {
                r.firstDone = true
                let waiters = r.firstWaiters
                r.firstWaiters = []
                for c in waiters { c.resume(returning: pool) }
            }
        }
    }

    private func finish(_ r: PoolRun, with list: [Track]) {
        r.firstDone = true
        r.allDone = true
        let waiters = r.firstWaiters + r.allWaiters
        r.firstWaiters = []
        r.allWaiters = []
        for c in waiters { c.resume(returning: list) }
    }

    func resetPool() {
        if let old = run { finish(old, with: []) }
        run = nil
        pool = []
        poolAt = nil
        poolKey = ""
        refillTask = nil
        gen += 1
    }

    // MARK: queue

    /// Adds the next batch of 8 to the player queue.
    func refill() async {
        if let t = refillTask {
            await t.value
            return
        }
        let g = gen
        let task = Task { @MainActor in
            let w = Wave.shared
            let p = await w.ensurePool(quick: true)
            guard g == w.gen else { return }
            let player = Player.shared
            let inQueue = Set(player.queue.map { $0.id })
            let ctx = w.context(current: player.queue.last, recent: player.queue, slotOffset: player.queue.count)
            let add = Recs.shared.pick(p.filter { !inQueue.contains($0.id) }, 8, ctx)
            player.append(add)
        }
        refillTask = task
        await task.value
        if refillTask == task { refillTask = nil }
    }

    func start() async {
        let player = Player.shared
        if player.mode == .wave && player.current != nil {
            player.toggle()
            return
        }
        if starting { return }
        starting = true
        failTitle = nil
        failSubtitle = nil
        let g = gen
        let p = await ensurePool(quick: true)
        starting = false
        if g != gen {
            await start()
            return
        }
        let q = Recs.shared.pick(p, 6, context(current: nil, recent: []))
        if q.isEmpty {
            failTitle = cfg.onlyDl ? "Не нашлось треков со скачиванием" : "Каталог не ответил"
            failSubtitle = cfg.onlyDl ? "Ослабьте настройки волны" : "Нажмите плей ещё раз через пару секунд"
            resetPool()
            return
        }
        player.play(q, start: 0, mode: .wave)
    }

    func retune() async {
        resetPool()
        let player = Player.shared
        if player.mode == .wave && player.current != nil {
            player.truncateAfterCurrent()
            await refill()
            if player.queue.count <= player.index + 1 {
                AppModel.shared.toast("Не удалось подобрать треки под эти настройки")
            }
        }
    }

    private func scheduleRetune() {
        retuneTask?.cancel()
        retuneTask = Task { @MainActor in
            try? await Task.sleep(nanoseconds: 450_000_000)
            if Task.isCancelled { return }
            await Wave.shared.retune()
        }
    }

    func selectMood(_ m: String) async {
        mood = (mood == m) ? nil : m
        focusGenre = nil
        await restartOrRetune()
    }

    func startGenre(_ g: String) async {
        focusGenre = (focusGenre == g) ? nil : g
        mood = nil
        await restartOrRetune()
    }

    private func restartOrRetune() async {
        let player = Player.shared
        if player.mode == .wave && player.current != nil {
            await retune()
        } else {
            resetPool()
            await start()
        }
    }

    func resetSettings() {
        cfg = WaveConfig()
    }
}
