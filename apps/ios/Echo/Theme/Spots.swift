import SwiftUI
import UIKit
import UIKit.UIGestureRecognizerSubclass

/// Last touch in window coordinates (main thread only), read by the spots background.
enum FXPointer {
    static var location: CGPoint? = nil
}

/// Soft spots that drift, push away from the finger and swell while music plays. Port of fx.js.
final class SpotsSim {
    struct Spot {
        var hx: CGFloat
        var hy: CGFloat
        var x: CGFloat
        var y: CGFloat
        var vx: CGFloat = 0
        var vy: CGFloat = 0
        var r: CGFloat
        var ph: Double
        var sp: Double
        var amp: CGFloat
        var ang: Double
        var rad: CGFloat
    }

    private var spots: [Spot] = []
    private var size: CGSize = .zero
    private var themeId = -1
    private var t: Double = 0
    private var en: Double = 0.25
    private var last: Date? = nil
    private var fx: CGFloat = -9999
    private var fy: CGFloat = -9999

    private func rnd(_ a: Double, _ b: Double) -> Double { Double.random(in: a...max(a, b)) }

    private func seed(_ cfg: FXConfig) {
        let k = CGFloat(max(0.55, min(1.2, Double(size.width) / 1300)))
        // phones are small: keep the look by scaling a bit less than the web
        let scale = max(k, 0.75)
        spots = (0..<cfg.n).map { _ in
            let hx = CGFloat(rnd(0, Double(size.width)))
            let hy = CGFloat(rnd(0, Double(size.height)))
            return Spot(hx: hx, hy: hy, x: hx, y: hy,
                        r: CGFloat(rnd(Double(cfg.minR), Double(cfg.maxR))) * scale,
                        ph: rnd(0, 6.283), sp: rnd(0.15, 0.45), amp: CGFloat(rnd(30, 120)),
                        ang: rnd(0, 6.283), rad: CGFloat(rnd(120, Double(max(size.width, size.height)) * 0.55)))
        }
    }

    func render(_ ctx: inout GraphicsContext, size newSize: CGSize, date: Date?, theme: Theme, playing: Bool) {
        let cfg = theme.fx
        if newSize != size || theme.id != themeId || spots.isEmpty {
            size = newSize
            themeId = theme.id
            if size.width > 0 && size.height > 0 { seed(cfg) }
        }
        var dt: Double = 0
        if let date = date {
            if let l = last { dt = min(0.05, max(0, date.timeIntervalSince(l))) }
            last = date
        }
        step(dt, cfg: cfg, playing: playing)
        draw(&ctx, cfg: cfg, theme: theme)
    }

    private func step(_ dt: Double, cfg: FXConfig, playing: Bool) {
        t += dt
        let target = playing ? 1.0 : 0.25
        en += (target - en) * 0.04
        let beat = en * (0.6 + 0.4 * abs(sin(t * 2.2)))
        let ocx = size.width / 2
        let ocy = size.height * 0.3
        let pointer = FXPointer.location
        let mx = pointer?.x ?? -9999
        let my = pointer?.y ?? -9999
        for i in spots.indices {
            var p = spots[i]
            var tx: CGFloat
            var ty: CGFloat
            if cfg.orbit {
                p.ang += dt * 0.08 * (1 + beat * 2)
                tx = ocx + CGFloat(cos(p.ang)) * p.rad
                ty = ocy + CGFloat(sin(p.ang)) * p.rad * 0.45
            } else {
                tx = p.hx + CGFloat(sin(t * p.sp + p.ph)) * p.amp
                ty = p.hy + CGFloat(cos(t * p.sp * 0.8 + p.ph)) * p.amp
            }
            if dt > 0 {
                let dx = p.x - mx
                let dy = p.y - my
                let d = sqrt(dx * dx + dy * dy)
                let rr = p.r * 1.1 + 140
                if d < rr && d > 0 {
                    let f = (1 - d / rr) * 9
                    p.vx += (dx / d) * f
                    p.vy += (dy / d) * f
                }
                p.vx += (tx - p.x) * 0.012
                p.vy += (ty - p.y) * 0.012
                p.vx *= 0.88
                p.vy *= 0.88
                p.x += p.vx
                p.y += p.vy
            }
            spots[i] = p
        }
        if pointer != nil {
            if fx < -999 {
                fx = mx
                fy = my
            }
            fx += (mx - fx) * 0.08
            fy += (my - fy) * 0.08
        } else {
            fx = -9999
            fy = -9999
        }
        lastBeat = beat
    }

    private var lastBeat: Double = 0.25

    private func draw(_ ctx: inout GraphicsContext, cfg: FXConfig, theme: Theme) {
        let beat = lastBeat
        for p in spots {
            let sw = 1 + beat * 0.28 * sin(t * 3 + p.ph)
            let alpha = cfg.a * (0.75 + beat * 0.5)
            let sx: CGFloat = cfg.stretch ? CGFloat(1.6 + beat * 0.8) : 1
            blob(&ctx, x: p.x, y: p.y, r: p.r * CGFloat(sw), a: alpha, sx: sx, cfg: cfg, gray: theme.spot)
        }
        if fx > -999 && !cfg.hard && !cfg.ring {
            blob(&ctx, x: fx, y: fy, r: 180, a: cfg.a * 0.9, sx: 1, cfg: cfg, gray: theme.spot)
        }
    }

    private func blob(_ ctx: inout GraphicsContext, x: CGFloat, y: CGFloat, r: CGFloat, a: Double, sx: CGFloat, cfg: FXConfig, gray: Double) {
        guard r > 0 else { return }
        let c = Color(gray: gray, alpha: min(1, a))
        if cfg.hard {
            let rect = CGRect(x: x - r * 0.5 * sx, y: y - r * 0.5, width: r * sx, height: r)
            ctx.fill(Path(ellipseIn: rect), with: .color(c))
            return
        }
        if cfg.ring {
            let rect = CGRect(x: x - r * 0.5 * sx, y: y - r * 0.5, width: r * sx, height: r)
            let path = Path(ellipseIn: rect)
            ctx.fill(path, with: .color(Color(gray: gray, alpha: a * 0.08)))
            ctx.stroke(path, with: .color(Color(gray: gray, alpha: a * 0.35)), lineWidth: 1.5)
            return
        }
        var local = ctx
        local.translateBy(x: x, y: y)
        local.scaleBy(x: sx, y: 1)
        let gradient = Gradient(stops: [
            .init(color: Color(gray: gray, alpha: a), location: 0),
            .init(color: Color(gray: gray, alpha: a * 0.45), location: 0.45),
            .init(color: Color(gray: gray, alpha: 0), location: 1),
        ])
        let rect = CGRect(x: -r, y: -r, width: r * 2, height: r * 2)
        local.fill(Path(ellipseIn: rect), with: .radialGradient(gradient, center: .zero, startRadius: 0, endRadius: r))
    }
}

/// Full-screen animated background for every screen. Static when Reduce Motion is on.
struct SpotsBackground: View {
    @EnvironmentObject var app: AppModel
    @EnvironmentObject var player: Player
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var sim = SpotsSim()

    var body: some View {
        let theme = app.theme
        let playing = player.isPlaying
        ZStack {
            theme.bg
            if reduceMotion {
                Canvas { ctx, size in
                    sim.render(&ctx, size: size, date: nil, theme: theme, playing: playing)
                }
            } else {
                TimelineView(.animation) { timeline in
                    Canvas { ctx, size in
                        sim.render(&ctx, size: size, date: timeline.date, theme: theme, playing: playing)
                    }
                }
            }
        }
        .ignoresSafeArea()
        .allowsHitTesting(false)
    }
}

/// Watches touches on the whole window without taking them from buttons or scroll views.
final class PassiveTouchRecognizer: UIGestureRecognizer, UIGestureRecognizerDelegate {
    override init(target: Any?, action: Selector?) {
        super.init(target: target, action: action)
        cancelsTouchesInView = false
        delaysTouchesBegan = false
        delaysTouchesEnded = false
        delegate = self
    }

    override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent) {
        if let t = touches.first { FXPointer.location = t.location(in: view) }
    }

    override func touchesMoved(_ touches: Set<UITouch>, with event: UIEvent) {
        if let t = touches.first { FXPointer.location = t.location(in: view) }
    }

    override func touchesEnded(_ touches: Set<UITouch>, with event: UIEvent) {
        FXPointer.location = nil
        state = .failed
    }

    override func touchesCancelled(_ touches: Set<UITouch>, with event: UIEvent) {
        FXPointer.location = nil
        state = .failed
    }

    func gestureRecognizer(_ gestureRecognizer: UIGestureRecognizer, shouldRecognizeSimultaneouslyWith otherGestureRecognizer: UIGestureRecognizer) -> Bool {
        true
    }
}

/// Zero-size view that installs PassiveTouchRecognizer on its window.
struct TouchTrackerInstaller: UIViewRepresentable {
    final class InstallerView: UIView {
        override func didMoveToWindow() {
            super.didMoveToWindow()
            guard let w = window else { return }
            let has = (w.gestureRecognizers ?? []).contains { $0 is PassiveTouchRecognizer }
            if !has { w.addGestureRecognizer(PassiveTouchRecognizer(target: nil, action: nil)) }
        }
    }

    func makeUIView(context: Context) -> InstallerView {
        let v = InstallerView(frame: .zero)
        v.isUserInteractionEnabled = false
        v.backgroundColor = .clear
        return v
    }

    func updateUIView(_ uiView: InstallerView, context: Context) {}
}
