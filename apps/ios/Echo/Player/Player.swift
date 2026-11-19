import Foundation
import AVFoundation
import MediaPlayer
import UIKit

enum PlayMode {
    case list
    case wave
}

/// Current position, separate from Player so only progress views redraw twice a second.
@MainActor
final class PlaybackClock: ObservableObject {
    @Published var time: Double = 0
    @Published var duration: Double = 0

    var fraction: Double {
        guard duration > 0 else { return 0 }
        return min(1, max(0, time / duration))
    }
}

/// AVPlayer queue with background audio, lock-screen controls, prefetch and the 4-errors stop.
@MainActor
final class Player: ObservableObject {
    static let shared = Player()

    enum Reason {
        case skip
        case end
        case error
        case ban
    }

    @Published private(set) var queue: [Track] = []
    @Published private(set) var index: Int = -1
    @Published private(set) var mode: PlayMode = .list
    @Published private(set) var isPlaying = false
    @Published private(set) var isBuffering = false
    @Published private(set) var shuffle = false

    let clock = PlaybackClock()

    private let player = AVPlayer()
    private var timeObserver: Any?
    private var statusObservation: NSKeyValueObservation?
    private var controlObservation: NSKeyValueObservation?
    private var notificationTokens: [NSObjectProtocol] = []
    private var counted = false
    private var errRun = 0
    private var waitingNext = false
    private var prefetchedId: String? = nil
    private var prefetchedItem: AVPlayerItem? = nil
    private var artwork: MPMediaItemArtwork? = nil
    private var artworkId: String? = nil

    var current: Track? {
        guard index >= 0 && index < queue.count else { return nil }
        return queue[index]
    }

    var upNext: Track? {
        let i = index + 1
        guard i >= 0 && i < queue.count else { return nil }
        return queue[i]
    }

    private init() {
        player.automaticallyWaitsToMinimizeStalling = true
        configureSession()
        timeObserver = Player.addTimeObserver(player)
        controlObservation = Player.observeControl(player)
        notificationTokens = Player.observeNotifications()
        configureRemoteCommands()
    }

    // MARK: setup (nonisolated helpers so callbacks never inherit main-actor isolation)

    private func configureSession() {
        let s = AVAudioSession.sharedInstance()
        do {
            try s.setCategory(.playback, mode: .default, options: [])
            try s.setActive(true)
        } catch {
            // playback still works in the foreground
        }
    }

    nonisolated private static func addTimeObserver(_ p: AVPlayer) -> Any {
        let interval = CMTime(seconds: 0.5, preferredTimescale: 600)
        return p.addPeriodicTimeObserver(forInterval: interval, queue: .main) { time in
            let secs = time.seconds
            Task { @MainActor in Player.shared.tick(secs) }
        }
    }

    nonisolated private static func observeControl(_ p: AVPlayer) -> NSKeyValueObservation {
        p.observe(\.timeControlStatus, options: [.initial, .new]) { obj, _ in
            let status = obj.timeControlStatus
            let playing = status != .paused
            let buffering = status == .waitingToPlayAtSpecifiedRate
            Task { @MainActor in Player.shared.controlChanged(playing: playing, buffering: buffering) }
        }
    }

    nonisolated private static func observeStatus(_ item: AVPlayerItem) -> NSKeyValueObservation {
        let oid = ObjectIdentifier(item)
        return item.observe(\.status, options: [.new]) { obj, _ in
            let failed = obj.status == .failed
            let ready = obj.status == .readyToPlay
            let secs = obj.duration.seconds
            Task { @MainActor in Player.shared.itemStatus(oid, failed: failed, ready: ready, duration: secs) }
        }
    }

    nonisolated private static func observeNotifications() -> [NSObjectProtocol] {
        let nc = NotificationCenter.default
        var out: [NSObjectProtocol] = []
        out.append(nc.addObserver(forName: AVPlayerItem.didPlayToEndTimeNotification, object: nil, queue: .main) { n in
            let oid = (n.object as? AVPlayerItem).map { ObjectIdentifier($0) }
            Task { @MainActor in Player.shared.itemEnded(oid) }
        })
        out.append(nc.addObserver(forName: AVPlayerItem.failedToPlayToEndTimeNotification, object: nil, queue: .main) { n in
            let oid = (n.object as? AVPlayerItem).map { ObjectIdentifier($0) }
            Task { @MainActor in Player.shared.itemFailed(oid) }
        })
        out.append(nc.addObserver(forName: AVAudioSession.interruptionNotification, object: nil, queue: .main) { n in
            let raw = (n.userInfo?[AVAudioSessionInterruptionTypeKey] as? NSNumber)?.uintValue ?? 0
            let optRaw = (n.userInfo?[AVAudioSessionInterruptionOptionKey] as? NSNumber)?.uintValue ?? 0
            Task { @MainActor in Player.shared.interruption(type: raw, options: optRaw) }
        })
        return out
    }

    nonisolated private static func makeArtwork(_ image: UIImage) -> MPMediaItemArtwork {
        MPMediaItemArtwork(boundsSize: image.size) { _ in image }
    }

    private func configureRemoteCommands() {
        let rc = MPRemoteCommandCenter.shared()
        rc.playCommand.isEnabled = true
        rc.pauseCommand.isEnabled = true
        rc.togglePlayPauseCommand.isEnabled = true
        rc.nextTrackCommand.isEnabled = true
        rc.previousTrackCommand.isEnabled = true
        rc.changePlaybackPositionCommand.isEnabled = true
        Player.addRemoteTargets(rc)
    }

    nonisolated private static func addRemoteTargets(_ rc: MPRemoteCommandCenter) {
        rc.playCommand.addTarget { _ in
            Task { @MainActor in Player.shared.resume() }
            return .success
        }
        rc.pauseCommand.addTarget { _ in
            Task { @MainActor in Player.shared.pause() }
            return .success
        }
        rc.togglePlayPauseCommand.addTarget { _ in
            Task { @MainActor in Player.shared.toggle() }
            return .success
        }
        rc.nextTrackCommand.addTarget { _ in
            Task { @MainActor in Player.shared.next() }
            return .success
        }
        rc.previousTrackCommand.addTarget { _ in
            Task { @MainActor in Player.shared.prev() }
            return .success
        }
        rc.changePlaybackPositionCommand.addTarget { event in
            guard let e = event as? MPChangePlaybackPositionCommandEvent else { return .commandFailed }
            let pos = e.positionTime
            Task { @MainActor in Player.shared.seek(to: pos) }
            return .success
        }
    }

    // MARK: callbacks

    private func tick(_ secs: Double) {
        guard secs.isFinite, let t = current else { return }
        clock.time = secs
        if let item = player.currentItem {
            let d = item.duration.seconds
            if d.isFinite && d > 0 && abs(d - clock.duration) > 0.5 { clock.duration = d }
        }
        if !counted && secs > 30 {
            counted = true
            Recs.shared.listened(t)
        }
    }

    private func controlChanged(playing: Bool, buffering: Bool) {
        if isPlaying != playing { isPlaying = playing }
        if isBuffering != buffering { isBuffering = buffering }
        if playing && !buffering { errRun = 0 }
        updateNowPlaying()
    }

    private func isCurrent(_ oid: ObjectIdentifier?) -> Bool {
        guard let oid = oid, let item = player.currentItem else { return false }
        return ObjectIdentifier(item) == oid
    }

    private func itemStatus(_ oid: ObjectIdentifier, failed: Bool, ready: Bool, duration: Double) {
        guard isCurrent(oid) else { return }
        if failed { handleError() }
        if ready && duration.isFinite && duration > 0 {
            clock.duration = duration
            updateNowPlaying()
        }
    }

    private func itemEnded(_ oid: ObjectIdentifier?) {
        guard isCurrent(oid), let t = current else { return }
        Recs.shared.finished(t)
        next(.end)
    }

    private func itemFailed(_ oid: ObjectIdentifier?) {
        guard isCurrent(oid) else { return }
        handleError()
    }

    private func interruption(type: UInt, options: UInt) {
        guard let kind = AVAudioSession.InterruptionType(rawValue: type) else { return }
        if kind == .ended {
            let opts = AVAudioSession.InterruptionOptions(rawValue: options)
            if opts.contains(.shouldResume) { player.play() }
        }
    }

    private func handleError() {
        guard current != nil else { return }
        errRun += 1
        if errRun >= 4 {
            errRun = 0
            pause()
            AppModel.shared.toast("Несколько треков подряд не загрузились. Проверьте соединение и нажмите плей ещё раз.")
            return
        }
        AppModel.shared.toast("Трек не загрузился, включаю следующий")
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: 600_000_000)
            Player.shared.next(.error)
        }
    }

    // MARK: queue control

    func play(_ list: [Track], start: Int = 0, mode: PlayMode = .list) {
        guard !list.isEmpty else {
            AppModel.shared.toast("Здесь пока нечего играть")
            return
        }
        queue = list.map { Sources.refresh($0) }
        index = max(0, min(start, queue.count - 1))
        self.mode = mode
        prefetchedId = nil
        prefetchedItem = nil
        load(autoplay: true)
    }

    func playAt(_ i: Int) {
        guard i >= 0 && i < queue.count else { return }
        index = i
        load(autoplay: true)
    }

    func append(_ list: [Track]) {
        guard !list.isEmpty else { return }
        let known = Set(queue.map { $0.id })
        queue += list.filter { !known.contains($0.id) }.map { Sources.refresh($0) }
        if prefetchedId == nil { prefetchNext() }
    }

    func truncateAfterCurrent() {
        guard index >= 0 && index < queue.count else { return }
        queue = Array(queue.prefix(index + 1))
        prefetchedId = nil
        prefetchedItem = nil
    }

    private func makeItem(_ url: URL) -> AVPlayerItem {
        if url.isFileURL { return AVPlayerItem(url: url) }
        let cookies = HTTPCookieStorage.shared.cookies(for: url) ?? []
        let asset = AVURLAsset(url: url, options: [AVURLAssetHTTPCookiesKey: cookies])
        let item = AVPlayerItem(asset: asset)
        item.preferredForwardBufferDuration = 20
        return item
    }

    private func sourceURL(_ t: Track) -> URL? {
        Downloads.shared.playableURL(for: t) ?? API.url(t.stream)
    }

    private func load(autoplay: Bool) {
        guard let t = current else { return }
        counted = false
        clock.time = 0
        clock.duration = t.duration
        Recs.shared.markPlayed(t)
        let item: AVPlayerItem
        if let pid = prefetchedId, pid == t.id, let pre = prefetchedItem {
            item = pre
        } else if let url = sourceURL(t) {
            item = makeItem(url)
        } else {
            prefetchedId = nil
            prefetchedItem = nil
            handleError()
            return
        }
        prefetchedId = nil
        prefetchedItem = nil
        statusObservation = Player.observeStatus(item)
        player.replaceCurrentItem(with: item)
        if autoplay { player.play() }
        objectWillChange.send()
        updateNowPlaying()
        loadArtwork(t)
        if mode == .wave && queue.count - index < 4 {
            Task { @MainActor in await Wave.shared.refill() }
        }
        prefetchNext()
    }

    /// Warms the next track: builds its item ahead of time and loads its first bytes.
    private func prefetchNext() {
        guard let nx = upNext, let url = sourceURL(nx) else { return }
        if prefetchedId == nx.id { return }
        let item = makeItem(url)
        prefetchedId = nx.id
        prefetchedItem = item
        if !url.isFileURL {
            let asset = item.asset
            Task {
                _ = try? await asset.load(.isPlayable)
            }
        }
    }

    func next(_ reason: Reason = .skip) {
        if let t = current, reason == .skip, clock.time < 30 {
            Recs.shared.skipped(t, at: clock.time)
        }
        if index < queue.count - 1 {
            index += 1
            load(autoplay: true)
        } else if mode == .wave {
            if waitingNext { return }
            waitingNext = true
            Task { @MainActor in
                await Wave.shared.refill()
                let p = Player.shared
                p.waitingNext = false
                if p.index < p.queue.count - 1 {
                    p.index += 1
                    p.load(autoplay: true)
                }
            }
        } else {
            pause()
        }
    }

    func prev() {
        if clock.time > 4 || index <= 0 {
            seek(to: 0)
        } else {
            index -= 1
            load(autoplay: true)
        }
    }

    func toggle() {
        guard current != nil else {
            Task { @MainActor in await Wave.shared.start() }
            return
        }
        if isPlaying { pause() } else { resume() }
    }

    func resume() {
        guard current != nil else { return }
        if player.currentItem?.status == .failed {
            load(autoplay: true)
            return
        }
        player.play()
    }

    func pause() {
        player.pause()
        updateNowPlaying()
    }

    func seek(to seconds: Double) {
        guard seconds.isFinite else { return }
        let s = max(0, seconds)
        player.seek(to: CMTime(seconds: s, preferredTimescale: 600))
        clock.time = s
        updateNowPlaying()
    }

    /// «Не то»: learns, bans and skips.
    func banCurrent() {
        guard let t = current else { return }
        Recs.shared.ban(t)
        AppModel.shared.toast("Больше не покажу похожее так часто")
        next(.ban)
    }

    func toggleShuffle() {
        shuffle.toggle()
        if shuffle && queue.count > index + 2 {
            let head = Array(queue.prefix(index + 1))
            let rest = Array(queue.dropFirst(index + 1)).shuffled()
            queue = head + rest
            prefetchedId = nil
            prefetchedItem = nil
            prefetchNext()
        }
    }

    // MARK: now playing

    private func updateNowPlaying() {
        guard let t = current else {
            MPNowPlayingInfoCenter.default().nowPlayingInfo = nil
            return
        }
        var info: [String: Any] = [
            MPMediaItemPropertyTitle: t.title,
            MPMediaItemPropertyArtist: t.artist,
            MPMediaItemPropertyAlbumTitle: t.genre.isEmpty ? "Эхо" : t.genre,
            MPNowPlayingInfoPropertyElapsedPlaybackTime: clock.time,
            MPNowPlayingInfoPropertyPlaybackRate: isPlaying ? 1.0 : 0.0,
        ]
        if clock.duration > 0 { info[MPMediaItemPropertyPlaybackDuration] = clock.duration }
        if let art = artwork, artworkId == t.id { info[MPMediaItemPropertyArtwork] = art }
        MPNowPlayingInfoCenter.default().nowPlayingInfo = info
    }

    private func loadArtwork(_ t: Track) {
        if artworkId == t.id { return }
        artwork = nil
        artworkId = nil
        guard !t.cover.isEmpty else { return }
        let id = t.id
        let path = t.cover
        Task { @MainActor in
            guard let img = await ImageCache.shared.image(path) else { return }
            let p = Player.shared
            guard p.current?.id == id else { return }
            p.artwork = Player.makeArtwork(img)
            p.artworkId = id
            p.updateNowPlaying()
        }
    }
}
