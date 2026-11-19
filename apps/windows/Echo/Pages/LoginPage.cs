using Echo.Core;
using Echo.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Echo.Pages;

// Вход по почте: email → 6-digit code → logged in.
public sealed class LoginPage : PageBase
{
    readonly TextBox email, code;
    readonly StackPanel codeWrap;
    readonly TextBlock note;
    readonly Button go;
    readonly Button back;
    string step = "email";

    public LoginPage() : base(560)
    {
        email = null!; code = null!; codeWrap = null!; note = null!; go = null!; back = null!;
        if (Account.Me != null) { BuildAccount(); return; }
        var s = new StackPanel { Spacing = 14, Margin = new Thickness(0, 40, 0, 0) };
        s.Children.Add(U.Display("Вход по почте", 44));
        s.Children.Add(U.Muted("Пришлём шестизначный код. Пароль не нужен.", 15, true));
        s.Children.Add(U.Muted("Почта", 13));
        email = U.Input("you@example.com", Settings.Current.LastEmail);
        email.InputScope = Scope(InputScopeNameValue.EmailSmtpAddress);
        s.Children.Add(email);
        codeWrap = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        codeWrap.Children.Add(U.Muted("Код из письма", 13));
        code = U.Input("000000");
        code.MaxLength = 6; code.InputScope = Scope(InputScopeNameValue.Number);
        code.FontSize = 22; code.CharacterSpacing = 300;
        codeWrap.Children.Add(code);
        s.Children.Add(codeWrap);
        note = U.Muted("", 13, true);
        s.Children.Add(note);
        go = U.Pill("Получить код", () => _ = Submit(), solid: true);
        back = U.Link("Другая почта", Reset);
        back.Visibility = Visibility.Collapsed;
        s.Children.Add(U.H(14, go, back));
        email.KeyDown += (o, e) => { if (e.Key == VirtualKey.Enter) { e.Handled = true; _ = Submit(); } };
        code.KeyDown += (o, e) => { if (e.Key == VirtualKey.Enter) { e.Handled = true; _ = Submit(); } };
        code.TextChanged += (o, e) => { if (code.Text.Trim().Length == 6 && step == "code" && go.IsEnabled) _ = Submit(); };
        Body.Children.Add(s);
        Loaded += (o, e) => email.Focus(FocusState.Programmatic);
    }

    static InputScope Scope(InputScopeNameValue v)
    {
        var sc = new InputScope();
        sc.Names.Add(new InputScopeName(v));
        return sc;
    }

    void BuildAccount()
    {
        var s = new StackPanel { Spacing = 14, Margin = new Thickness(0, 40, 0, 0) };
        s.Children.Add(U.Display("Вы вошли", 44));
        s.Children.Add(U.Bold(Account.Me!.Email, 18));
        s.Children.Add(U.Muted(Account.IsAdmin ? "администратор" : "слушатель", 14));
        s.Children.Add(U.H(12, U.Pill("К моей волне", () => Shell.Main.Go("home"), solid: true), U.Pill("Выйти", () => _ = Shell.Main.Logout())));
        Body.Children.Add(s);
    }

    void SetGo(string t) { if (go.Content is TextBlock tb) tb.Text = t; }

    void Reset()
    {
        step = "email"; codeWrap.Visibility = Visibility.Collapsed; email.IsReadOnly = false; back.Visibility = Visibility.Collapsed;
        SetGo("Получить код"); note.Text = ""; code.Text = "";
        email.Focus(FocusState.Programmatic);
    }

    async Task Submit()
    {
        var mail = email.Text.Trim();
        if (mail.Length == 0 || !mail.Contains('@')) { note.Text = "Введите почту"; return; }
        go.IsEnabled = false;
        try
        {
            if (step == "email")
            {
                note.Text = "Отправляем код…";
                await Account.RequestCode(mail);
                step = "code"; codeWrap.Visibility = Visibility.Visible; email.IsReadOnly = true; back.Visibility = Visibility.Visible;
                SetGo("Войти");
                note.Text = $"Письмо отправлено на {mail}. Введите код из письма. Проверьте и папку «Спам».";
                code.Focus(FocusState.Programmatic);
            }
            else
            {
                var c = new string(code.Text.Where(char.IsDigit).ToArray());
                var u = await Account.Verify(mail, c);
                Toast(u?.Role == "admin" ? "Вы вошли как администратор" : "Вы вошли");
                Wave.ResetPool();
                Shell.Main.Go(Recs.HasTaste() ? "home" : "welcome");
                return;
            }
        }
        catch (Exception e) { note.Text = e.Message; }
        go.IsEnabled = true;
    }
}
