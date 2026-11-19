import Foundation
import AVFoundation
import CoreMedia

enum WavError: LocalizedError {
    case noAudio
    case cannotRead
    case cannotWrite

    var errorDescription: String? {
        switch self {
        case .noAudio: return "В файле нет звука"
        case .cannotRead: return "Не удалось прочитать файл"
        case .cannotWrite: return "Не удалось записать WAV"
        }
    }
}

/// Streams PCM into a RIFF/WAVE file (PCM format 1, little-endian), like encodeWav in download.js.
final class RiffWriter {
    let bits: Int
    private let handle: FileHandle
    private(set) var started = false
    private var sampleRate = 44100
    private var channels = 2
    private var dataBytes: UInt64 = 0

    init(url: URL, bits: Int) throws {
        self.bits = bits == 24 ? 24 : 16
        if FileManager.default.fileExists(atPath: url.path) {
            try FileManager.default.removeItem(at: url)
        }
        guard FileManager.default.createFile(atPath: url.path, contents: nil) else { throw WavError.cannotWrite }
        handle = try FileHandle(forWritingTo: url)
    }

    func start(sampleRate: Double, channels: Int) throws {
        guard !started else { return }
        self.sampleRate = max(1, Int(sampleRate.rounded()))
        self.channels = max(1, channels)
        started = true
        try handle.write(contentsOf: header(dataSize: 0))
    }

    /// Interleaved float samples in −1…1.
    func write(_ samples: [Float]) throws {
        guard !samples.isEmpty else { return }
        let bps = bits / 8
        var bytes = [UInt8](repeating: 0, count: samples.count * bps)
        var o = 0
        if bits == 24 {
            for f in samples {
                let s = f.isFinite ? max(-1, min(1, f)) : 0
                let n = Int32((s < 0 ? s * 8_388_608 : s * 8_388_607).rounded())
                bytes[o] = UInt8(truncatingIfNeeded: n)
                bytes[o + 1] = UInt8(truncatingIfNeeded: n >> 8)
                bytes[o + 2] = UInt8(truncatingIfNeeded: n >> 16)
                o += 3
            }
        } else {
            for f in samples {
                let s = f.isFinite ? max(-1, min(1, f)) : 0
                let n = Int16(s < 0 ? s * 32_768 : s * 32_767)
                bytes[o] = UInt8(truncatingIfNeeded: n)
                bytes[o + 1] = UInt8(truncatingIfNeeded: n >> 8)
                o += 2
            }
        }
        try handle.write(contentsOf: Data(bytes))
        dataBytes += UInt64(bytes.count)
    }

    func finish() throws {
        if !started { try start(sampleRate: 44100, channels: 2) }
        let size = UInt32(clamping: dataBytes)
        try handle.seek(toOffset: 0)
        try handle.write(contentsOf: header(dataSize: size))
        try handle.close()
    }

    func abort() {
        try? handle.close()
    }

    private func header(dataSize: UInt32) -> Data {
        var d = Data()
        func ascii(_ s: String) { d.append(contentsOf: Array(s.utf8)) }
        func u32(_ v: UInt32) {
            var x = v.littleEndian
            withUnsafeBytes(of: &x) { d.append(contentsOf: $0) }
        }
        func u16(_ v: UInt16) {
            var x = v.littleEndian
            withUnsafeBytes(of: &x) { d.append(contentsOf: $0) }
        }
        let bps = bits / 8
        ascii("RIFF")
        u32(36 &+ dataSize)
        ascii("WAVE")
        ascii("fmt ")
        u32(16)
        u16(1)
        u16(UInt16(channels))
        u32(UInt32(sampleRate))
        u32(UInt32(sampleRate * channels * bps))
        u16(UInt16(channels * bps))
        u16(UInt16(bits))
        ascii("data")
        u32(dataSize)
        return d
    }
}

/// Decodes an MP3 / FLAC / M4A file to WAV at the file's own sample rate.
/// Primary path: AVAssetReader → LinearPCM (float, no resampling) → RIFF. Fallback: AVAudioFile.
enum WavConverter {
    static func convert(_ src: URL, to dest: URL, bits: Int) async throws {
        do {
            try await viaReader(src, dest, bits)
        } catch {
            try viaAudioFile(src, dest, bits)
        }
    }

    private static func viaReader(_ src: URL, _ dest: URL, _ bits: Int) async throws {
        let asset = AVURLAsset(url: src)
        let tracks = try await asset.loadTracks(withMediaType: .audio)
        guard let track = tracks.first else { throw WavError.noAudio }
        let reader = try AVAssetReader(asset: asset)
        // No AVSampleRateKey / AVNumberOfChannelsKey: the source rate and layout are kept.
        let settings: [String: Any] = [
            AVFormatIDKey: kAudioFormatLinearPCM,
            AVLinearPCMBitDepthKey: 32,
            AVLinearPCMIsFloatKey: true,
            AVLinearPCMIsBigEndianKey: false,
            AVLinearPCMIsNonInterleaved: false,
        ]
        let output = AVAssetReaderTrackOutput(track: track, outputSettings: settings)
        output.alwaysCopiesSampleData = false
        guard reader.canAdd(output) else { throw WavError.cannotRead }
        reader.add(output)
        guard reader.startReading() else { throw reader.error ?? WavError.cannotRead }
        let writer = try RiffWriter(url: dest, bits: bits)
        do {
            while let buffer = output.copyNextSampleBuffer() {
                if !writer.started {
                    guard let fd = CMSampleBufferGetFormatDescription(buffer),
                          let asbd = CMAudioFormatDescriptionGetStreamBasicDescription(fd)?.pointee else {
                        throw WavError.cannotRead
                    }
                    try writer.start(sampleRate: asbd.mSampleRate, channels: Int(asbd.mChannelsPerFrame))
                }
                guard let block = CMSampleBufferGetDataBuffer(buffer) else { continue }
                let length = CMBlockBufferGetDataLength(block)
                let count = length / 4
                if count == 0 { continue }
                var floats = [Float](repeating: 0, count: count)
                let status = floats.withUnsafeMutableBytes { raw -> OSStatus in
                    guard let base = raw.baseAddress else { return -1 }
                    return CMBlockBufferCopyDataBytes(block, atOffset: 0, dataLength: count * 4, destination: base)
                }
                if status != 0 { throw WavError.cannotRead }
                try writer.write(floats)
            }
            if reader.status == .failed { throw reader.error ?? WavError.cannotRead }
            if !writer.started { throw WavError.noAudio }
            try writer.finish()
        } catch {
            writer.abort()
            reader.cancelReading()
            try? FileManager.default.removeItem(at: dest)
            throw error
        }
    }

    private static func viaAudioFile(_ src: URL, _ dest: URL, _ bits: Int) throws {
        let file = try AVAudioFile(forReading: src)
        let format = file.processingFormat
        let channels = Int(format.channelCount)
        guard channels > 0 else { throw WavError.noAudio }
        let capacity: AVAudioFrameCount = 65_536
        guard let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: capacity) else { throw WavError.cannotRead }
        let writer = try RiffWriter(url: dest, bits: bits)
        do {
            try writer.start(sampleRate: file.fileFormat.sampleRate, channels: channels)
            while file.framePosition < file.length {
                try file.read(into: buffer, frameCount: capacity)
                let n = Int(buffer.frameLength)
                if n == 0 { break }
                guard let data = buffer.floatChannelData else { throw WavError.cannotRead }
                var inter = [Float](repeating: 0, count: n * channels)
                if format.isInterleaved {
                    let p = data[0]
                    for i in 0..<(n * channels) { inter[i] = p[i] }
                } else {
                    for c in 0..<channels {
                        let p = data[c]
                        var o = c
                        for i in 0..<n {
                            inter[o] = p[i]
                            o += channels
                        }
                    }
                }
                try writer.write(inter)
            }
            try writer.finish()
        } catch {
            writer.abort()
            try? FileManager.default.removeItem(at: dest)
            throw error
        }
    }
}
