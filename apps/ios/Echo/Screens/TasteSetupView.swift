import SwiftUI

/// Настройка вкуса: genres, favourite artists, favourite songs → likes (web #welcome).
struct TasteSetupView: View {
    @EnvironmentObject var app: AppModel
    @EnvironmentObject var recs: Recs
    @EnvironmentObject var wave: Wave
    @Environment(\.dismiss) private var dismiss
    var inSheet: Bool = false

    @State private var artistText = ""
    @State private var artistNote = ""
    @State private var addingArtists = false
    @State private var song = ""
    @State private var songResults: [Track] = []
    @State private var songFailed = false
    @State private var songLoading = false

    var body: some View {
        let th = app.theme
        let favs = recs.P.fav.sorted { $0.value < $1.value }
        let liked = recs.P.likes.values.sorted { ($0.likedAt ?? 0) > ($1.likedAt ?? 0) }.map { Sources.refresh($0) }
        ScrollView {
            VStack(alignment: .leading, spacing: 30) {
                VStack(alignment: .leading, spacing: 10) {
                    Eyebrow(text: "Настройка вкуса")
                    Text("Расскажите, что вы любите")
                        .font(th.display(34))
                        .foregroundStyle(th.fg)
                    Text("Волна сразу подстроится под ответы. Всё это можно поменять потом в настройках.")
                        .font(.system(size: 15))
                        .foregroundStyle(th.muted)
                }
                .padding(.top, 8)

                VStack(alignment: .leading, spacing: 12) {
                    SectionHeader(title: "Жанры", note: "выберите несколько")
                    FlowLayout(spacing: 8) {
                        ForEach(STARTER_GENRES, id: \.self) { g in
                            let on = (recs.P.g[g] ?? 0) > 0
                            Chip(text: g, on: on) {
                                recs.seedGenre(g, on: !on)
                                wave.resetPool()
                            }
                        }
                    }
                }

                VStack(alignment: .leading, spacing: 12) {
                    SectionHeader(title: "Любимые артисты")
                    Text("впишите имя и нажмите «Добавить»").font(.caption).foregroundStyle(th.muted)
                    HStack(spacing: 10) {
                        EchoField(placeholder: "Например: Skrillex, deadmau5", text: $artistText)
                            .submitLabel(.done)
                            .onSubmit { addArtists() }
                        PillButton(title: "Добавить", solid: true, busy: addingArtists) { addArtists() }
                            .disabled(addingArtists)
                    }
                    if !favs.isEmpty {
                        FlowLayout(spacing: 8) {
                            ForEach(favs, id: \.key) { pair in
                                Chip(text: pair.value, icon: "xmark", on: true) {
                                    recs.removeFavArtist(pair.key)
                                    wave.resetPool()
                                }
                            }
                        }
                    }
                    if !artistNote.isEmpty {
                        Text(artistNote).font(.system(size: 13)).foregroundStyle(th.muted)
                    }
                }

                VStack(alignment: .leading, spacing: 12) {
                    SectionHeader(title: "Любимые песни")
                    Text("найдите и нажмите на сердце: песня попадёт в любимое").font(.caption).foregroundStyle(th.muted)
                    EchoField(placeholder: "Название песни или исполнитель", text: $song, icon: "magnifyingglass")
                    if song.trimmingCharacters(in: .whitespaces).isEmpty {
                        TrackList(tracks: Array(liked.prefix(8)), empty: "Найдите песню, которую любите, и нажмите на сердце")
                    } else if songFailed {
                        StatusText(text: "Поиск не ответил. Попробуйте ещё раз.", error: true)
                    } else {
                        TrackList(tracks: songResults, empty: "«\(song)» не нашлось. Попробуйте написать по-английски или только исполнителя.", loading: songLoading)
                    }
                }

                HStack(spacing: 12) {
                    PillButton(title: "Готово, к моей волне", solid: true) {
                        wave.resetPool()
                        app.tab = .home
                        dismiss()
                    }
                    Text("Жанров: \(recs.topGenres(20).count) · артистов: \(favs.count) · песен в любимом: \(recs.P.likes.count)")
                        .font(.caption)
                        .foregroundStyle(th.muted)
                }
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 30)
        }
        .scrollDismissesKeyboard(.interactively)
        .screenBackground()
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            if inSheet {
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button("Позже") { dismiss() }.foregroundStyle(th.fg)
                }
            }
        }
        .task(id: song) {
            let q = song.trimmingCharacters(in: .whitespacesAndNewlines)
            if q.isEmpty {
                songResults = []
                songFailed = false
                return
            }
            try? await Task.sleep(nanoseconds: 350_000_000)
            if Task.isCancelled { return }
            songLoading = true
            songResults = []
            let r = await Sources.searchAll(q)
            if Task.isCancelled { return }
            songResults = Array(r.list.prefix(10))
            songFailed = r.failed
            songLoading = false
        }
    }

    private func addArtists() {
        let names = artistText.split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }.prefix(5)
        guard !names.isEmpty, !addingArtists else { return }
        addingArtists = true
        artistNote = "Ищу…"
        let list = Array(names)
        Task { @MainActor in
            var missed: [String] = []
            for name in list {
                do {
                    let found = try await Sources.auSearch(name, limit: 30)
                    let low = name.lowercased()
                    let hit = found.first(where: { $0.artist.lowercased() == low }) ?? found.first(where: { $0.artist.lowercased().contains(low) })
                    if let h = hit {
                        Recs.shared.addFavArtist(id: h.artistId, name: h.artist, genre: h.genre)
                    } else {
                        missed.append(name)
                    }
                } catch {
                    missed.append(name)
                }
            }
            artistText = ""
            artistNote = missed.isEmpty ? "Добавлено" : "Не нашёл в каталоге: \(missed.joined(separator: ", ")). Похоже, их музыки нет на Audius."
            addingArtists = false
            Wave.shared.resetPool()
        }
    }
}
