import Foundation

struct APIError: LocalizedError {
    let message: String
    let status: Int

    var errorDescription: String? { message }
}

/// Client for the site's JSON API. Cookies (`sid`) live in `HTTPCookieStorage.shared`, which persists them.
final class API: @unchecked Sendable {
    static let shared = API()
    static let base = URL(string: "https://music.maxsim.space")!

    let session: URLSession
    private let lock = NSLock()
    private var cache: [String: (Date, JSON)] = [:]

    private init() {
        let cfg = URLSessionConfiguration.default
        cfg.httpCookieStorage = HTTPCookieStorage.shared
        cfg.httpCookieAcceptPolicy = .always
        cfg.httpShouldSetCookies = true
        cfg.timeoutIntervalForRequest = 15
        cfg.requestCachePolicy = .useProtocolCachePolicy
        session = URLSession(configuration: cfg)
    }

    // MARK: URLs

    static func url(_ path: String) -> URL? {
        if path.isEmpty { return nil }
        if path.hasPrefix("http://") || path.hasPrefix("https://") || path.hasPrefix("file:") {
            return URL(string: path)
        }
        return URL(string: path, relativeTo: base)?.absoluteURL
    }

    private static let unreserved = CharacterSet(charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")

    static func enc(_ s: String) -> String {
        s.addingPercentEncoding(withAllowedCharacters: unreserved) ?? ""
    }

    /// Builds "path?k=v&…" with strict percent-encoding (so "+" survives as %2B like URLSearchParams).
    static func path(_ p: String, _ items: [(String, String?)]) -> String {
        var parts: [String] = []
        for item in items {
            if let v = item.1 { parts.append(enc(item.0) + "=" + enc(v)) }
        }
        return parts.isEmpty ? p : p + "?" + parts.joined(separator: "&")
    }

    // MARK: cache (sync helpers so no lock is taken across an await)

    private func cached(_ key: String, ttl: TimeInterval) -> JSON? {
        lock.lock()
        defer { lock.unlock() }
        guard let hit = cache[key] else { return nil }
        if Date().timeIntervalSince(hit.0) < ttl { return hit.1 }
        cache[key] = nil
        return nil
    }

    private func store(_ key: String, _ value: JSON) {
        lock.lock()
        defer { lock.unlock() }
        if cache.count > 300 { cache.removeAll() }
        cache[key] = (Date(), value)
    }

    func clearCache() {
        lock.lock()
        defer { lock.unlock() }
        cache.removeAll()
    }

    // MARK: requests

    func get(_ path: String, ttl: TimeInterval = 0) async throws -> JSON {
        if ttl > 0, let hit = cached(path, ttl: ttl) { return hit }
        let value = try await request(path, method: "GET", body: nil, contentType: nil)
        if ttl > 0 { store(path, value) }
        return value
    }

    func send(_ path: String, method: String = "POST", json: Any? = nil) async throws -> JSON {
        var body: Data? = nil
        if let json = json {
            guard let d = JSONOut.data(json) else { throw APIError(message: "Не удалось подготовить данные", status: 0) }
            body = d
        }
        return try await request(path, method: method, body: body, contentType: body == nil ? nil : "application/json")
    }

    func request(_ path: String, method: String, body: Data?, contentType: String?) async throws -> JSON {
        guard let url = API.url(path) else { throw APIError(message: "Неверный адрес", status: 0) }
        var req = URLRequest(url: url, timeoutInterval: 15)
        req.httpMethod = method
        req.setValue("1", forHTTPHeaderField: "X-Echo")
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        if let contentType = contentType { req.setValue(contentType, forHTTPHeaderField: "Content-Type") }
        req.httpBody = body
        let result: (Data, URLResponse)
        do {
            result = try await session.data(for: req)
        } catch {
            throw APIError(message: API.networkMessage(error), status: 0)
        }
        let code = (result.1 as? HTTPURLResponse)?.statusCode ?? 0
        let json = JSON.parse(result.0)
        if code < 200 || code >= 300 {
            let msg = json["error"].string ?? "HTTP \(code)"
            throw APIError(message: msg, status: code)
        }
        return json
    }

    static func networkMessage(_ error: Error) -> String {
        if let u = error as? URLError {
            switch u.code {
            case .timedOut: return "Сервер не ответил вовремя"
            case .cancelled: return "Запрос отменён"
            default: return "Сеть недоступна"
            }
        }
        return error.localizedDescription
    }

    /// Error text from an upload / transfer response body.
    static func errorText(_ data: Data, status: Int, fallback: String) -> String {
        JSON.parse(data)["error"].string ?? (status > 0 ? "\(fallback) (HTTP \(status))" : fallback)
    }
}
