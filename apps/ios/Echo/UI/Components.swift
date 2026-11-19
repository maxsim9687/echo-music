import SwiftUI

// MARK: - environment

extension View {
    /// Injects every shared model (sheets and covers get their own copy to be safe).
    @MainActor
    func echoEnvironment() -> some View {
        self
            .environmentObject(AppModel.shared)
            .environmentObject(Player.shared)
            .environmentObject(Recs.shared)
            .environmentObject(Wave.shared)
            .environmentObject(Downloads.shared)
            .environmentObject(HomeModel.shared)
            .environmentObject(ChartsModel.shared)
    }

    /// Animated spots behind a screen and transparent scroll / nav backgrounds.
    @MainActor
    func screenBackground() -> some View {
        self
            .scrollContentBackground(.hidden)
            .background { SpotsBackground() }
            .toolbarBackground(.hidden, for: .navigationBar)
    }
}

// MARK: - surfaces

struct CardBackground: ViewModifier {
    @EnvironmentObject var app: AppModel
    var radius: CGFloat? = nil
    var padding: CGFloat = 0

    @ViewBuilder
    func body(content: Content) -> some View {
        let th = app.theme
        let r = radius ?? th.radius
        let shape = RoundedRectangle(cornerRadius: r, style: .continuous)
        content
            .padding(padding)
            .background {
                if th.glass {
                    shape.fill(.ultraThinMaterial)
                        .overlay(shape.fill(th.card))
                } else {
                    shape.fill(th.card.opacity(th.light ? 0.85 : 0.55))
                }
            }
            .overlay(shape.stroke(th.line, lineWidth: th.border))
            .background {
                if th.hardShadow {
                    shape.fill(th.fg).offset(x: 6, y: 6)
                }
            }
    }
}

extension View {
    @MainActor
    func echoCard(radius: CGFloat? = nil, padding: CGFloat = 0) -> some View {
        modifier(CardBackground(radius: radius, padding: padding))
    }
}

// MARK: - text

struct SectionHeader: View {
    @EnvironmentObject var app: AppModel
    let title: String
    var note: String? = nil
    var actionTitle: String? = nil
    var action: (() -> Void)? = nil

    var body: some View {
        let th = app.theme
        HStack(alignment: .firstTextBaseline, spacing: 12) {
            Text(title)
                .font(th.heading(24))
                .textCase(th.uppercase ? .uppercase : nil)
                .foregroundStyle(th.fg)
                .lineLimit(2)
            Spacer(minLength: 8)
            if let note = note, !note.isEmpty {
                Text(note)
                    .font(.caption)
                    .foregroundStyle(th.muted)
                    .lineLimit(1)
            }
            if let actionTitle = actionTitle, let action = action {
                Button(action: action) {
                    Text(actionTitle)
                        .font(.subheadline.weight(.semibold))
                        .foregroundStyle(th.fg)
                }
                .buttonStyle(.plain)
            }
        }
    }
}

struct Eyebrow: View {
    @EnvironmentObject var app: AppModel
    let text: String
    var pulsing: Bool = false

    var body: some View {
        let th = app.theme
        HStack(spacing: 8) {
            TimelineView(.animation(minimumInterval: nil, paused: !pulsing)) { tl in
                let phase = pulsing ? abs(sin(tl.date.timeIntervalSinceReferenceDate * .pi / 1.4)) : 0
                Circle()
                    .fill(th.fg)
                    .frame(width: 7, height: 7)
                    .scaleEffect(1 + 0.6 * phase)
                    .opacity(pulsing ? 1 - 0.6 * phase : 0.4)
            }
            .frame(width: 12, height: 12)
            Text(text.uppercased())
                .font(.system(size: 11, weight: .semibold))
                .tracking(1.6)
                .foregroundStyle(th.muted)
                .lineLimit(1)
        }
    }
}

struct StatusText: View {
    @EnvironmentObject var app: AppModel
    let text: String
    var error: Bool = false

    var body: some View {
        Text(text)
            .font(.subheadline)
            .foregroundStyle(error ? app.theme.fg : app.theme.muted)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.vertical, 16)
            .padding(.horizontal, 8)
    }
}

// MARK: - buttons

/// `.mood` pill from the web: muted outline, filled card-2 when on.
struct Chip: View {
    @EnvironmentObject var app: AppModel
    let text: String
    var icon: String? = nil
    var on: Bool = false
    let action: () -> Void

    var body: some View {
        let th = app.theme
        let shape = RoundedRectangle(cornerRadius: th.pill ? 999 : th.buttonRadius, style: .continuous)
        Button(action: action) {
            HStack(spacing: 6) {
                if let icon = icon { Image(systemName: icon).font(.system(size: 13, weight: .semibold)) }
                Text(text).font(.system(size: 14, weight: .medium)).lineLimit(1)
            }
            .padding(.vertical, 9)
            .padding(.horizontal, 14)
            .foregroundStyle(on ? (th.uppercase && th.light ? th.bg : th.fg) : th.muted)
            .background {
                if on {
                    shape.fill(th.uppercase && th.light ? th.fg : th.card2)
                } else {
                    shape.fill(th.glass ? Color.clear : th.bg.opacity(0.6))
                }
            }
            .overlay(shape.stroke(on ? th.fg : th.line, lineWidth: th.border))
            .contentShape(shape)
        }
        .buttonStyle(.plain)
    }
}

/// `.btn` / `.btn.solid`.
struct PillButton: View {
    @EnvironmentObject var app: AppModel
    let title: String
    var icon: String? = nil
    var solid: Bool = false
    var busy: Bool = false
    let action: () -> Void

    var body: some View {
        let th = app.theme
        let shape = RoundedRectangle(cornerRadius: th.pill ? 999 : th.buttonRadius, style: .continuous)
        Button(action: action) {
            HStack(spacing: 8) {
                if busy {
                    ProgressView().tint(solid ? th.bg : th.fg).controlSize(.small)
                } else if let icon = icon {
                    Image(systemName: icon).font(.system(size: 14, weight: .semibold))
                }
                Text(title).font(.system(size: 15, weight: .semibold)).lineLimit(1)
            }
            .padding(.vertical, 11)
            .padding(.horizontal, 18)
            .foregroundStyle(solid ? th.bg : th.fg)
            .background(shape.fill(solid ? th.fg : Color.clear))
            .overlay(shape.stroke(solid ? th.fg : th.line, lineWidth: th.border))
            .contentShape(shape)
        }
        .buttonStyle(.plain)
    }
}

struct IconButton: View {
    @EnvironmentObject var app: AppModel
    let system: String
    var on: Bool = false
    var size: CGFloat = 20
    var disabled: Bool = false
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: system)
                .font(.system(size: size, weight: .medium))
                .foregroundStyle(on ? app.theme.fg : app.theme.muted)
                .frame(width: 40, height: 40)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(disabled)
        .opacity(disabled ? 0.3 : 1)
    }
}

/// Big round play knob.
struct PlayKnob: View {
    @EnvironmentObject var app: AppModel
    let playing: Bool
    var size: CGFloat = 64
    var busy: Bool = false
    let action: () -> Void

    var body: some View {
        let th = app.theme
        Button(action: action) {
            ZStack {
                Circle().fill(th.fg)
                if busy {
                    ProgressView().tint(th.bg)
                } else {
                    Image(systemName: playing ? "pause.fill" : "play.fill")
                        .font(.system(size: size * 0.36, weight: .bold))
                        .foregroundStyle(th.bg)
                        .offset(x: playing ? 0 : size * 0.04)
                }
            }
            .frame(width: size, height: size)
            .shadow(color: th.fg.opacity(0.18), radius: 20, y: 10)
        }
        .buttonStyle(.plain)
    }
}

// MARK: - toasts

struct ToastStack: View {
    @EnvironmentObject var app: AppModel

    var body: some View {
        let th = app.theme
        VStack(alignment: .trailing, spacing: 8) {
            ForEach(app.toasts) { t in
                VStack(alignment: .leading, spacing: 8) {
                    Text(t.text)
                        .font(.subheadline)
                        .foregroundStyle(th.bg)
                        .fixedSize(horizontal: false, vertical: true)
                    if let p = t.progress {
                        GeometryReader { geo in
                            ZStack(alignment: .leading) {
                                Capsule().fill(th.bg.opacity(0.25))
                                Capsule().fill(th.bg).frame(width: geo.size.width * CGFloat(p))
                            }
                        }
                        .frame(height: 3)
                    }
                }
                .padding(.vertical, 12)
                .padding(.horizontal, 16)
                .frame(maxWidth: 360, alignment: .leading)
                .background(RoundedRectangle(cornerRadius: min(th.radius, 12), style: .continuous).fill(th.fg))
                .shadow(color: .black.opacity(0.3), radius: 15, y: 8)
                .transition(.move(edge: .bottom).combined(with: .opacity))
                .onTapGesture { app.closeToast(t.id) }
            }
        }
        .padding(.horizontal, 16)
        .frame(maxWidth: .infinity, alignment: .trailing)
        .animation(.easeOut(duration: 0.25), value: app.toasts)
    }
}

// MARK: - helpers

struct HScroll<Content: View>: View {
    let spacing: CGFloat
    let content: () -> Content

    init(spacing: CGFloat = 14, @ViewBuilder content: @escaping () -> Content) {
        self.spacing = spacing
        self.content = content
    }

    var body: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(alignment: .top, spacing: spacing) {
                content()
            }
            .padding(.horizontal, 16)
        }
        .padding(.horizontal, -16)
    }
}

/// Flow layout for chips that wrap onto several lines.
struct FlowLayout: Layout {
    var spacing: CGFloat = 8

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let maxW = proposal.width ?? 360
        var x: CGFloat = 0
        var y: CGFloat = 0
        var rowH: CGFloat = 0
        var widest: CGFloat = 0
        for v in subviews {
            let s = v.sizeThatFits(.unspecified)
            if x > 0 && x + s.width > maxW {
                y += rowH + spacing
                x = 0
                rowH = 0
            }
            x += s.width + spacing
            rowH = max(rowH, s.height)
            widest = max(widest, x - spacing)
        }
        return CGSize(width: min(maxW, widest), height: y + rowH)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var x = bounds.minX
        var y = bounds.minY
        var rowH: CGFloat = 0
        for v in subviews {
            let s = v.sizeThatFits(.unspecified)
            if x > bounds.minX && x + s.width > bounds.maxX {
                y += rowH + spacing
                x = bounds.minX
                rowH = 0
            }
            v.place(at: CGPoint(x: x, y: y), proposal: ProposedViewSize(s))
            x += s.width + spacing
            rowH = max(rowH, s.height)
        }
    }
}

/// Monochrome text field in the theme style.
struct EchoField: View {
    @EnvironmentObject var app: AppModel
    let placeholder: String
    @Binding var text: String
    var icon: String? = nil

    var body: some View {
        let th = app.theme
        let shape = RoundedRectangle(cornerRadius: th.pill ? 999 : th.buttonRadius, style: .continuous)
        HStack(spacing: 10) {
            if let icon = icon {
                Image(systemName: icon).foregroundStyle(th.muted)
            }
            TextField("", text: $text, prompt: Text(placeholder).foregroundColor(th.muted))
                .foregroundStyle(th.fg)
                .autocorrectionDisabled()
                .textInputAutocapitalization(.never)
            if !text.isEmpty {
                Button {
                    text = ""
                } label: {
                    Image(systemName: "xmark.circle.fill").foregroundStyle(th.muted)
                }
                .buttonStyle(.plain)
            }
        }
        .padding(.vertical, 12)
        .padding(.horizontal, 16)
        .background {
            if th.glass { shape.fill(.ultraThinMaterial) } else { shape.fill(th.card) }
        }
        .overlay(shape.stroke(th.line, lineWidth: th.border))
    }
}
