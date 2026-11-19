import SwiftUI
import UIKit

/// In-memory cover cache.
final class ImageCache: @unchecked Sendable {
    static let shared = ImageCache()
    private let cache = NSCache<NSString, UIImage>()

    private init() {
        cache.countLimit = 300
    }

    func cached(_ path: String) -> UIImage? {
        cache.object(forKey: path as NSString)
    }

    func image(_ path: String) async -> UIImage? {
        if path.isEmpty { return nil }
        if let hit = cache.object(forKey: path as NSString) { return hit }
        guard let url = API.url(path) else { return nil }
        do {
            let (data, resp) = try await API.shared.session.data(from: url)
            let code = (resp as? HTTPURLResponse)?.statusCode ?? 200
            guard code < 300, let img = UIImage(data: data) else { return nil }
            cache.setObject(img, forKey: path as NSString)
            return img
        } catch {
            return nil
        }
    }
}

/// Placeholder art like the web `.cv0…cv7` patterns.
struct CoverPattern: View {
    let seed: Int
    let fg: Color
    let bg: Color

    var body: some View {
        Canvas { ctx, size in
            let w = size.width
            let h = size.height
            ctx.fill(Path(CGRect(origin: .zero, size: size)), with: .color(bg))
            switch ((seed % 8) + 8) % 8 {
            case 0:
                ctx.fill(Path(ellipseIn: CGRect(x: w * 0.32 - w * 0.2, y: h * 0.34 - h * 0.2, width: w * 0.4, height: h * 0.4)), with: .color(fg))
                ctx.stroke(Path(ellipseIn: CGRect(x: w * 0.7 - w * 0.25, y: h * 0.72 - h * 0.25, width: w * 0.5, height: h * 0.5)), with: .color(fg), lineWidth: max(1, w * 0.012))
            case 1:
                var p = Path()
                var x: CGFloat = -h
                while x < w {
                    p.move(to: CGPoint(x: x, y: h))
                    p.addLine(to: CGPoint(x: x + h, y: 0))
                    x += max(6, w * 0.08)
                }
                ctx.stroke(p, with: .color(fg), lineWidth: max(1, w * 0.012))
            case 2:
                var r: CGFloat = max(4, w * 0.06)
                while r < w * 0.75 {
                    ctx.stroke(Path(ellipseIn: CGRect(x: w / 2 - r, y: h / 2 - r, width: r * 2, height: r * 2)), with: .color(fg.opacity(0.8)), lineWidth: max(1, w * 0.01))
                    r += max(5, w * 0.07)
                }
            case 3:
                ctx.fill(Path(CGRect(origin: .zero, size: size)), with: .linearGradient(Gradient(colors: [fg, bg]), startPoint: CGPoint(x: w * 0.2, y: 0), endPoint: CGPoint(x: w * 0.8, y: h * 0.8)))
            case 4:
                ctx.fill(Path(CGRect(origin: .zero, size: size)), with: .conicGradient(Gradient(colors: [fg, bg, fg, bg, fg]), center: CGPoint(x: w / 2, y: h / 2), angle: .degrees(30)))
            case 5:
                var p = Path()
                var y: CGFloat = 0
                while y < h {
                    p.addRect(CGRect(x: 0, y: y, width: w * 0.4, height: 1))
                    y += max(4, h * 0.05)
                }
                ctx.fill(p, with: .color(fg))
            case 6:
                let r = w * 0.38 * 2
                ctx.fill(Path(ellipseIn: CGRect(x: w / 2 - r / 2 * 1.3, y: h * 1.2 - r / 2 * 1.3, width: r * 1.3, height: r * 1.3)), with: .color(fg))
            default:
                var p = Path()
                var y: CGFloat = h / 8
                while y < h {
                    p.addRect(CGRect(x: 0, y: y, width: w, height: h / 8))
                    y += h / 4
                }
                ctx.fill(p, with: .color(fg))
            }
        }
    }
}

/// Square cover from a server path with a pattern placeholder; round on «Винил».
struct CoverView: View {
    @EnvironmentObject var app: AppModel
    let path: String
    var seed: Int = 0
    var radius: CGFloat? = nil
    var round: Bool = false
    @State private var image: UIImage? = nil

    var body: some View {
        let th = app.theme
        let r = radius ?? th.coverRadius
        let shape: AnyShape = (round || th.roundCovers) ? AnyShape(Circle()) : AnyShape(RoundedRectangle(cornerRadius: r, style: .continuous))
        Color.clear
            .aspectRatio(1, contentMode: .fit)
            .overlay {
                ZStack {
                    CoverPattern(seed: seed, fg: th.fg, bg: th.card2)
                    if let image = image {
                        Image(uiImage: image)
                            .resizable()
                            .scaledToFill()
                            .transition(.opacity)
                    }
                }
            }
            .clipShape(shape)
            .overlay { shape.stroke(th.line, lineWidth: min(th.border, 2)) }
            .task(id: path) {
                if let hit = ImageCache.shared.cached(path) {
                    image = hit
                    return
                }
                image = nil
                let loaded = await ImageCache.shared.image(path)
                withAnimation(.easeOut(duration: 0.2)) { image = loaded }
            }
    }
}
