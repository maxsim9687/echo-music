import SwiftUI

/// Чарты: country chips, top list, «Найти на Audius» badges, «Слушать то, что нашлось», «Открыть в Spotify».
struct ChartsView: View {
    @EnvironmentObject var app: AppModel
    @EnvironmentObject var charts: ChartsModel

    var body: some View {
        let th = app.theme
        ScrollView {
            LazyVStack(alignment: .leading, spacing: 18) {
                Text("Чарты Spotify")
                    .font(th.display(36))
                    .textCase(th.uppercase ? .uppercase : nil)
                    .foregroundStyle(th.fg)
                    .padding(.top, 8)
                if let d = charts.data {
                    Text("\(d.name) · \(d.date) · топ \(d.items.count)")
                        .font(.caption)
                        .foregroundStyle(th.muted)
                }

                HScroll(spacing: 8) {
                    ForEach(charts.charts) { c in
                        Chip(text: c.name, on: c.id == charts.country) {
                            Task { await charts.select(c.id) }
                        }
                    }
                }

                Text("Spotify не отдаёт сами песни, поэтому рядом с каждым треком мы ищем его на Audius. «Оригинал» значит, что артист выложил трек сам. «Версия» означает ремикс, кавер или перезалив.")
                    .font(.system(size: 13))
                    .foregroundStyle(th.muted)

                HStack(spacing: 12) {
                    PillButton(title: "Слушать то, что нашлось", icon: "play.fill", solid: true, busy: charts.playingAll) {
                        Task { await charts.playFound() }
                    }
                    .disabled(charts.data == nil || charts.playingAll)
                    if !charts.progress.isEmpty {
                        Text(charts.progress).font(.caption).foregroundStyle(th.muted)
                    }
                }

                if let e = charts.error {
                    StatusText(text: e, error: true)
                    PillButton(title: "Повторить") { Task { await charts.load(force: true) } }
                } else if charts.loading && charts.data == nil {
                    VStack(spacing: 0) { ForEach(0..<8, id: \.self) { _ in SkeletonRow() } }
                } else if let d = charts.data {
                    VStack(spacing: 0) {
                        ForEach(d.items) { it in
                            ChartRow(item: it)
                            Rectangle().fill(th.line).frame(height: 1)
                        }
                    }
                    .modifier(TrackListChrome())
                }
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 24)
        }
        .refreshable { await charts.load(force: true) }
        .screenBackground()
        .toolbar(.hidden, for: .navigationBar)
        .task { await charts.load() }
    }
}

struct ChartRow: View {
    @EnvironmentObject var app: AppModel
    @EnvironmentObject var charts: ChartsModel
    let item: ChartItem

    var body: some View {
        let th = app.theme
        let m = charts.match(for: item)
        let open = charts.expanded.contains(item.pos)
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 10) {
                Text("\(item.pos)")
                    .font(th.display(18).monospacedDigit())
                    .foregroundStyle(th.fg)
                    .frame(width: 34, alignment: .trailing)
                Text(item.change)
                    .font(.system(size: 10).monospacedDigit())
                    .foregroundStyle(th.muted)
                    .frame(width: 26)
                    .lineLimit(1)
                    .minimumScaleFactor(0.6)
                VStack(alignment: .leading, spacing: 2) {
                    Text(item.title).font(.system(size: 15, weight: .semibold)).foregroundStyle(th.fg).lineLimit(1)
                    Text(item.artists).font(.system(size: 13)).foregroundStyle(th.muted).lineLimit(1)
                    if !item.streamsText.isEmpty {
                        Text(item.streamsText).font(.system(size: 11).monospacedDigit()).foregroundStyle(th.muted)
                    }
                }
                Spacer(minLength: 4)
                Button {
                    Task { await charts.toggleExpanded(item) }
                } label: {
                    badge(m, th)
                }
                .buttonStyle(.plain)
                if let url = URL(string: "https://open.spotify.com/track/\(API.enc(item.spotify))"), !item.spotify.isEmpty {
                    Link(destination: url) {
                        Image(systemName: "arrow.up.right.square")
                            .font(.system(size: 18))
                            .foregroundStyle(th.muted)
                            .frame(width: 34, height: 34)
                    }
                    .accessibilityLabel("Открыть в Spotify")
                }
            }
            .padding(.vertical, 10)
            .padding(.horizontal, 6)

            if open {
                Group {
                    if let m = m {
                        if m.list.isEmpty {
                            StatusText(text: "На Audius этого трека нет. Оригинал есть только в Spotify.")
                        } else {
                            let list = Array(m.list.prefix(6))
                            VStack(spacing: 0) {
                                ForEach(Array(list.enumerated()), id: \.element.id) { pair in
                                    TrackRow(track: pair.element, index: pair.offset, list: list)
                                }
                            }
                        }
                    } else {
                        VStack(spacing: 0) { ForEach(0..<3, id: \.self) { _ in SkeletonRow() } }
                    }
                }
                .padding(.leading, 30)
                .padding(.bottom, 8)
            }
        }
    }

    @ViewBuilder
    private func badge(_ m: ChartMatch?, _ th: Theme) -> some View {
        let shape = RoundedRectangle(cornerRadius: th.pill ? 999 : th.buttonRadius, style: .continuous)
        let checking = charts.checking.contains(item.pos)
        let text = m?.label ?? (checking ? "Ищу…" : "Найти на Audius")
        let orig = m?.kind == "orig"
        let none = m?.kind == "none"
        Text(text)
            .font(.system(size: 12, weight: .medium))
            .lineLimit(1)
            .padding(.vertical, 6)
            .padding(.horizontal, 10)
            .foregroundStyle(orig ? th.bg : th.fg)
            .background(shape.fill(orig ? th.fg : Color.clear))
            .overlay(shape.stroke(m?.kind == "ver" || orig ? th.fg : th.line, lineWidth: th.border))
            .opacity(none ? 0.5 : 1)
            .fixedSize()
    }
}
