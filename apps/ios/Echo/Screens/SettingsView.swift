import SwiftUI

/// Настройки: account, upload (admin), 10 themes, taste bars, sources.
struct SettingsView: View {
    @EnvironmentObject var app: AppModel
    @EnvironmentObject var recs: Recs
    @EnvironmentObject var wave: Wave
    @State private var resetArmed = false
    @State private var resetNote = ""
    @State private var updateNote = ""
    @State private var updateBuild = 0
    @Environment(\.openURL) private var openURL

    private var currentBuild: Int { Int(Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "") ?? 0 }

    var body: some View {
        let th = app.theme
        ScrollView {
            VStack(alignment: .leading, spacing: 32) {
                Text("Настройки")
                    .font(th.display(40))
                    .textCase(th.uppercase ? .uppercase : nil)
                    .foregroundStyle(th.fg)
                    .padding(.top, 8)

                account(th)

                updates(th)

                VStack(alignment: .leading, spacing: 14) {
                    SectionHeader(title: "Оформление")
                    LazyVGrid(columns: [GridItem(.flexible(), spacing: 10), GridItem(.flexible(), spacing: 10)], spacing: 10) {
                        ForEach(Theme.all) { t in
                            themeButton(t, th)
                        }
                    }
                }

                VStack(alignment: .leading, spacing: 14) {
                    SectionHeader(title: "Ваш вкус", note: "так волна видит вас сейчас")
                    bars("Жанры", recs.tasteBars(recs.P.g), th) { $0 }
                    bars("Настроения", recs.tasteBars(recs.P.m), th) { $0 }
                    bars("Артисты", recs.tasteBars(recs.P.a), th) { recs.P.an[$0] ?? "артист" }
                    FlowLayout(spacing: 8) {
                        ForEach(STARTER_GENRES, id: \.self) { g in
                            let on = (recs.P.g[g] ?? 0) > 0
                            Chip(text: g, on: on) {
                                recs.seedGenre(g, on: !on)
                                wave.resetPool()
                            }
                        }
                    }
                    HStack(spacing: 10) {
                        NavigationLink {
                            TasteSetupView()
                        } label: {
                            Text("Настроить вкус")
                                .font(.system(size: 15, weight: .semibold))
                                .foregroundStyle(th.bg)
                                .padding(.vertical, 11)
                                .padding(.horizontal, 18)
                                .background(RoundedRectangle(cornerRadius: th.pill ? 999 : th.buttonRadius, style: .continuous).fill(th.fg))
                        }
                        .buttonStyle(.plain)
                        PillButton(title: resetArmed ? "Точно сбросить?" : "Сбросить вкус") { resetTaste() }
                    }
                    if !resetNote.isEmpty {
                        Text(resetNote).font(.caption).foregroundStyle(th.muted)
                    }
                }

                VStack(alignment: .leading, spacing: 14) {
                    SectionHeader(title: "Источники музыки")
                    sourceRow("Audius", "Подключён. Полные треки, MP3 320 kbps. Скачивание там, где автор разрешил.", th)
                    sourceRow("Jamendo", app.jamendo ? "Подключён. Скачивание FLAC без потерь." : "Не подключён. Нужен бесплатный Client ID в настройках сервера.", th)
                    sourceRow("Ваша музыка", "Файлы, которые загружает администратор. Хранятся без перекодирования.", th)
                }

                VStack(alignment: .leading, spacing: 4) {
                    Text("Эхо для iOS").font(.system(size: 13, weight: .semibold)).foregroundStyle(th.fg)
                    Text("Полные треки независимых артистов из Audius и Jamendo, плюс ваша собственная музыка.")
                        .font(.system(size: 12))
                        .foregroundStyle(th.muted)
                }
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 30)
        }
        .screenBackground()
        .toolbar(.hidden, for: .navigationBar)
    }

    /// iOS does not let an app replace itself, so here it only checks: a newer build opens the install guide.
    @ViewBuilder
    private func updates(_ th: Theme) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            SectionHeader(title: "Обновления")
            Text("Версия 1.0.\(currentBuild). На iPhone приложение не может обновить себя само: проверьте новую версию и поставьте её тем же способом, что и в первый раз.")
                .font(.system(size: 14))
                .foregroundStyle(th.muted)
            if !updateNote.isEmpty {
                Text(updateNote).font(.system(size: 14, weight: .semibold)).foregroundStyle(th.fg)
            }
            HStack(spacing: 10) {
                PillButton(title: "Проверить обновления", icon: "arrow.triangle.2.circlepath") { Task { await checkUpdate() } }
                if updateBuild > 0 {
                    PillButton(title: "Скачать 1.0.\(updateBuild)", icon: "arrow.down.circle", solid: true) {
                        if let u = URL(string: "https://music.maxsim.space/apps#ios") { openURL(u) }
                    }
                }
            }
        }
    }

    private func checkUpdate() async {
        updateNote = "Проверяю…"
        guard let all = try? await API.shared.get("/api/apps"),
              let tag = all["ios"]["tag"].string,
              let build = Int(tag.replacingOccurrences(of: "apps-", with: "")) else {
            updateNote = "Не получилось проверить, попробуйте позже"
            return
        }
        if build > currentBuild {
            updateBuild = build
            updateNote = "Есть новая версия 1.0.\(build)"
        } else {
            updateBuild = 0
            updateNote = "У вас последняя версия"
        }
    }

    @ViewBuilder
    private func account(_ th: Theme) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            SectionHeader(title: "Аккаунт")
            if let me = app.me {
                HStack(spacing: 12) {
                    Image(systemName: "person.crop.circle.fill").font(.system(size: 36)).foregroundStyle(th.fg)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(me.email).font(.system(size: 15, weight: .semibold)).foregroundStyle(th.fg).lineLimit(1)
                        Text(me.roleText).font(.system(size: 13)).foregroundStyle(th.muted)
                    }
                    Spacer()
                    PillButton(title: "Выйти") { Task { await app.logout() } }
                }
                .padding(14)
                .echoCard(radius: th.cardRadius)
                if me.isAdmin {
                    NavigationLink {
                        UploadView()
                    } label: {
                        HStack(spacing: 12) {
                            Image(systemName: "square.and.arrow.up").font(.system(size: 18))
                            VStack(alignment: .leading, spacing: 2) {
                                Text("Загрузка").font(.system(size: 15, weight: .semibold))
                                Text("Загрузить свою музыку и обложки").font(.system(size: 12)).foregroundStyle(th.muted)
                            }
                            Spacer()
                            Image(systemName: "chevron.right").foregroundStyle(th.muted)
                        }
                        .foregroundStyle(th.fg)
                        .padding(14)
                        .echoCard(radius: th.cardRadius)
                    }
                    .buttonStyle(.plain)
                }
            } else {
                Text("Войдите, чтобы вкус, любимое и плейлисты сохранялись в аккаунте и совпадали с сайтом.")
                    .font(.system(size: 14))
                    .foregroundStyle(th.muted)
                PillButton(title: "Войти по почте", icon: "envelope", solid: true) { app.showLogin = true }
            }
        }
    }

    @ViewBuilder
    private func themeButton(_ t: Theme, _ th: Theme) -> some View {
        let on = t.id == app.themeIndex
        let shape = RoundedRectangle(cornerRadius: min(th.radius, 16), style: .continuous)
        Button {
            withAnimation(.easeInOut(duration: 0.3)) { app.themeIndex = t.id }
        } label: {
            VStack(alignment: .leading, spacing: 8) {
                HStack(spacing: 4) {
                    Circle().fill(Color(hex: t.fgHex)).frame(width: 14, height: 14)
                    Circle().fill(Color(hex: t.fgHex).opacity(0.4)).frame(width: 14, height: 14)
                    Spacer()
                }
                .padding(8)
                .frame(height: 64, alignment: .bottomLeading)
                .background(RoundedRectangle(cornerRadius: min(th.radius, 10), style: .continuous).fill(Color(hex: t.bgHex)))
                .overlay(RoundedRectangle(cornerRadius: min(th.radius, 10), style: .continuous).stroke(th.line, lineWidth: 1))
                Text("\(t.id). \(t.name)")
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundStyle(th.fg)
                Text(t.id == 10 ? "по умолчанию" : " ")
                    .font(.system(size: 12))
                    .foregroundStyle(th.muted)
            }
            .padding(10)
            .overlay(shape.stroke(on ? th.fg : th.line, lineWidth: th.border))
            .contentShape(shape)
        }
        .buttonStyle(.plain)
    }

    @ViewBuilder
    private func bars(_ title: String, _ pairs: [(String, Double)], _ th: Theme, label: @escaping (String) -> String) -> some View {
        let items: [(key: String, value: Double)] = pairs.map { (key: $0.0, value: $0.1) }
        let maxV = items.first?.value ?? 1
        VStack(alignment: .leading, spacing: 8) {
            Text(title).font(.system(size: 15, weight: .semibold)).foregroundStyle(th.fg)
            if items.isEmpty {
                Text("Пока пусто: послушайте и полайкайте пару треков").font(.system(size: 13)).foregroundStyle(th.muted)
            } else {
                ForEach(items, id: \.key) { item in
                    HStack(spacing: 10) {
                        Text(label(item.key))
                            .font(.system(size: 13))
                            .foregroundStyle(th.fg)
                            .lineLimit(1)
                            .frame(width: 110, alignment: .leading)
                        GeometryReader { geo in
                            Capsule()
                                .fill(th.fg)
                                .frame(width: max(6, geo.size.width * CGFloat(item.value / max(maxV, 0.001))), height: 6)
                                .frame(maxHeight: .infinity, alignment: .center)
                        }
                        .frame(height: 14)
                        Text(String(format: "%.1f", item.value))
                            .font(.system(size: 12).monospacedDigit())
                            .foregroundStyle(th.muted)
                            .frame(width: 36, alignment: .trailing)
                    }
                }
            }
        }
    }

    @ViewBuilder
    private func sourceRow(_ name: String, _ text: String, _ th: Theme) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(name).font(.system(size: 15, weight: .semibold)).foregroundStyle(th.fg)
            Text(text).font(.system(size: 13)).foregroundStyle(th.muted)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(14)
        .echoCard(radius: th.cardRadius)
    }

    private func resetTaste() {
        if !resetArmed {
            resetArmed = true
            Task { @MainActor in
                try? await Task.sleep(nanoseconds: 4_000_000_000)
                resetArmed = false
            }
            return
        }
        recs.resetProfile()
        wave.resetPool()
        resetArmed = false
        resetNote = "Вкус сброшен. Любимое и плейлисты остались."
    }
}
