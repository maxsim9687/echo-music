import SwiftUI

/// Вход по почте: email → 6-digit code (one-time-code autofill) → logged in.
struct LoginView: View {
    @EnvironmentObject var app: AppModel
    @Environment(\.dismiss) private var dismiss
    var inSheet: Bool = true

    @State private var email = ""
    @State private var code = ""
    @State private var step = 0
    @State private var note = ""
    @State private var busy = false
    @FocusState var codeFocused: Bool

    var body: some View {
        let th = app.theme
        let fieldShape = RoundedRectangle(cornerRadius: min(th.radius, 14), style: .continuous)
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                Text("Вход по почте")
                    .font(th.display(34))
                    .foregroundStyle(th.fg)
                    .padding(.top, 8)
                Text("Пришлём шестизначный код. Пароль не нужен.")
                    .font(.system(size: 15))
                    .foregroundStyle(th.muted)

                VStack(alignment: .leading, spacing: 6) {
                    Text("Почта").font(.system(size: 13, weight: .medium)).foregroundStyle(th.muted)
                    TextField("", text: $email, prompt: Text("you@example.com").foregroundColor(th.muted))
                        .textContentType(.emailAddress)
                        .keyboardType(.emailAddress)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .disabled(step == 1)
                        .foregroundStyle(step == 1 ? th.muted : th.fg)
                        .padding(14)
                        .background(fieldShape.fill(th.card))
                        .overlay(fieldShape.stroke(th.line, lineWidth: th.border))
                }

                if step == 1 {
                    VStack(alignment: .leading, spacing: 6) {
                        Text("Код из письма").font(.system(size: 13, weight: .medium)).foregroundStyle(th.muted)
                        TextField("", text: $code, prompt: Text("000000").foregroundColor(th.muted))
                            .textContentType(.oneTimeCode)
                            .keyboardType(.numberPad)
                            .font(.system(size: 22, weight: .semibold).monospacedDigit())
                            .foregroundStyle(th.fg)
                            .focused($codeFocused)
                            .padding(14)
                            .background(fieldShape.fill(th.card))
                            .overlay(fieldShape.stroke(th.line, lineWidth: th.border))
                            .onChange(of: code) { _, v in
                                let digits = String(v.filter { $0.isNumber }.prefix(6))
                                if digits != v { code = digits }
                                if digits.count == 6 && !busy { submit() }
                            }
                    }
                }

                if !note.isEmpty {
                    Text(note)
                        .font(.system(size: 13))
                        .foregroundStyle(th.fg)
                        .fixedSize(horizontal: false, vertical: true)
                }

                HStack(spacing: 14) {
                    PillButton(title: step == 0 ? "Получить код" : "Войти", solid: true, busy: busy) { submit() }
                        .disabled(busy)
                    if step == 1 {
                        Button("Другая почта") { reset() }
                            .font(.system(size: 14, weight: .medium))
                            .foregroundStyle(th.muted)
                            .buttonStyle(.plain)
                    }
                }
            }
            .padding(.horizontal, 20)
            .padding(.bottom, 30)
        }
        .screenBackground()
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            if inSheet {
                ToolbarItem(placement: .topBarTrailing) {
                    Button("Закрыть") { dismiss() }.foregroundStyle(th.fg)
                }
            }
        }
    }

    private func reset() {
        step = 0
        code = ""
        note = ""
    }

    private func submit() {
        let mail = email.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !busy else { return }
        guard mail.contains("@") else {
            note = "Введите почту"
            return
        }
        busy = true
        Task { @MainActor in
            do {
                if step == 0 {
                    note = "Отправляем код…"
                    try await app.requestCode(email: mail)
                    step = 1
                    note = "Письмо отправлено на \(mail). Введите код из письма. Проверьте и папку «Спам»."
                    codeFocused = true
                } else {
                    try await app.verify(email: mail, code: code)
                    reset()
                    dismiss()
                }
            } catch {
                note = error.localizedDescription
            }
            busy = false
        }
    }
}
