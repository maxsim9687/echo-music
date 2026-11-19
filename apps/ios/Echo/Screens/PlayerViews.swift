import SwiftUI

/// Thin progress line on top of the mini player.
struct MiniProgress: View {
    @ObservedObject var clock: PlaybackClock
    let color: Color

    var body: some View {
        GeometryReader { geo in
            Rectangle()
                .fill(color)
                .frame(width: geo.size.width * CGFloat(clock.fraction), height: 2)
        }
        .frame(height: 2)
    }
}

/// Mini player bar shown above the tab bar on every tab.
struct MiniPlayer: View {
    @EnvironmentObject var app: AppModel
    @EnvironmentObject var player: Player
    @EnvironmentObject var recs: Recs
    @EnvironmentObject var wave: Wave

    var body: some View {
        let th = app.theme
        VStack(spacing: 0) {
            MiniProgress(clock: player.clock, color: th.fg)
            HStack(spacing: 12) {
                if let t = player.current {
                    CoverView(path: t.coverSm, seed: 1, radius: min(th.coverRadius, 8))
                        .frame(width: 44, height: 44)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(t.title)
                            .font(.system(size: 15, weight: .semibold))
                            .foregroundStyle(th.fg)
                            .lineLimit(1)
                        Text(t.artist)
                            .font(.system(size: 13))
                            .foregroundStyle(th.muted)
                            .lineLimit(1)
                    }
                    Spacer(minLength: 4)
                    IconButton(system: recs.isLiked(t) ? "heart.fill" : "heart", on: recs.isLiked(t), size: 18) {
                        let on = recs.toggleLike(t)
                        app.toast(on ? "Добавлено в любимое" : "Убрано из любимого")
                    }
                } else {
                    RoundedRectangle(cornerRadius: min(th.coverRadius, 8)).fill(th.card2)
                        .frame(width: 44, height: 44)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(wave.starting ? "Подбираю треки…" : "Ничего не играет")
                            .font(.system(size: 15, weight: .semibold))
                            .foregroundStyle(th.fg)
                            .lineLimit(1)
                        Text("Включите волну или найдите трек")
                            .font(.system(size: 13))
                            .foregroundStyle(th.muted)
                            .lineLimit(1)
                    }
                    Spacer(minLength: 4)
                }
                Button {
                    player.toggle()
                } label: {
                    ZStack {
                        RoundedRectangle(cornerRadius: th.pill ? 999 : th.buttonRadius, style: .continuous).fill(th.fg)
                        if player.isBuffering || (wave.starting && player.current == nil) {
                            ProgressView().tint(th.bg).controlSize(.small)
                        } else {
                            Image(systemName: player.isPlaying ? "pause.fill" : "play.fill")
                                .font(.system(size: 17, weight: .bold))
                                .foregroundStyle(th.bg)
                        }
                    }
                    .frame(width: 42, height: 42)
                }
                .buttonStyle(.plain)
                IconButton(system: "forward.end.fill", size: 17, disabled: player.current == nil) {
                    player.next()
                }
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 8)
        }
        .background {
            if th.glass {
                Rectangle().fill(.ultraThinMaterial)
            } else {
                Rectangle().fill(th.bg)
            }
        }
        .overlay(alignment: .top) { Rectangle().fill(th.line).frame(height: th.border) }
        .contentShape(Rectangle())
        .onTapGesture {
            if player.current != nil { app.showFullPlayer = true }
        }
    }
}

/// Seek bar with times; dragging does not fight the clock updates.
struct SeekBar: View {
    @ObservedObject var clock: PlaybackClock
    let theme: Theme
    let onSeek: (Double) -> Void
    @State private var dragging = false
    @State private var value: Double = 0

    var body: some View {
        let total = max(clock.duration, 1)
        VStack(spacing: 6) {
            Slider(value: Binding(get: { dragging ? value : min(clock.time, total) },
                                  set: { value = $0 }),
                   in: 0...total,
                   onEditingChanged: { editing in
                       if editing {
                           value = clock.time
                           dragging = true
                       } else {
                           dragging = false
                           onSeek(value)
                       }
                   })
                .tint(theme.fg)
            HStack {
                Text(fmtTime(dragging ? value : clock.time))
                Spacer()
                Text(fmtTime(clock.duration))
            }
            .font(.system(size: 12).monospacedDigit())
            .foregroundStyle(theme.muted)
        }
    }
}

/// Full-screen player: big cover, seek, prev/play/next, like, download, shuffle, queue.
struct FullPlayerView: View {
    @EnvironmentObject var app: AppModel
    @EnvironmentObject var player: Player
    @EnvironmentObject var recs: Recs
    @EnvironmentObject var downloads: Downloads
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        let th = app.theme
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 24) {
                    if let t = player.current {
                        nowPlaying(t, th)
                        queueSection(th)
                    } else {
                        StatusText(text: "Ничего не играет")
                    }
                }
                .padding(.horizontal, 20)
                .padding(.bottom, 40)
            }
            .screenBackground()
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button {
                        dismiss()
                    } label: {
                        Image(systemName: "chevron.down").font(.system(size: 18, weight: .semibold)).foregroundStyle(th.fg)
                    }
                }
                ToolbarItem(placement: .principal) {
                    VStack(spacing: 2) {
                        Text(player.mode == .wave ? "МОЯ ВОЛНА" : "СЕЙЧАС ИГРАЕТ")
                            .font(.system(size: 11, weight: .semibold))
                            .tracking(1.4)
                            .foregroundStyle(th.muted)
                    }
                }
            }
            .navigationBarTitleDisplayMode(.inline)
        }
        .preferredColorScheme(th.light ? .light : .dark)
        .confirmationDialog("Скачать", isPresented: Binding(get: { app.dlTarget != nil && app.showFullPlayer },
                                                            set: { if !$0 { app.dlTarget = nil } }),
                            titleVisibility: .visible, presenting: app.dlTarget) { t in
            downloadButtons(t)
        } message: { t in
            Text(t.artist + " — " + t.title)
        }
        .overlay(alignment: .bottom) { ToastStack().padding(.bottom, 24) }
    }

    @ViewBuilder
    private func nowPlaying(_ t: Track, _ th: Theme) -> some View {
        let liked = recs.isLiked(t)
        TimelineView(.animation(minimumInterval: nil, paused: !(th.roundCovers && player.isPlaying))) { tl in
            CoverView(path: t.cover, seed: 3, radius: min(th.coverRadius * 1.5, 22))
                .frame(maxWidth: .infinity)
                .rotationEffect(.degrees(th.roundCovers ? (tl.date.timeIntervalSinceReferenceDate * 20).truncatingRemainder(dividingBy: 360) : 0))
                .shadow(color: .black.opacity(0.35), radius: 30, y: 20)
        }
        .padding(.top, 8)

        HStack(alignment: .center, spacing: 12) {
            VStack(alignment: .leading, spacing: 4) {
                Text(t.title)
                    .font(th.display(26))
                    .foregroundStyle(th.fg)
                    .lineLimit(2)
                HStack(spacing: 8) {
                    Text(t.artist).lineLimit(1)
                    if !t.genre.isEmpty { Text("· " + t.genre).lineLimit(1) }
                    Text(t.quality)
                        .font(.system(size: 10, weight: .semibold))
                        .padding(.horizontal, 5)
                        .padding(.vertical, 2)
                        .overlay(RoundedRectangle(cornerRadius: 3).stroke(th.line, lineWidth: 1))
                        .fixedSize()
                }
                .font(.system(size: 15))
                .foregroundStyle(th.muted)
            }
            Spacer()
            IconButton(system: liked ? "heart.fill" : "heart", on: liked, size: 24) {
                let on = recs.toggleLike(t)
                app.toast(on ? "Добавлено в любимое" : "Убрано из любимого")
            }
        }

        SeekBar(clock: player.clock, theme: th) { player.seek(to: $0) }

        HStack {
            IconButton(system: "shuffle", on: player.shuffle, size: 20) { player.toggleShuffle() }
            Spacer()
            IconButton(system: "backward.end.fill", on: true, size: 26) { player.prev() }
            Spacer()
            PlayKnob(playing: player.isPlaying, size: 76, busy: player.isBuffering) { player.toggle() }
            Spacer()
            IconButton(system: "forward.end.fill", on: true, size: 26) { player.next() }
            Spacer()
            IconButton(system: "arrow.down.circle", size: 22, disabled: !t.downloadable) { app.dlTarget = t }
        }

        HStack(spacing: 10) {
            if player.mode == .wave {
                Chip(text: "Не то", icon: "nosign") { player.banCurrent() }
            }
            if let item = downloads.item(for: t) {
                ShareLink(item: downloads.fileURL(item)) {
                    HStack(spacing: 6) {
                        Image(systemName: "square.and.arrow.up")
                        Text("Поделиться файлом")
                    }
                    .font(.system(size: 14, weight: .medium))
                    .foregroundStyle(th.fg)
                    .padding(.vertical, 9)
                    .padding(.horizontal, 14)
                    .overlay(RoundedRectangle(cornerRadius: th.pill ? 999 : th.buttonRadius).stroke(th.line, lineWidth: th.border))
                }
            }
            if let link = URL(string: t.link), !t.link.isEmpty {
                Link(destination: link) {
                    HStack(spacing: 6) {
                        Image(systemName: "arrow.up.right.square")
                        Text(t.src == "jamendo" ? "Jamendo" : "Audius")
                    }
                    .font(.system(size: 14, weight: .medium))
                    .foregroundStyle(th.muted)
                    .padding(.vertical, 9)
                    .padding(.horizontal, 14)
                    .overlay(RoundedRectangle(cornerRadius: th.pill ? 999 : th.buttonRadius).stroke(th.line, lineWidth: th.border))
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    @ViewBuilder
    private func queueSection(_ th: Theme) -> some View {
        let start = player.index + 1
        let upcoming: [QueueEntry] = start < player.queue.count
            ? Array(player.queue.enumerated().map { QueueEntry(index: $0.offset, track: $0.element) }.dropFirst(start).prefix(40))
            : []
        SectionHeader(title: "Очередь", note: upcoming.isEmpty ? "пусто" : "\(player.queue.count - start) дальше")
        if upcoming.isEmpty {
            StatusText(text: player.mode == .wave ? "Волна подберёт следующие треки" : "Дальше ничего нет")
        } else {
            VStack(spacing: 0) {
                ForEach(upcoming) { entry in
                    TrackRow(track: entry.track, index: entry.index, list: player.queue, onTap: { player.playAt(entry.index) })
                    Rectangle().fill(th.line).frame(height: 1)
                }
            }
            .modifier(TrackListChrome())
        }
    }
}

struct QueueEntry: Identifiable {
    let index: Int
    let track: Track
    var id: String { track.id }
}

/// Buttons of the download menu (`downloadOptions`).
@MainActor
@ViewBuilder
func downloadButtons(_ track: Track) -> some View {
    ForEach(downloadOptions(track)) { o in
        Button(o.label + " · " + o.note) {
            Task { @MainActor in await Downloads.shared.download(track, o) }
        }
    }
    Button("Отмена", role: .cancel) {}
}
