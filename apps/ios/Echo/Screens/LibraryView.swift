import SwiftUI

/// Коллекция: Любимое, Мои плейлисты, Недавно слушали, Скачанное.
struct LibraryView: View {
    @EnvironmentObject var app: AppModel
    @EnvironmentObject var recs: Recs
    @EnvironmentObject var player: Player
    @EnvironmentObject var downloads: Downloads

    private var liked: [Track] {
        recs.P.likes.values.map { Sources.refresh($0) }.sorted { ($0.likedAt ?? 0) > ($1.likedAt ?? 0) }
    }

    private var history: [Track] {
        Array(recs.P.hist.prefix(100)).map { Sources.refresh($0) }
    }

    var body: some View {
        let th = app.theme
        let likedList = liked
        let hist = history
        ScrollView {
            VStack(alignment: .leading, spacing: 30) {
                Text("Коллекция")
                    .font(th.display(40))
                    .textCase(th.uppercase ? .uppercase : nil)
                    .foregroundStyle(th.fg)
                    .padding(.top, 8)

                VStack(alignment: .leading, spacing: 12) {
                    header("Любимое", note: likedList.isEmpty ? nil : "\(likedList.count) треков", all: likedList.count > 6 ? AnyView(TrackListScreen(title: "Любимое", tracks: likedList, empty: "Нажмите на сердечко у трека, и он появится здесь")) : nil)
                    TrackList(tracks: likedList, empty: "Нажмите на сердечко у трека, и он появится здесь", limit: 6)
                }

                VStack(alignment: .leading, spacing: 12) {
                    SectionHeader(title: "Мои плейлисты")
                    if recs.P.playlists.isEmpty {
                        StatusText(text: "Попросите ИИшку в поиске собрать плейлист и сохраните его")
                    } else {
                        HScroll {
                            ForEach(Array(recs.P.playlists.enumerated()), id: \.element.id) { pair in
                                NavigationLink {
                                    PlaylistScreen(playlist: pair.element)
                                } label: {
                                    VStack(alignment: .leading, spacing: 8) {
                                        CoverView(path: pair.element.tracks.first(where: { !$0.cover.isEmpty })?.cover ?? "", seed: pair.offset)
                                            .frame(width: 150, height: 150)
                                        Text(pair.element.title)
                                            .font(.system(size: 14, weight: .semibold))
                                            .foregroundStyle(th.fg)
                                            .lineLimit(2)
                                            .multilineTextAlignment(.leading)
                                        Text("\(pair.element.tracks.count) треков")
                                            .font(.system(size: 12))
                                            .foregroundStyle(th.muted)
                                    }
                                    .frame(width: 150, alignment: .leading)
                                }
                                .buttonStyle(.plain)
                            }
                        }
                    }
                }

                VStack(alignment: .leading, spacing: 12) {
                    header("Недавно слушали", note: nil, all: hist.count > 6 ? AnyView(TrackListScreen(title: "Недавно слушали", tracks: hist, empty: "История появится после первых прослушиваний")) : nil)
                    TrackList(tracks: hist, empty: "История появится после первых прослушиваний", limit: 6)
                }

                VStack(alignment: .leading, spacing: 12) {
                    SectionHeader(title: "Скачанное", note: downloads.items.isEmpty ? nil : "\(downloads.items.count) файлов")
                    if downloads.items.isEmpty {
                        StatusText(text: "Скачанные треки появятся здесь и в приложении «Файлы». Они играют без интернета.")
                    } else {
                        DownloadsList()
                    }
                }
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 24)
        }
        .refreshable { downloads.reload() }
        .screenBackground()
        .toolbar(.hidden, for: .navigationBar)
        .onAppear { downloads.reload() }
    }

    @ViewBuilder
    private func header(_ title: String, note: String?, all: AnyView?) -> some View {
        let th = app.theme
        HStack(alignment: .firstTextBaseline) {
            SectionHeader(title: title, note: note)
            if let all = all {
                NavigationLink {
                    all
                } label: {
                    Text("Все").font(.subheadline.weight(.semibold)).foregroundStyle(th.fg)
                }
            }
        }
    }
}

struct DownloadsList: View {
    @EnvironmentObject var app: AppModel
    @EnvironmentObject var downloads: Downloads
    @EnvironmentObject var player: Player
    @State private var confirm: DownloadedItem? = nil

    var body: some View {
        let th = app.theme
        let items = downloads.items
        let tracks = items.map { $0.track }
        VStack(spacing: 0) {
            ForEach(Array(items.enumerated()), id: \.element.id) { pair in
                let item = pair.element
                TrackRow(track: item.track, index: pair.offset, list: tracks, trailing: AnyView(
                    HStack(spacing: 0) {
                        Text(item.fmt.uppercased())
                            .font(.system(size: 10, weight: .semibold))
                            .foregroundStyle(th.muted)
                            .padding(.trailing, 4)
                        ShareLink(item: downloads.fileURL(item)) {
                            Image(systemName: "square.and.arrow.up")
                                .font(.system(size: 17))
                                .foregroundStyle(th.muted)
                                .frame(width: 36, height: 40)
                        }
                        Button {
                            confirm = item
                        } label: {
                            Image(systemName: "trash")
                                .font(.system(size: 16))
                                .foregroundStyle(th.muted)
                                .frame(width: 36, height: 40)
                        }
                        .buttonStyle(.plain)
                    }
                ))
                if pair.offset < items.count - 1 {
                    Rectangle().fill(th.line).frame(height: 1)
                }
            }
        }
        .modifier(TrackListChrome())
        .confirmationDialog("Удалить файл?", isPresented: Binding(get: { confirm != nil }, set: { if !$0 { confirm = nil } }),
                            titleVisibility: .visible, presenting: confirm) { item in
            Button("Удалить «\(item.track.title)»", role: .destructive) {
                downloads.delete(item)
                app.toast("Файл удалён")
            }
            Button("Отмена", role: .cancel) {}
        } message: { item in
            Text(item.file)
        }
    }
}

/// Full list pushed from a section's «Все».
struct TrackListScreen: View {
    @EnvironmentObject var app: AppModel
    let title: String
    let tracks: [Track]
    var empty: String = "Пока пусто"

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                TrackList(tracks: tracks, empty: empty)
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 24)
        }
        .screenBackground()
        .navigationTitle(title)
        .navigationBarTitleDisplayMode(.large)
    }
}

struct PlaylistScreen: View {
    @EnvironmentObject var app: AppModel
    @EnvironmentObject var player: Player
    @EnvironmentObject var recs: Recs
    @Environment(\.dismiss) private var dismiss
    let playlist: Playlist
    @State private var confirmDelete = false

    var body: some View {
        let th = app.theme
        let tracks = playlist.tracks.map { Sources.refresh($0) }
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                HStack(alignment: .bottom, spacing: 16) {
                    CoverView(path: tracks.first(where: { !$0.cover.isEmpty })?.cover ?? "", seed: 4)
                        .frame(width: 132, height: 132)
                    VStack(alignment: .leading, spacing: 6) {
                        Text("ПЛЕЙЛИСТ").font(.system(size: 10, weight: .semibold)).tracking(1.4).foregroundStyle(th.muted)
                        Text(playlist.title).font(th.display(26)).foregroundStyle(th.fg).lineLimit(3)
                        if !playlist.desc.isEmpty {
                            Text(playlist.desc).font(.system(size: 13)).foregroundStyle(th.muted).lineLimit(3)
                        }
                        Text("\(tracks.count) треков").font(.caption).foregroundStyle(th.muted)
                    }
                }
                HStack(spacing: 10) {
                    PillButton(title: "Слушать", icon: "play.fill", solid: true) { player.play(tracks, start: 0, mode: .list) }
                    PillButton(title: "Перемешать", icon: "shuffle") { player.play(tracks.shuffled(), start: 0, mode: .list) }
                    Spacer()
                    IconButton(system: "trash", size: 18) { confirmDelete = true }
                }
                TrackList(tracks: tracks, empty: "В плейлисте пусто")
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 24)
        }
        .screenBackground()
        .navigationBarTitleDisplayMode(.inline)
        .confirmationDialog("Удалить плейлист?", isPresented: $confirmDelete, titleVisibility: .visible) {
            Button("Удалить", role: .destructive) {
                recs.deletePlaylist(playlist.id)
                dismiss()
            }
            Button("Отмена", role: .cancel) {}
        }
    }
}
