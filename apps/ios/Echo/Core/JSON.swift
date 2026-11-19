import Foundation

/// Lenient read-only view over `JSONSerialization` output. Every accessor tolerates missing keys,
/// nulls and numbers sent as strings (and the other way round).
struct JSON: @unchecked Sendable {
    let raw: Any?

    init(_ raw: Any?) {
        self.raw = raw
    }

    static func parse(_ data: Data) -> JSON {
        if data.isEmpty { return JSON(nil) }
        return JSON(try? JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed]))
    }

    subscript(key: String) -> JSON {
        JSON((raw as? [String: Any])?[key])
    }

    var array: [JSON] {
        guard let list = raw as? [Any] else { return [] }
        return list.map { JSON($0) }
    }

    var object: [String: JSON] {
        guard let dict = raw as? [String: Any] else { return [:] }
        var out: [String: JSON] = [:]
        for (k, v) in dict { out[k] = JSON(v) }
        return out
    }

    var string: String? {
        if let s = raw as? String { return s }
        if let n = raw as? NSNumber {
            let d = n.doubleValue
            if d.isFinite && d == d.rounded() && abs(d) < 1e15 { return String(Int64(d)) }
            return n.stringValue
        }
        return nil
    }

    var str: String { string ?? "" }

    var double: Double? {
        if let n = raw as? NSNumber {
            let d = n.doubleValue
            return d.isFinite ? d : nil
        }
        if let s = raw as? String, let d = Double(s.trimmingCharacters(in: .whitespaces)), d.isFinite { return d }
        return nil
    }

    var num: Double { double ?? 0 }

    var int: Int? {
        guard let d = double, abs(d) < 1e15 else { return nil }
        return Int(d)
    }

    var bool: Bool {
        if let n = raw as? NSNumber { return n.boolValue }
        if let s = raw as? String { return s == "true" || s == "1" }
        return false
    }

    var exists: Bool {
        guard let r = raw else { return false }
        return !(r is NSNull)
    }

    var isObject: Bool { raw is [String: Any] }

    var strings: [String] {
        array.compactMap { $0.string }
    }
}

enum JSONOut {
    /// Serializes a JSON-compatible value; returns nil instead of raising for NaN or invalid input.
    static func data(_ value: Any) -> Data? {
        guard JSONSerialization.isValidJSONObject(value) else { return nil }
        return try? JSONSerialization.data(withJSONObject: value, options: [])
    }
}
