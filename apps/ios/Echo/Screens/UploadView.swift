import SwiftUI
import PhotosUI
import UniformTypeIdentifiers
import AVFoundation
import UIKit

/// Загрузка (admin): audio file + optional cover, metadata, upload with progress, list with delete.
struct UploadView: View {
    @EnvironmentObject var app: AppModel
    @EnvironmentObject var wave: Wave
    @EnvironmentObject var home: HomeModel

    private enum ImportMode {
        case audio
        case cover
    }

    @State private var audioURL: URL? = nil
    @State private var audioName = ""
    @State private var audioSize: Int64 = 0
    @State private var coverData: Data? = nil
    @State private var photoItem: PhotosPickerItem? = nil
    @State private var importing = false
    @State private var importMode: ImportMode = .audio
    @State private var title = ""
    @State private var artist = ""
    @State private var genre = ""
    @State private var mood = ""
    @State private var bpm = ""
    @State private var note = ""
    @State private var busy = false
    @State private var uploads: [Track] = []
    @State private var listLoading = false
    @State private var confirmDelete: Track? = nil

    var body: some View {
        let th = app.theme
        ScrollView {
            VStack(alignment: .leading, spacing: 24) {
                VStack(alignment: .leading, spacing: 6) {
                    Text("Загрузить музыку")
                        .font(th.display(32))
                        .foregroundStyle(th.fg)
                    Text("WAV, FLAC и MP3 хранятся без перекодирования")
                        .font(.caption)
                        .foregroundStyle(th.muted)
                }
                .padding(.top, 8)

                Button {
                    importMode = .audio
                    importing = true
                } label: {
                    VStack(spacing: 8) {
                        Image(systemName: "square.and.arrow.up").font(.system(size: 26))
                        Text(audioName.isEmpty ? "Выберите аудиофайл" : "\(audioName) · \(String(format: "%.1f", Double(audioSize) / 1_048_576)) МБ")
                            .font(.system(size: 15, weight: .semibold))
                            .multilineTextAlignment(.center)
                        Text("до 500 МБ").font(.caption).foregroundStyle(th.muted)
                    }
                    .foregroundStyle(th.fg)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 28)
                    .overlay(RoundedRectangle(cornerRadius: th.cardRadius, style: .continuous)
                        .stroke(th.line, style: StrokeStyle(lineWidth: max(1, th.border), dash: [6, 5])))
                }
                .buttonStyle(.plain)

                VStack(spacing: 12) {
                    field("Название", text: $title, placeholder: "")
                    field("Исполнитель", text: $artist, placeholder: "")
                    suggestField("Жанр", text: $genre, placeholder: "Lo-Fi", options: GENRE_LIST)
                    suggestField("Настроение", text: $mood, placeholder: "Peaceful", options: MOOD_LIST)
                    field("Темп, BPM", text: $bpm, placeholder: "", numeric: true)
                }

                VStack(alignment: .leading, spacing: 10) {
                    Text("Обложка").font(.system(size: 13, weight: .medium)).foregroundStyle(th.muted)
                    HStack(spacing: 12) {
                        if let d = coverData, let img = UIImage(data: d) {
                            Image(uiImage: img)
                                .resizable()
                                .scaledToFill()
                                .frame(width: 64, height: 64)
                                .clipShape(RoundedRectangle(cornerRadius: min(th.coverRadius, 10)))
                        }
                        PhotosPicker(selection: $photoItem, matching: .images) {
                            Label("Из фото", systemImage: "photo")
                                .font(.system(size: 14, weight: .medium))
                                .foregroundStyle(th.fg)
                                .padding(.vertical, 9)
                                .padding(.horizontal, 14)
                                .overlay(RoundedRectangle(cornerRadius: th.pill ? 999 : th.buttonRadius).stroke(th.line, lineWidth: th.border))
                        }
                        Chip(text: "Из файлов", icon: "folder") {
                            importMode = .cover
                            importing = true
                        }
                        if coverData != nil {
                            IconButton(system: "xmark.circle", size: 18) {
                                coverData = nil
                                photoItem = nil
                            }
                        }
                    }
                }

                HStack(spacing: 12) {
                    PillButton(title: "Загрузить", icon: "arrow.up", solid: true, busy: busy) { upload() }
                        .disabled(busy)
                    if !note.isEmpty {
                        Text(note).font(.system(size: 13)).foregroundStyle(th.fg)
                    }
                }

                VStack(alignment: .leading, spacing: 12) {
                    SectionHeader(title: "Загруженные треки", note: uploads.isEmpty ? nil : "\(uploads.count)")
                    if uploads.isEmpty && !listLoading {
                        StatusText(text: "Вы ещё ничего не загрузили")
                    } else {
                        VStack(spacing: 0) {
                            ForEach(Array(uploads.enumerated()), id: \.element.id) { pair in
                                TrackRow(track: pair.element, index: pair.offset, list: uploads, trailing: AnyView(
                                    IconButton(system: "trash", size: 16) { confirmDelete = pair.element }
                                ))
                                Rectangle().fill(th.line).frame(height: 1)
                            }
                        }
                        .modifier(TrackListChrome())
                    }
                }
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 30)
        }
        .scrollDismissesKeyboard(.interactively)
        .screenBackground()
        .navigationTitle("Загрузка")
        .navigationBarTitleDisplayMode(.inline)
        .fileImporter(isPresented: $importing,
                      allowedContentTypes: importMode == .audio ? [UTType.audio] : [UTType.jpeg, UTType.png, UTType.webP, UTType.image]) { result in
            switch result {
            case .success(let url):
                if importMode == .audio { pickAudio(url) } else { pickCover(url) }
            case .failure:
                note = "Файл не открылся"
            }
        }
        .onChange(of: photoItem) { _, item in
            guard let item = item else { return }
            Task { @MainActor in
                let data = try? await item.loadTransferable(type: Data.self)
                if let data = data { coverData = UploadView.jpeg(data) }
            }
        }
        .confirmationDialog("Удалить трек?", isPresented: Binding(get: { confirmDelete != nil }, set: { if !$0 { confirmDelete = nil } }),
                            titleVisibility: .visible, presenting: confirmDelete) { t in
            Button("Удалить «\(t.title)»", role: .destructive) { delete(t) }
            Button("Отмена", role: .cancel) {}
        }
        .task { await reloadList() }
    }

    // MARK: fields

    @ViewBuilder
    private func field(_ label: String, text: Binding<String>, placeholder: String, numeric: Bool = false) -> some View {
        let th = app.theme
        let shape = RoundedRectangle(cornerRadius: min(th.radius, 12), style: .continuous)
        VStack(alignment: .leading, spacing: 5) {
            Text(label).font(.system(size: 13, weight: .medium)).foregroundStyle(th.muted)
            TextField("", text: text, prompt: Text(placeholder).foregroundColor(th.muted))
                .keyboardType(numeric ? .numberPad : .default)
                .autocorrectionDisabled()
                .foregroundStyle(th.fg)
                .padding(12)
                .background(shape.fill(th.card))
                .overlay(shape.stroke(th.line, lineWidth: th.border))
        }
    }

    @ViewBuilder
    private func suggestField(_ label: String, text: Binding<String>, placeholder: String, options: [String]) -> some View {
        let th = app.theme
        HStack(alignment: .bottom, spacing: 8) {
            field(label, text: text, placeholder: placeholder)
            Menu {
                ForEach(options, id: \.self) { o in
                    Button(o) { text.wrappedValue = o }
                }
            } label: {
                Image(systemName: "chevron.down.circle")
                    .font(.system(size: 22))
                    .foregroundStyle(th.muted)
                    .frame(width: 40, height: 46)
            }
        }
    }

    // MARK: picking

    private func copyPicked(_ url: URL) -> URL? {
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("upload-" + UUID().uuidString)
        do {
            try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            let dest = dir.appendingPathComponent(url.lastPathComponent)
            try FileManager.default.copyItem(at: url, to: dest)
            return dest
        } catch {
            return nil
        }
    }

    private func pickAudio(_ url: URL) {
        guard let local = copyPicked(url) else {
            note = "Не удалось прочитать файл"
            return
        }
        audioURL = local
        audioName = url.lastPathComponent
        let attrs = try? FileManager.default.attributesOfItem(atPath: local.path)
        audioSize = (attrs?[.size] as? NSNumber)?.int64Value ?? 0
        note = ""
        if title.isEmpty {
            let base = (audioName as NSString).deletingPathExtension
            let parts = base.components(separatedBy: " - ")
            if parts.count > 1 {
                if artist.isEmpty { artist = parts[0].trimmingCharacters(in: .whitespaces) }
                title = parts.dropFirst().joined(separator: " - ").trimmingCharacters(in: .whitespaces)
            } else {
                title = base
            }
        }
    }

    private func pickCover(_ url: URL) {
        guard let local = copyPicked(url), let data = try? Data(contentsOf: local) else {
            note = "Обложка не открылась"
            return
        }
        coverData = UploadView.jpeg(data)
        try? FileManager.default.removeItem(at: local)
    }

    /// Any picked image (HEIC from Photos too) → JPEG ≤ 1500 px, so the server accepts it.
    static func jpeg(_ data: Data) -> Data? {
        guard let img = UIImage(data: data) else { return nil }
        let maxSide: CGFloat = 1500
        let side = max(img.size.width, img.size.height)
        var out = img
        if side > maxSide {
            let k = maxSide / side
            let size = CGSize(width: (img.size.width * k).rounded(), height: (img.size.height * k).rounded())
            let format = UIGraphicsImageRendererFormat.default()
            format.scale = 1
            out = UIGraphicsImageRenderer(size: size, format: format).image { _ in
                img.draw(in: CGRect(origin: .zero, size: size))
            }
        }
        return out.jpegData(compressionQuality: 0.9)
    }

    // MARK: upload

    private func upload() {
        guard let file = audioURL else {
            note = "Выберите аудиофайл"
            return
        }
        let t = title.trimmingCharacters(in: .whitespaces)
        let a = artist.trimmingCharacters(in: .whitespaces)
        guard !t.isEmpty, !a.isEmpty else {
            note = "Заполните название и исполнителя"
            return
        }
        busy = true
        note = "Загружаю… 0%"
        let name = audioName
        let g = genre
        let m = mood
        let b = bpm
        let cover = coverData
        Task { @MainActor in
            do {
                let asset = AVURLAsset(url: file)
                var dur: Double = 0
                if let d = try? await asset.load(.duration) {
                    let s = d.seconds
                    if s.isFinite && s > 0 { dur = s }
                }
                let ext = (name as NSString).pathExtension.lowercased()
                let mime = UTType(filenameExtension: ext)?.preferredMIMEType ?? "application/octet-stream"
                let path = API.path("/api/uploads", [
                    ("title", t), ("artist", a), ("genre", g), ("mood", m), ("bpm", b),
                    ("filename", name), ("duration", dur > 0 ? String(format: "%.1f", dur) : ""),
                ])
                guard let url = API.url(path) else { throw APIError(message: "Неверный адрес", status: 0) }
                let (data, status) = try await Transfer.upload(url, file: file, contentType: mime) { p in
                    note = "Загружаю… \(Int((p * 100).rounded()))%"
                }
                guard status >= 200 && status < 300 else {
                    throw APIError(message: API.errorText(data, status: status, fallback: "Ошибка загрузки"), status: status)
                }
                let id = JSON.parse(data)["id"].str
                var coverFailed = false
                if let c = cover, !id.isEmpty {
                    note = "Загружаю обложку…"
                    let tmp = FileManager.default.temporaryDirectory.appendingPathComponent("cover-" + UUID().uuidString + ".jpg")
                    do {
                        try c.write(to: tmp)
                        guard let curl = API.url("/api/uploads/\(API.enc(id))/cover") else { throw APIError(message: "", status: 0) }
                        let (_, cs) = try await Transfer.upload(curl, file: tmp, contentType: "image/jpeg") { _ in }
                        if cs < 200 || cs >= 300 { coverFailed = true }
                    } catch {
                        coverFailed = true
                    }
                    try? FileManager.default.removeItem(at: tmp)
                }
                note = coverFailed ? "Трек загружен, а обложка нет. Попробуйте файл JPG или PNG до 8 МБ." : "Готово"
                app.toast("Трек загружен и уже доступен в поиске и волне")
                try? FileManager.default.removeItem(at: file.deletingLastPathComponent())
                audioURL = nil
                audioName = ""
                audioSize = 0
                title = ""
                artist = ""
                genre = ""
                mood = ""
                bpm = ""
                coverData = nil
                photoItem = nil
                wave.resetPool()
                API.shared.clearCache()
                await reloadList()
                await home.load(force: true)
            } catch {
                note = error.localizedDescription
            }
            busy = false
        }
    }

    private func reloadList() async {
        listLoading = true
        API.shared.clearCache()
        uploads = await Sources.localTracks()
        listLoading = false
    }

    private func delete(_ t: Track) {
        Task { @MainActor in
            do {
                _ = try await API.shared.send("/api/uploads/\(API.enc(t.rawId))", method: "DELETE")
                app.toast("Трек удалён")
                wave.resetPool()
                await reloadList()
            } catch {
                app.toast(error.localizedDescription)
            }
        }
    }
}
