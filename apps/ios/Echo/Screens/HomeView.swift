import SwiftUI

/// Главная: wave hero, greeting + quick tiles, shelves, charts teaser, genres grid (Spotify-like).
struct HomeView: View {
    @EnvironmentObject var app: AppModel
    @EnvironmentObject var player: Player
    @EnvironmentObject var recs: Recs
    @EnvironmentObject var wave: Wave
    @EnvironmentObject var home: HomeModel
    @EnvironmentObject var charts: ChartsModel

    var body: some View {
        let th = app.theme
        ScrollView {
            VStack(alignment: .leading, spacing: 32) {
                WaveHero()

                if !recs.hasTaste {
                    onboarding(th)
                }

                greeting(th)

                if !recs.P.hist.isEmpty {
                    VStack(alignment: .leading, spacing: 14) {
                        SectionHeader(title: "Недавно слушали")
                        HScroll {
                            ForEach(Array(recs.P.hist.prefix(15).enumerated()), id: \.element.id) { pair in
                                ShelfCard(cover: pair.element.cover, title: pair.element.title, subtitle: pair.element.artist, seed: pair.offset, width: 140) {
                                    let list = recs.P.hist.prefix(40).map { Sources.refresh($0) }
                                    player.play(Array(list), start: pair.offset, mode: .list)
                                }
                            }
                        }
                    }
                }

                VStack(alignment: .leading, spacing: 14) {
                    SectionHeader(title: "Для вас", note: "собирается из вашей истории")
                    HScroll {
                        ForEach(home.mixes) { mix in
                            ShelfCard(cover: mix.tracks?.first(where: { !$0.cover.isEmpty })?.cover ?? "",
                                      title: mix.title, subtitle: mix.failed ? "не загрузилась" : mix.subtitle,
                                      seed: mix.id, width: 150) {
                                Task { await home.playMix(mix) }
                            }
                        }
                    }
                }

                artistsShelf(th)

                chartsTeaser(th)

                VStack(alignment: .leading, spacing: 14) {
                    SectionHeader(title: "Новое", note: "Audius, полные версии")
                    if home.freshFailed {
                        HStack {
                            StatusText(text: "Каталог не ответил.", error: true)
                            PillButton(title: "Повторить") { Task { await home.load(force: true) } }
                        }
                    } else if home.fresh.isEmpty {
                        HScroll {
                            ForEach(0..<5, id: \.self) { _ in
                                RoundedRectangle(cornerRadius: th.coverRadius).fill(th.card2).frame(width: 140, height: 140)
                            }
                        }
                    } else {
                        HScroll {
                            ForEach(Array(home.fresh.prefix(12).enumerated()), id: \.element.id) { pair in
                                ShelfCard(cover: pair.element.cover, title: pair.element.title, subtitle: pair.element.artist, seed: pair.offset + 2, width: 140) {
                                    player.play(Array(home.fresh.prefix(30)), start: pair.offset, mode: .list)
                                }
                            }
                        }
                    }
                }

                if !home.uploads.isEmpty {
                    VStack(alignment: .leading, spacing: 14) {
                        SectionHeader(title: "Ваша музыка", note: "\(home.uploads.count) треков")
                        TrackList(tracks: home.uploads, limit: 6)
                    }
                }

                genresGrid(th)
            }
            .padding(.horizontal, 16)
            .padding(.top, 8)
            .padding(.bottom, 24)
        }
        .refreshable { await home.load(force: true) }
        .screenBackground()
        .toolbar(.hidden, for: .navigationBar)
        .task {
            await home.load()
            await charts.load()
        }
    }

    // MARK: sections

    @ViewBuilder
    private func onboarding(_ th: Theme) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            SectionHeader(title: "Что вам нравится?")
            Text("Отметьте пару жанров, чтобы волна сразу попала в настроение. Дальше она учится на лайках и пропусках.")
                .font(.system(size: 14))
                .foregroundStyle(th.muted)
            FlowLayout(spacing: 8) {
                ForEach(STARTER_GENRES, id: \.self) { g in
                    let on = (recs.P.g[g] ?? 0) > 0
                    Chip(text: g, on: on) {
                        recs.seedGenre(g, on: !on)
                        wave.resetPool()
                        home.refreshMixesIfTasteChanged()
                    }
                }
            }
        }
    }

    private var greetingText: String {
        let h = Calendar.current.component(.hour, from: Date())
        if h >= 5 && h < 12 { return "Доброе утро" }
        if h >= 12 && h < 18 { return "Добрый день" }
        return "Добрый вечер"
    }

    private struct QuickTile: Identifiable {
        let id: String
        let title: String
        let cover: String
        let action: () -> Void
    }

    private func quickTiles() -> [QuickTile] {
        var out: [QuickTile] = []
        for pl in recs.P.playlists.prefix(2) {
            let tracks = pl.tracks
            out.append(QuickTile(id: "pl:" + pl.id, title: pl.title, cover: tracks.first(where: { !$0.cover.isEmpty })?.cover ?? "") {
                Player.shared.play(tracks, start: 0, mode: .list)
            })
        }
        if !recs.P.likes.isEmpty && out.count < 6 {
            let liked = recs.P.likes.values.sorted { ($0.likedAt ?? 0) > ($1.likedAt ?? 0) }
            out.append(QuickTile(id: "likes", title: "Любимое", cover: liked.first(where: { !$0.cover.isEmpty })?.cover ?? "") {
                Player.shared.play(liked, start: 0, mode: .list)
            })
        }
        let hist = recs.P.hist
        for (i, t) in hist.enumerated() {
            if out.count >= 6 { break }
            let list = Array(hist.prefix(40))
            out.append(QuickTile(id: "h:" + t.id, title: t.title, cover: t.coverSm.isEmpty ? t.cover : t.coverSm) {
                Player.shared.play(list, start: min(i, list.count - 1), mode: .list)
            })
        }
        return out
    }

    @ViewBuilder
    private func greeting(_ th: Theme) -> some View {
        let tiles = quickTiles()
        VStack(alignment: .leading, spacing: 14) {
            SectionHeader(title: greetingText)
            if tiles.isEmpty {
                Text("Здесь появятся треки и плейлисты, которые вы слушали недавно.")
                    .font(.system(size: 14))
                    .foregroundStyle(th.muted)
            } else {
                LazyVGrid(columns: [GridItem(.flexible(), spacing: 10), GridItem(.flexible(), spacing: 10)], spacing: 10) {
                    ForEach(Array(tiles.enumerated()), id: \.element.id) { pair in
                        Button(action: pair.element.action) {
                            HStack(spacing: 10) {
                                CoverView(path: pair.element.cover, seed: pair.offset, radius: 0)
                                    .frame(width: 52, height: 52)
                                Text(pair.element.title)
                                    .font(.system(size: 13, weight: .semibold))
                                    .foregroundStyle(th.fg)
                                    .lineLimit(2)
                                    .multilineTextAlignment(.leading)
                                Spacer(minLength: 0)
                            }
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .background(th.card2)
                            .clipShape(RoundedRectangle(cornerRadius: min(th.radius, 8), style: .continuous))
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
        }
    }

    private struct ArtistItem: Identifiable {
        let id: String
        let name: String
        let cover: String
    }

    private func favouriteArtists() -> [ArtistItem] {
        var ids: [String] = Array(recs.P.fav.keys).sorted()
        for a in recs.topArtists(10) where !ids.contains(a) { ids.append(a) }
        let pool = recs.P.hist + Array(recs.P.likes.values)
        var out: [ArtistItem] = []
        for id in ids.prefix(12) {
            let name = recs.P.fav[id] ?? recs.P.an[id] ?? ""
            if name.isEmpty { continue }
            let cover = pool.first(where: { $0.artistId == id && !$0.cover.isEmpty })?.cover ?? ""
            out.append(ArtistItem(id: id, name: name, cover: cover))
        }
        return out
    }

    @ViewBuilder
    private func artistsShelf(_ th: Theme) -> some View {
        let artists = favouriteArtists()
        if !artists.isEmpty {
            VStack(alignment: .leading, spacing: 14) {
                SectionHeader(title: "Любимые артисты")
                HScroll(spacing: 16) {
                    ForEach(Array(artists.enumerated()), id: \.element.id) { pair in
                        ShelfCard(cover: pair.element.cover, title: pair.element.name, subtitle: "", seed: pair.offset, width: 96, round: true) {
                            playArtist(pair.element)
                        }
                    }
                }
            }
        }
    }

    private func playArtist(_ a: ArtistItem) {
        let id = a.id
        Task { @MainActor in
            var list: [Track] = []
            if id.hasPrefix("au:") { list = (try? await Sources.auArtist(id, limit: 30)) ?? [] }
            if list.isEmpty {
                let r = Recs.shared
                list = Sources.dedupe(r.P.hist.filter { $0.artistId == id } + r.P.likes.values.filter { $0.artistId == id })
            }
            if list.isEmpty {
                AppModel.shared.toast("Не нашёл треков этого артиста")
            } else {
                Player.shared.play(list, start: 0, mode: .list)
            }
        }
    }

    @ViewBuilder
    private func chartsTeaser(_ th: Theme) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            SectionHeader(title: "Чарты", note: charts.data?.name, actionTitle: "Все чарты") {
                app.tab = .charts
            }
            if let d = charts.data, !d.items.isEmpty {
                VStack(spacing: 0) {
                    ForEach(Array(d.items.prefix(5))) { it in
                        Button {
                            Task { await charts.playItem(it) }
                        } label: {
                            HStack(spacing: 12) {
                                Text("\(it.pos)")
                                    .font(th.display(20).monospacedDigit())
                                    .foregroundStyle(th.fg)
                                    .frame(width: 30, alignment: .trailing)
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(it.title).font(.system(size: 15, weight: .semibold)).foregroundStyle(th.fg).lineLimit(1)
                                    Text(it.artists).font(.system(size: 13)).foregroundStyle(th.muted).lineLimit(1)
                                }
                                Spacer()
                                if charts.checking.contains(it.pos) {
                                    ProgressView().controlSize(.small).tint(th.muted)
                                } else {
                                    Image(systemName: "play.circle").font(.system(size: 22)).foregroundStyle(th.muted)
                                }
                            }
                            .padding(.vertical, 10)
                            .padding(.horizontal, 8)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        Rectangle().fill(th.line).frame(height: 1)
                    }
                }
                .modifier(TrackListChrome())
            } else if charts.loading {
                VStack(spacing: 0) { ForEach(0..<3, id: \.self) { _ in SkeletonRow() } }
            } else {
                StatusText(text: charts.error ?? "Чарт пока не загрузился")
            }
        }
    }

    private static let tileNames: [String] = MoodPreset.order + ["Electronic", "Hip-Hop/Rap", "Lo-Fi", "House", "Techno", "Drum & Bass", "Ambient", "Rock", "Jazz"]

    @ViewBuilder
    private func genresGrid(_ th: Theme) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            SectionHeader(title: "Жанры и настроения")
            LazyVGrid(columns: [GridItem(.flexible(), spacing: 10), GridItem(.flexible(), spacing: 10)], spacing: 10) {
                ForEach(Array(HomeView.tileNames.enumerated()), id: \.element) { pair in
                    let name = pair.element
                    let isMood = MoodPreset.all[name] != nil
                    let on = isMood ? wave.mood == name : wave.focusGenre == name
                    Button {
                        Task {
                            if isMood { await wave.selectMood(name) } else { await wave.startGenre(name) }
                        }
                    } label: {
                        ZStack(alignment: .bottomLeading) {
                            CoverPattern(seed: pair.offset, fg: th.fg.opacity(on ? 0.5 : 0.18), bg: on ? th.card2 : th.card)
                            VStack(alignment: .leading, spacing: 2) {
                                Text(isMood ? "НАСТРОЕНИЕ" : "ЖАНР")
                                    .font(.system(size: 9, weight: .semibold))
                                    .tracking(1.2)
                                    .foregroundStyle(th.muted)
                                Text(name)
                                    .font(th.heading(18))
                                    .foregroundStyle(th.fg)
                                    .lineLimit(1)
                                    .minimumScaleFactor(0.7)
                            }
                            .padding(12)
                        }
                        .frame(height: 84)
                        .clipShape(RoundedRectangle(cornerRadius: min(th.radius, 14), style: .continuous))
                        .overlay(RoundedRectangle(cornerRadius: min(th.radius, 14), style: .continuous).stroke(on ? th.fg : th.line, lineWidth: th.border))
                    }
                    .buttonStyle(.plain)
                }
            }
        }
    }
}
