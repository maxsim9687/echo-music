import SwiftUI

extension Color {
    /// "#rrggbb" or "#rgb".
    init(hex: String, alpha: Double = 1) {
        var s = hex.trimmingCharacters(in: .whitespaces)
        if s.hasPrefix("#") { s.removeFirst() }
        if s.count == 3 { s = s.map { "\($0)\($0)" }.joined() }
        let v = UInt64(s, radix: 16) ?? 0
        let r = Double((v >> 16) & 0xff) / 255
        let g = Double((v >> 8) & 0xff) / 255
        let b = Double(v & 0xff) / 255
        self.init(.sRGB, red: r, green: g, blue: b, opacity: alpha)
    }

    init(gray: Double, alpha: Double) {
        self.init(.sRGB, red: gray, green: gray, blue: gray, opacity: alpha)
    }
}

/// Particle look per theme (fx.js CFG).
struct FXConfig {
    let n: Int
    let minR: CGFloat
    let maxR: CGFloat
    let a: Double
    var hard = false
    var stretch = false
    var ring = false
    var orbit = false
}

/// The ten looks from styles.css `:root[data-v]`.
struct Theme: Identifiable {
    let id: Int
    let name: String
    let bgHex: String
    let fgHex: String
    let bg: Color
    let fg: Color
    let muted: Color
    let line: Color
    let card: Color
    let card2: Color
    /// Spot colour (`--p`) as 0…1 gray.
    let spot: Double
    let light: Bool
    let radius: CGFloat
    let coverRadius: CGFloat
    let pill: Bool
    let buttonRadius: CGFloat
    let border: CGFloat
    let design: Font.Design
    let heroWeight: Font.Weight
    let italic: Bool
    let width: Font.Width?
    let uppercase: Bool
    let glass: Bool
    let hardShadow: Bool
    let roundCovers: Bool
    let fx: FXConfig

    var spotColor: Color { Color(gray: spot, alpha: 1) }

    func display(_ size: CGFloat) -> Font {
        var f = Font.system(size: size, weight: heroWeight, design: design)
        if italic { f = f.italic() }
        if let w = width { f = f.width(w) }
        return f
    }

    /// Section heading font (theme display face, smaller).
    func heading(_ size: CGFloat = 26) -> Font {
        display(size)
    }

    var cardRadius: CGFloat { min(radius, 22) }

    static let all: [Theme] = [
        Theme(id: 1, name: "Монолит", bgHex: "#050505", fgHex: "#f2f2f2",
              bg: Color(hex: "#050505"), fg: Color(hex: "#f2f2f2"), muted: Color(hex: "#8a8a8a"), line: Color(hex: "#232323"),
              card: Color(hex: "#111111"), card2: Color(hex: "#1a1a1a"), spot: 242.0 / 255, light: false,
              radius: 14, coverRadius: 10, pill: true, buttonRadius: 999, border: 1,
              design: .default, heroWeight: .bold, italic: false, width: nil, uppercase: false, glass: false, hardShadow: false, roundCovers: false,
              fx: FXConfig(n: 8, minR: 160, maxR: 360, a: 0.17)),
        Theme(id: 2, name: "Бумага", bgHex: "#f5f5f4", fgHex: "#121212",
              bg: Color(hex: "#f5f5f4"), fg: Color(hex: "#121212"), muted: Color(hex: "#6b6b6b"), line: Color(hex: "#d6d6d4"),
              card: Color(hex: "#ececea"), card2: Color(hex: "#e2e2e0"), spot: 18.0 / 255, light: true,
              radius: 2, coverRadius: 0, pill: false, buttonRadius: 2, border: 1,
              design: .serif, heroWeight: .regular, italic: true, width: nil, uppercase: false, glass: false, hardShadow: false, roundCovers: false,
              fx: FXConfig(n: 7, minR: 180, maxR: 400, a: 0.11)),
        Theme(id: 3, name: "Брутализм", bgHex: "#ffffff", fgHex: "#000000",
              bg: Color(hex: "#ffffff"), fg: Color(hex: "#000000"), muted: Color(hex: "#444444"), line: Color(hex: "#000000"),
              card: Color(hex: "#ffffff"), card2: Color(hex: "#f0f0f0"), spot: 0, light: true,
              radius: 0, coverRadius: 0, pill: false, buttonRadius: 0, border: 3,
              design: .default, heroWeight: .black, italic: false, width: .condensed, uppercase: true, glass: false, hardShadow: true, roundCovers: false,
              fx: FXConfig(n: 11, minR: 50, maxR: 150, a: 0.95, hard: true)),
        Theme(id: 4, name: "Плёнка", bgHex: "#141414", fgHex: "#e6e6e6",
              bg: Color(hex: "#141414"), fg: Color(hex: "#e6e6e6"), muted: Color(hex: "#8d8d8d"), line: Color(hex: "#2c2c2c"),
              card: Color(hex: "#1b1b1b"), card2: Color(hex: "#222222"), spot: 230.0 / 255, light: false,
              radius: 4, coverRadius: 2, pill: false, buttonRadius: 4, border: 1,
              design: .default, heroWeight: .bold, italic: false, width: nil, uppercase: true, glass: false, hardShadow: false, roundCovers: false,
              fx: FXConfig(n: 9, minR: 140, maxR: 320, a: 0.15)),
        Theme(id: 5, name: "Созвездие", bgHex: "#000000", fgHex: "#fafafa",
              bg: Color(hex: "#000000"), fg: Color(hex: "#fafafa"), muted: Color(hex: "#7d7d7d"), line: Color(hex: "#1d1d1d"),
              card: Color(hex: "#0b0b0b"), card2: Color(hex: "#151515"), spot: 250.0 / 255, light: false,
              radius: 22, coverRadius: 18, pill: true, buttonRadius: 999, border: 1,
              design: .rounded, heroWeight: .heavy, italic: false, width: nil, uppercase: false, glass: false, hardShadow: false, roundCovers: false,
              fx: FXConfig(n: 26, minR: 24, maxR: 90, a: 0.24)),
        Theme(id: 6, name: "Осциллограф", bgHex: "#0a0a0a", fgHex: "#ededed",
              bg: Color(hex: "#0a0a0a"), fg: Color(hex: "#ededed"), muted: Color(hex: "#858585"), line: Color(hex: "#242424"),
              card: Color(hex: "#121212"), card2: Color(hex: "#1a1a1a"), spot: 237.0 / 255, light: false,
              radius: 8, coverRadius: 6, pill: true, buttonRadius: 999, border: 1,
              design: .default, heroWeight: .heavy, italic: false, width: .expanded, uppercase: false, glass: false, hardShadow: false, roundCovers: false,
              fx: FXConfig(n: 9, minR: 120, maxR: 280, a: 0.18, stretch: true)),
        Theme(id: 7, name: "Снег", bgHex: "#0e0e0f", fgHex: "#f0f0f0",
              bg: Color(hex: "#0e0e0f"), fg: Color(hex: "#f0f0f0"), muted: Color(hex: "#8e8e90"), line: Color(hex: "#252527"),
              card: Color(hex: "#151516"), card2: Color(hex: "#1d1d1f"), spot: 245.0 / 255, light: false,
              radius: 24, coverRadius: 20, pill: true, buttonRadius: 999, border: 1,
              design: .serif, heroWeight: .regular, italic: true, width: nil, uppercase: false, glass: false, hardShadow: false, roundCovers: false,
              fx: FXConfig(n: 42, minR: 10, maxR: 46, a: 0.38)),
        Theme(id: 8, name: "Сетка", bgHex: "#ffffff", fgHex: "#0a0a0a",
              bg: Color(hex: "#ffffff"), fg: Color(hex: "#0a0a0a"), muted: Color(hex: "#6f6f6f"), line: Color(hex: "#e3e3e3"),
              card: Color(hex: "#f4f4f4"), card2: Color(hex: "#ebebeb"), spot: 10.0 / 255, light: true,
              radius: 0, coverRadius: 0, pill: false, buttonRadius: 0, border: 1,
              design: .default, heroWeight: .black, italic: false, width: nil, uppercase: false, glass: false, hardShadow: false, roundCovers: false,
              fx: FXConfig(n: 7, minR: 120, maxR: 300, a: 0.5, ring: true)),
        Theme(id: 9, name: "Винил", bgHex: "#101010", fgHex: "#eeeeee",
              bg: Color(hex: "#101010"), fg: Color(hex: "#eeeeee"), muted: Color(hex: "#8b8b8b"), line: Color(hex: "#262626"),
              card: Color(hex: "#171717"), card2: Color(hex: "#1f1f1f"), spot: 238.0 / 255, light: false,
              radius: 16, coverRadius: 16, pill: true, buttonRadius: 999, border: 1,
              design: .serif, heroWeight: .regular, italic: false, width: nil, uppercase: false, glass: false, hardShadow: false, roundCovers: true,
              fx: FXConfig(n: 9, minR: 140, maxR: 300, a: 0.15, orbit: true)),
        Theme(id: 10, name: "Стекло", bgHex: "#161616", fgHex: "#f4f4f4",
              bg: Color(hex: "#161616"), fg: Color(hex: "#f4f4f4"), muted: Color(hex: "#9a9a9a"), line: Color(gray: 1, alpha: 0.12),
              card: Color(gray: 1, alpha: 0.06), card2: Color(gray: 1, alpha: 0.10), spot: 1, light: false,
              radius: 20, coverRadius: 14, pill: true, buttonRadius: 999, border: 1,
              design: .default, heroWeight: .light, italic: false, width: nil, uppercase: false, glass: true, hardShadow: false, roundCovers: false,
              fx: FXConfig(n: 9, minR: 200, maxR: 420, a: 0.24)),
    ]
}
