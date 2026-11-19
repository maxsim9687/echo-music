import Foundation

struct DownloadOption: Identifiable {
    let id: Int
    let label: String
    let note: String
    let fmt: String
    let url: String
    let convert: Bool
    let bits: Int
    /// Extension of the file that is fetched before converting.
    let sourceExt: String
}

/// Same choices as `downloadOptions` in download.js.
func downloadOptions(_ t: Track) -> [DownloadOption] {
    guard t.downloadable else { return [] }
    if t.src == "audius" {
        return [
            DownloadOption(id: 0, label: "MP3", note: "320 kbps, как у автора", fmt: "mp3", url: t.stream, convert: false, bits: 16, sourceExt: "mp3"),
            DownloadOption(id: 1, label: "WAV", note: "без потерь из MP3 320", fmt: "wav", url: t.stream, convert: true, bits: 16, sourceExt: "mp3"),
        ]
    }
    if t.src == "jamendo" {
        return [
            DownloadOption(id: 0, label: "FLAC", note: "оригинал без потерь", fmt: "flac", url: t.dlOrig, convert: false, bits: 24, sourceExt: "flac"),
            DownloadOption(id: 1, label: "WAV", note: "из FLAC, 24 бит", fmt: "wav", url: t.dlOrig, convert: true, bits: 24, sourceExt: "flac"),
            DownloadOption(id: 2, label: "MP3", note: "для телефона", fmt: "mp3", url: t.dlMp3, convert: false, bits: 16, sourceExt: "mp3"),
        ]
    }
    if t.src == "file" { return [] }
    let ext = (t.ext.isEmpty ? "mp3" : t.ext).lowercased()
    var out = [DownloadOption(id: 0, label: ext.uppercased(), note: "оригинал, как загружен", fmt: ext, url: t.dlOrig, convert: false, bits: 16, sourceExt: ext)]
    if ext != "wav" {
        out.append(DownloadOption(id: 1, label: "WAV", note: "из \(ext.uppercased())", fmt: "wav", url: t.stream, convert: true, bits: ext == "mp3" ? 16 : 24, sourceExt: ext))
    }
    return out
}

struct DownloadedItem: Identifiable {
    var file: String
    var track: Track
    var fmt: String
    var at: Double

    var id: String { file }
}

/// Files saved into the app's Documents folder (visible in the Files app) and played offline.
@MainActor
final class Downloads: ObservableObject {
    static let shared = Downloads()

    @Published private(set) var items: [DownloadedItem] = []
    @Published private(set) var active: Set<String> = []

    static let audioExts: Set<String> = ["mp3", "wav", "flac", "m4a", "aac", "aif", "aiff", "caf", "alac"]

    private init() {
        reload()
    }

    var docs: URL {
        FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0]
    }

    private var indexURL: URL {
        let dir = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir.appendingPathComponent("echo.downloads.json")
    }

    func fileURL(_ item: DownloadedItem) -> URL {
        docs.appendingPathComponent(item.file)
    }

    /// Reads the index and adds audio files the user dropped into Documents through the Files app.
    func reload() {
        var list: [DownloadedItem] = []
        if let data = try? Data(contentsOf: indexURL) {
            for j in JSON.parse(data).array {
                let file = j["file"].str
                if file.isEmpty { continue }
                list.append(DownloadedItem(file: file, track: Track(json: j["track"]), fmt: j["fmt"].str, at: j["at"].num))
            }
        }
        let fm = FileManager.default
        list = list.filter { fm.fileExists(atPath: docs.appendingPathComponent($0.file).path) }
        let known = Set(list.map { $0.file })
        let names = (try? fm.contentsOfDirectory(atPath: docs.path)) ?? []
        for name in names.sorted() where !known.contains(name) {
            let ext = (name as NSString).pathExtension.lowercased()
            guard Downloads.audioExts.contains(ext) else { continue }
            var t = Track()
            t.id = "fi:" + name
            t.src = "file"
            t.rawId = name
            let base = (name as NSString).deletingPathExtension
            let parts = base.components(separatedBy: " - ")
            if parts.count > 1 {
                t.artist = parts[0].trimmingCharacters(in: .whitespaces)
                t.title = parts.dropFirst().joined(separator: " - ").trimmingCharacters(in: .whitespaces)
            } else {
                t.artist = "Файл"
                t.title = base
            }
            t.artistId = "fi:" + t.artist.lowercased()
            t.quality = ext.uppercased()
            t.ext = ext
            let attrs = try? fm.attributesOfItem(atPath: docs.appendingPathComponent(name).path)
            let date = (attrs?[.modificationDate] as? Date) ?? Date()
            list.append(DownloadedItem(file: name, track: t, fmt: ext, at: date.timeIntervalSince1970 * 1000))
        }
        items = list.sorted { $0.at > $1.at }
    }

    private func saveIndex() {
        let arr: [[String: Any]] = items.filter { $0.track.src != "file" }.map {
            ["file": $0.file, "track": $0.track.jsonObject, "fmt": $0.fmt, "at": $0.at]
        }
        if let data = JSONOut.data(arr) {
            try? data.write(to: indexURL, options: .atomic)
        }
    }

    /// Offline file for this track, if one that AVPlayer can play was downloaded.
    func playableURL(for t: Track) -> URL? {
        if t.src == "file" { return docs.appendingPathComponent(t.rawId) }
        let ordered = items.filter { $0.track.id == t.id }.sorted { a, b in
            // prefer compressed originals over big WAVs, any is fine
            (a.fmt == "wav" ? 1 : 0) < (b.fmt == "wav" ? 1 : 0)
        }
        for it in ordered {
            let u = fileURL(it)
            if FileManager.default.fileExists(atPath: u.path) { return u }
        }
        return nil
    }

    func item(for t: Track) -> DownloadedItem? {
        items.first { $0.track.id == t.id }
    }

    var tracks: [Track] { items.map { $0.track } }

    static func safe(_ s: String) -> String {
        let bad = CharacterSet(charactersIn: "\\/:*?\"<>|")
        let cleaned = s.components(separatedBy: bad).joined().trimmingCharacters(in: .whitespacesAndNewlines)
        let cut = String(cleaned.prefix(150))
        return cut.isEmpty ? "track" : cut
    }

    func download(_ t: Track, _ o: DownloadOption) async {
        let app = AppModel.shared
        let key = t.id + "|" + o.fmt
        if active.contains(key) { return }
        active.insert(key)
        defer { active.remove(key) }
        let toastId = app.progressToast("Готовлю \(o.label): \(t.title)")
        let name = "\(Downloads.safe(t.artist)) - \(Downloads.safe(t.title)).\(o.fmt)"
        let dest = docs.appendingPathComponent(name)
        do {
            guard let url = API.url(o.url) else { throw APIError(message: "Нет ссылки на файл", status: 0) }
            let convert = o.convert
            let tmp = try await Transfer.download(url) { p in
                AppModel.shared.updateToast(toastId, progress: convert ? p * 0.8 : p)
            }
            let fm = FileManager.default
            if fm.fileExists(atPath: dest.path) { try fm.removeItem(at: dest) }
            if o.convert {
                let src = tmp.appendingPathExtension(o.sourceExt)
                try fm.moveItem(at: tmp, to: src)
                app.updateToast(toastId, text: "Перевожу в WAV: \(t.title)", progress: 0.85)
                let bits = o.bits
                do {
                    try await Task.detached(priority: .userInitiated) {
                        try await WavConverter.convert(src, to: dest, bits: bits)
                    }.value
                } catch {
                    try? fm.removeItem(at: src)
                    throw error
                }
                try? fm.removeItem(at: src)
            } else {
                try fm.moveItem(at: tmp, to: dest)
            }
            var saved = Recs.slim(t)
            saved.at = nil
            items.removeAll { $0.file == name }
            items.insert(DownloadedItem(file: name, track: saved, fmt: o.fmt, at: nowMs()), at: 0)
            saveIndex()
            app.updateToast(toastId, text: "Скачано: \(o.label) · в «Скачанном» и в приложении «Файлы»", progress: 1)
            app.closeToast(toastId, after: 3)
        } catch {
            let msg = (error as? WavError) != nil
                ? "Не получилось перевести в WAV. Скачайте оригинал."
                : "Не получилось скачать. Попробуйте другой формат."
            app.updateToast(toastId, text: msg, hideProgress: true)
            app.closeToast(toastId, after: 5)
        }
    }

    func delete(_ item: DownloadedItem) {
        try? FileManager.default.removeItem(at: fileURL(item))
        items.removeAll { $0.file == item.file }
        saveIndex()
    }
}
