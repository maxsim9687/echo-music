import Foundation
import SwiftUI

enum AppTab: Hashable {
    case home
    case search
    case charts
    case library
    case settings
}

struct ToastItem: Identifiable, Equatable {
    let id: UUID
    var text: String
    var progress: Double?
}

/// Account, theme, toasts and app-wide presentation state.
@MainActor
final class AppModel: ObservableObject {
    static let shared = AppModel()

    @Published var me: Me? = nil
    @Published var jamendo = false
    @Published var toasts: [ToastItem] = []
    @Published var themeIndex: Int {
        didSet { UserDefaults.standard.set(themeIndex, forKey: "echo.theme") }
    }
    @Published var tab: AppTab = .home
    @Published var showFullPlayer = false
    @Published var showLogin = false
    @Published var showTasteSetup = false
    /// Track whose download menu is open.
    @Published var dlTarget: Track? = nil

    private var pushTask: Task<Void, Never>? = nil
    private var syncWarned = false
    private var booted = false

    private init() {
        let v = UserDefaults.standard.integer(forKey: "echo.theme")
        themeIndex = (1...10).contains(v) ? v : 10
    }

    var theme: Theme { Theme.all[max(0, min(Theme.all.count - 1, themeIndex - 1))] }

    func boot() async {
        if booted { return }
        booted = true
        Downloads.shared.reload()
        await refreshMe()
    }

    // MARK: toasts

    func toast(_ text: String, seconds: Double = 3.8) {
        let item = ToastItem(id: UUID(), text: text, progress: nil)
        toasts.append(item)
        if toasts.count > 4 { toasts.removeFirst(toasts.count - 4) }
        closeToast(item.id, after: seconds)
    }

    func progressToast(_ text: String) -> UUID {
        let item = ToastItem(id: UUID(), text: text, progress: 0)
        toasts.append(item)
        return item.id
    }

    func updateToast(_ id: UUID, text: String? = nil, progress: Double? = nil, hideProgress: Bool = false) {
        guard let i = toasts.firstIndex(where: { $0.id == id }) else { return }
        if let text = text { toasts[i].text = text }
        if let p = progress { toasts[i].progress = max(0, min(1, p)) }
        if hideProgress { toasts[i].progress = nil }
    }

    func closeToast(_ id: UUID, after seconds: Double = 0) {
        Task { @MainActor in
            if seconds > 0 { try? await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000)) }
            AppModel.shared.toasts.removeAll { $0.id == id }
        }
    }

    // MARK: account

    func refreshMe() async {
        do {
            let d = try await API.shared.get("/api/me")
            let u = d["user"]
            if u.isObject {
                me = Me(email: u["email"].str, role: u["role"].str)
            } else {
                me = nil
            }
            jamendo = d["jamendo"].bool
            Sources.jamendoOn = jamendo
        } catch {
            // offline: keep what we had
        }
        if me != nil { await pullProfile() }
    }

    func pullProfile() async {
        do {
            let d = try await API.shared.get("/api/profile")
            if d["data"].isObject {
                Recs.shared.merge(server: Profile(json: d["data"]))
            }
            pushProfile()
        } catch {
            // not fatal
        }
    }

    /// Debounced PUT /api/profile (2.5 s), like pushProfile in app.js.
    func pushProfile() {
        guard me != nil else { return }
        pushTask?.cancel()
        pushTask = Task { @MainActor in
            try? await Task.sleep(nanoseconds: 2_500_000_000)
            if Task.isCancelled { return }
            let model = AppModel.shared
            guard model.me != nil else { return }
            let body: [String: Any] = ["data": Recs.shared.P.jsonObject]
            do {
                _ = try await API.shared.send("/api/profile", method: "PUT", json: body)
                model.syncWarned = false
            } catch {
                if !model.syncWarned {
                    model.syncWarned = true
                    model.toast("Не удалось сохранить вкус в аккаунт: \(error.localizedDescription)")
                }
            }
        }
    }

    func requestCode(email: String) async throws {
        _ = try await API.shared.send("/api/auth/code", json: ["email": email])
    }

    func verify(email: String, code: String) async throws {
        let d = try await API.shared.send("/api/auth/verify", json: ["email": email, "code": code])
        let role = d["user"]["role"].str
        toast(role == "admin" ? "Вы вошли как администратор" : "Вы вошли")
        await refreshMe()
        Wave.shared.resetPool()
        if !Recs.shared.hasTaste {
            // let the login sheet finish closing before the next one opens
            Task { @MainActor in
                try? await Task.sleep(nanoseconds: 800_000_000)
                AppModel.shared.showTasteSetup = true
            }
        }
    }

    func logout() async {
        _ = try? await API.shared.send("/api/auth/logout")
        pushTask?.cancel()
        me = nil
        if let cookies = HTTPCookieStorage.shared.cookies(for: API.base) {
            for c in cookies where c.name == "sid" { HTTPCookieStorage.shared.deleteCookie(c) }
        }
        Recs.shared.clearProfile()
        Wave.shared.resetPool()
        API.shared.clearCache()
        await refreshMe()
        toast("Вы вышли")
        tab = .home
    }
}
