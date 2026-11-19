import SwiftUI

/// Round wave play button with the two expanding rings from the web.
struct VibeButton: View {
    @EnvironmentObject var app: AppModel
    let playing: Bool
    let busy: Bool
    let action: () -> Void

    var body: some View {
        let th = app.theme
        ZStack {
            TimelineView(.animation(minimumInterval: nil, paused: !playing)) { tl in
                let t = tl.date.timeIntervalSinceReferenceDate
                ZStack {
                    ForEach(0..<2, id: \.self) { i in
                        let phase = playing ? ((t / 2.4) + Double(i) * 0.5).truncatingRemainder(dividingBy: 1) : 0
                        Circle()
                            .stroke(th.fg, lineWidth: 1)
                            .scaleEffect(0.95 + 0.5 * phase)
                            .opacity(playing ? 0.5 * (1 - phase) : 0)
                    }
                }
            }
            .frame(width: 80, height: 80)
            PlayKnob(playing: playing, size: 72, busy: busy, action: action)
        }
        .frame(width: 92, height: 92)
    }
}

/// Wave settings panel («Настроить»).
struct WaveSettingsPanel: View {
    @EnvironmentObject var app: AppModel
    @EnvironmentObject var wave: Wave

    private var noveltyText: String {
        wave.cfg.novelty < 35 ? "больше любимого" : (wave.cfg.novelty > 65 ? "больше нового" : "поровну")
    }

    private var popularityText: String {
        wave.cfg.popularity < 35 ? "андеграунд" : (wave.cfg.popularity > 65 ? "хиты" : "всё подряд")
    }

    var body: some View {
        let th = app.theme
        VStack(alignment: .leading, spacing: 14) {
            row("Знакомое или новое", noveltyText) {
                Slider(value: $wave.cfg.novelty, in: 0...100, step: 5).tint(th.fg)
            }
            row("Андеграунд или хиты", popularityText) {
                Slider(value: $wave.cfg.popularity, in: 0...100, step: 5).tint(th.fg)
            }
            row("Темп", nil) {
                Picker("Темп", selection: $wave.cfg.tempo) {
                    Text("Любой").tag("any")
                    Text("Спокойный").tag("slow")
                    Text("Средний").tag("mid")
                    Text("Быстрый").tag("fast")
                }
                .pickerStyle(.segmented)
            }
            row("Вокал", nil) {
                Picker("Вокал", selection: $wave.cfg.vocals) {
                    Text("Любой").tag("any")
                    Text("Без слов").tag("instrumental")
                    Text("С вокалом").tag("vocal")
                }
                .pickerStyle(.segmented)
            }
            Toggle(isOn: $wave.cfg.onlyDl) {
                Text("Только треки, которые можно скачать")
                    .font(.system(size: 14))
                    .foregroundStyle(th.fg)
            }
            .tint(th.fg)
            HStack {
                Spacer()
                Button("Сбросить") { wave.resetSettings() }
                    .font(.system(size: 14, weight: .medium))
                    .foregroundStyle(th.muted)
                    .buttonStyle(.plain)
            }
        }
        .padding(16)
        .echoCard(radius: th.cardRadius)
    }

    @ViewBuilder
    private func row<C: View>(_ title: String, _ value: String?, @ViewBuilder control: () -> C) -> some View {
        let th = app.theme
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(title).font(.system(size: 13, weight: .medium)).foregroundStyle(th.fg)
                Spacer()
                if let value = value {
                    Text(value).font(.system(size: 12)).foregroundStyle(th.muted)
                }
            }
            control()
        }
    }
}

/// «Моя волна» hero card.
struct WaveHero: View {
    @EnvironmentObject var app: AppModel
    @EnvironmentObject var player: Player
    @EnvironmentObject var wave: Wave
    @EnvironmentObject var recs: Recs
    @State private var showSettings = false

    var body: some View {
        let th = app.theme
        let t = player.current
        let waveOn = player.mode == .wave && t != nil
        let playing = waveOn && player.isPlaying
        let next = player.mode == .wave ? player.upNext : nil

        VStack(alignment: .leading, spacing: 18) {
            HStack(alignment: .center) {
                Eyebrow(text: wave.eyebrow, pulsing: playing)
                Spacer()
                Button {
                    withAnimation(.easeInOut(duration: 0.2)) { showSettings.toggle() }
                } label: {
                    HStack(spacing: 6) {
                        Image(systemName: "slider.horizontal.3")
                        Text("Настроить")
                    }
                    .font(.system(size: 13, weight: .medium))
                    .foregroundStyle(showSettings ? th.fg : th.muted)
                    .padding(.vertical, 6)
                    .padding(.horizontal, 10)
                    .overlay(RoundedRectangle(cornerRadius: th.pill ? 999 : th.buttonRadius).stroke(showSettings ? th.fg : th.line, lineWidth: 1))
                }
                .buttonStyle(.plain)
            }

            HScroll(spacing: 8) {
                ForEach(MoodPreset.order, id: \.self) { m in
                    Chip(text: m, on: wave.mood == m) {
                        Task { await wave.selectMood(m) }
                    }
                }
            }

            if showSettings {
                WaveSettingsPanel()
                    .transition(.opacity.combined(with: .move(edge: .top)))
            }

            Text("Моя волна")
                .font(th.display(64))
                .textCase(th.uppercase ? .uppercase : nil)
                .foregroundStyle(th.fg)
                .lineLimit(1)
                .minimumScaleFactor(0.4)
                .padding(.top, 6)

            HStack(alignment: .center, spacing: 14) {
                VibeButton(playing: playing, busy: wave.starting) {
                    if waveOn {
                        player.toggle()
                    } else {
                        Task { await wave.start() }
                    }
                }
                VStack(alignment: .leading, spacing: 3) {
                    Text(waveOn ? (playing ? "СЕЙЧАС ИГРАЕТ" : "НА ПАУЗЕ") : "МОЯ ВОЛНА")
                        .font(.system(size: 10, weight: .semibold))
                        .tracking(1.4)
                        .foregroundStyle(th.muted)
                    Text(titleText(t: t, waveOn: waveOn))
                        .font(.system(size: 20, weight: .semibold))
                        .foregroundStyle(th.fg)
                        .lineLimit(2)
                    Text(subtitleText(t: t, waveOn: waveOn))
                        .font(.system(size: 14))
                        .foregroundStyle(th.muted)
                        .lineLimit(1)
                }
                Spacer(minLength: 0)
                if let t = t, waveOn {
                    CoverView(path: t.cover, seed: 3, radius: min(th.coverRadius, 22))
                        .frame(width: 104, height: 104)
                        .rotationEffect(.degrees(playing && !th.roundCovers ? -2 : 0))
                        .shadow(color: .black.opacity(0.35), radius: 20, y: 12)
                        .animation(.easeOut(duration: 0.4), value: playing)
                } else {
                    CoverPattern(seed: 3, fg: th.fg, bg: th.card2)
                        .frame(width: 104, height: 104)
                        .clipShape(RoundedRectangle(cornerRadius: min(th.coverRadius, 22), style: .continuous))
                }
            }

            if let t = t, waveOn {
                HStack(spacing: 8) {
                    Chip(text: "Нравится", icon: recs.isLiked(t) ? "heart.fill" : "heart", on: recs.isLiked(t)) {
                        let on = recs.toggleLike(t)
                        app.toast(on ? "Добавлено в любимое" : "Убрано из любимого")
                    }
                    Chip(text: "Не то", icon: "nosign") {
                        player.banCurrent()
                    }
                }
            }

            HStack(spacing: 12) {
                if let n = next {
                    CoverView(path: n.coverSm, seed: 5, radius: min(th.coverRadius, 10))
                        .frame(width: 44, height: 44)
                }
                VStack(alignment: .leading, spacing: 2) {
                    Text("ДАЛЬШЕ В ВОЛНЕ")
                        .font(.system(size: 10, weight: .semibold))
                        .tracking(1.4)
                        .foregroundStyle(th.muted)
                    Text(next?.title ?? "—")
                        .font(.system(size: 15, weight: .semibold))
                        .foregroundStyle(th.fg)
                        .lineLimit(1)
                    if let n = next {
                        Text(n.artist).font(.system(size: 13)).foregroundStyle(th.muted).lineLimit(1)
                    }
                }
                Spacer(minLength: 0)
            }
        }
        .padding(20)
        .echoCard(radius: th.radius * 2)
    }

    private func titleText(t: Track?, waveOn: Bool) -> String {
        if wave.starting { return "Подбираю треки…" }
        if let title = wave.failTitle, !waveOn { return title }
        if waveOn, let t = t { return t.title }
        return "Нажмите, чтобы начать"
    }

    private func subtitleText(t: Track?, waveOn: Bool) -> String {
        if let sub = wave.failSubtitle, !waveOn, !wave.starting { return sub }
        if waveOn, let t = t { return t.genre.isEmpty ? t.artist : "\(t.artist) · \(t.genre)" }
        if let m = wave.mood { return "Настроение: \(m)" }
        if let g = wave.focusGenre { return "Жанр: \(g)" }
        return "Подстроится под ваш вкус"
    }
}
