import SwiftUI

/// Equalizer bars shown on the playing row.
struct EqBars: View {
    let color: Color
    let playing: Bool

    var body: some View {
        TimelineView(.animation(minimumInterval: nil, paused: !playing)) { tl in
            let t = tl.date.timeIntervalSinceReferenceDate
            HStack(alignment: .bottom, spacing: 2) {
                ForEach(0..<3, id: \.self) { i in
                    let h = playing ? 3 + 11 * abs(sin(t * 3.5 + Double(i) * 1.1)) : 3
                    Rectangle().fill(color).frame(width: 3, height: CGFloat(h))
                }
            }
            .frame(width: 14, height: 14, alignment: .bottom)
        }
    }
}

/// One `.tr` row: number, cover, title / artist, quality, duration, like, download.
struct TrackRow: View {
    @EnvironmentObject var app: AppModel
    @EnvironmentObject var player: Player
    @EnvironmentObject var recs: Recs

    let track: Track
    let index: Int
    let list: [Track]
    var showNumber: Bool = true
    var trailing: AnyView? = nil
    var onTap: (() -> Void)? = nil

    var body: some View {
        let th = app.theme
        let isNow = player.current?.id == track.id
        let liked = recs.P.likes[track.id] != nil
        HStack(spacing: 12) {
            if showNumber {
                ZStack {
                    if isNow {
                        EqBars(color: th.fg, playing: player.isPlaying)
                    } else {
                        Text("\(index + 1)")
                            .font(.system(size: 13).monospacedDigit())
                            .foregroundStyle(th.muted)
                    }
                }
                .frame(width: 24, alignment: .trailing)
            }
            CoverView(path: track.coverSm, seed: index, radius: min(th.coverRadius, 6))
                .frame(width: 44, height: 44)
            VStack(alignment: .leading, spacing: 2) {
                Text(track.title)
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundStyle(th.fg)
                    .lineLimit(1)
                HStack(spacing: 6) {
                    Text(track.artist)
                        .lineLimit(1)
                    if !track.quality.isEmpty {
                        Text(track.quality)
                            .font(.system(size: 9, weight: .semibold))
                            .padding(.horizontal, 4)
                            .padding(.vertical, 2)
                            .overlay(RoundedRectangle(cornerRadius: 3).stroke(th.line, lineWidth: 1))
                            .lineLimit(1)
                            .fixedSize()
                    }
                }
                .font(.system(size: 13))
                .foregroundStyle(th.muted)
            }
            Spacer(minLength: 4)
            Text(track.durationText)
                .font(.system(size: 12).monospacedDigit())
                .foregroundStyle(th.muted)
            if let trailing = trailing {
                trailing
            } else {
                IconButton(system: liked ? "heart.fill" : "heart", on: liked, size: 17) {
                    let on = recs.toggleLike(track)
                    app.toast(on ? "Добавлено в любимое" : "Убрано из любимого")
                }
                IconButton(system: "arrow.down.circle", size: 18, disabled: !track.downloadable) {
                    app.dlTarget = track
                }
            }
        }
        .padding(.vertical, 8)
        .padding(.horizontal, 8)
        .background(isNow ? th.card2 : Color.clear)
        .contentShape(Rectangle())
        .onTapGesture {
            if let onTap = onTap {
                onTap()
            } else if isNow {
                player.toggle()
            } else {
                player.play(list, start: index, mode: .list)
            }
        }
    }
}

/// `.tracks` list with separators (a glass card on «Стекло»).
struct TrackList: View {
    @EnvironmentObject var app: AppModel
    let tracks: [Track]
    var empty: String = "Пока пусто"
    var limit: Int? = nil
    var loading: Bool = false

    var body: some View {
        let th = app.theme
        let shown = limit.map { Array(tracks.prefix($0)) } ?? tracks
        Group {
            if loading && tracks.isEmpty {
                VStack(spacing: 0) {
                    ForEach(0..<5, id: \.self) { _ in SkeletonRow() }
                }
            } else if tracks.isEmpty {
                StatusText(text: empty)
            } else {
                LazyVStack(spacing: 0) {
                    ForEach(Array(shown.enumerated()), id: \.element.id) { pair in
                        TrackRow(track: pair.element, index: pair.offset, list: tracks)
                        if pair.offset < shown.count - 1 {
                            Rectangle().fill(th.line).frame(height: 1)
                        }
                    }
                }
            }
        }
        .modifier(TrackListChrome())
    }
}

struct TrackListChrome: ViewModifier {
    @EnvironmentObject var app: AppModel

    @ViewBuilder
    func body(content: Content) -> some View {
        let th = app.theme
        if th.glass {
            content.echoCard(radius: 20)
        } else {
            content.overlay(alignment: .top) {
                Rectangle().fill(th.line).frame(height: th.border)
            }
        }
    }
}

struct SkeletonRow: View {
    @EnvironmentObject var app: AppModel

    var body: some View {
        let th = app.theme
        HStack(spacing: 12) {
            RoundedRectangle(cornerRadius: 6).fill(th.card2).frame(width: 44, height: 44)
            VStack(alignment: .leading, spacing: 6) {
                RoundedRectangle(cornerRadius: 3).fill(th.card2).frame(width: 160, height: 12)
                RoundedRectangle(cornerRadius: 3).fill(th.card).frame(width: 100, height: 10)
            }
            Spacer()
        }
        .padding(.vertical, 8)
        .padding(.horizontal, 8)
        .padding(.leading, 36)
    }
}

/// Horizontal square card (Spotify shelf).
struct ShelfCard: View {
    @EnvironmentObject var app: AppModel
    let cover: String
    let title: String
    let subtitle: String
    var seed: Int = 0
    var width: CGFloat = 150
    var round: Bool = false
    let action: () -> Void

    var body: some View {
        let th = app.theme
        Button(action: action) {
            VStack(alignment: round ? .center : .leading, spacing: 8) {
                CoverView(path: cover, seed: seed, round: round)
                    .frame(width: width, height: width)
                Text(title)
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundStyle(th.fg)
                    .lineLimit(2)
                    .multilineTextAlignment(round ? .center : .leading)
                if !subtitle.isEmpty {
                    Text(subtitle)
                        .font(.system(size: 12))
                        .foregroundStyle(th.muted)
                        .lineLimit(1)
                }
            }
            .frame(width: width, alignment: round ? .center : .leading)
        }
        .buttonStyle(.plain)
    }
}
