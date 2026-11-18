using System.ComponentModel.DataAnnotations;
using System.Windows.Forms.Automation;
using SIGER.Desktop.Controls;
using SIGER.Desktop.Services;

namespace SIGER.Desktop.Forms.Authentication;

internal sealed class LoginForm : Form
{
    private readonly AuthApiService auth;
    private readonly TextBox email = new() { AccessibleName = "Correo electrónico", MaxLength = 150 };
    private readonly TextBox password = new() { AccessibleName = "Contraseña", UseSystemPasswordChar = true };
    private readonly CheckBox reveal = new() { Text = "Mostrar contraseña", AutoSize = true };
    private readonly Button signIn = DesktopTheme.Button("Iniciar sesión", DesktopTheme.Olive, DesktopTheme.Ink, DesktopTheme.SoftOlive);
    private readonly Label feedback = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Error);
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Height = 5 };
    private CancellationTokenSource? pendingLogin;

    internal event EventHandler? Authenticated;

    internal LoginForm(AuthApiService auth, string? notice = null)
    {
        this.auth = auth;
        Text = "Iniciar sesión | Superior Kitchen Essentials";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 555);
        ClientSize = new Size(970, 595);
        Font = DesktopTheme.Body;
        BackColor = DesktopTheme.Paper;

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.Controls.Add(BuildBrand(), 0, 0);
        split.Controls.Add(BuildSignIn(), 1, 0);
        Controls.Add(split);

        if (notice is not null) ShowFeedback(notice, isError: false);
        AcceptButton = signIn;
        ActiveControl = email;
        reveal.CheckedChanged += (_, _) => password.UseSystemPasswordChar = !reveal.Checked;
        signIn.Click += SignInAsync;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!e.Cancel) pendingLogin?.Cancel();
    }

    private static Control BuildBrand()
    {
        var brand = CenteredColumn(34);
        brand.BackColor = DesktopTheme.Sidebar;
        var content = Stack();
        content.Controls.Add(new Panel { BackColor = DesktopTheme.Olive, Size = new Size(42, 3), Margin = new Padding(0, 0, 0, 16) });
        content.Controls.Add(DesktopTheme.Label("Superior", DesktopTheme.Display, DesktopTheme.Cream));
        content.Controls.Add(DesktopTheme.Label("Kitchen Essentials", DesktopTheme.BrandSubtitle, DesktopTheme.SoftOlive));
        var caption = DesktopTheme.Label("Acceso para el equipo del restaurante.", DesktopTheme.Body, DesktopTheme.Cream);
        caption.Margin = new Padding(0, 24, 0, 0);
        content.Controls.Add(caption);
        brand.Controls.Add(content, 0, 1);
        return brand;
    }

    private Control BuildSignIn()
    {
        var host = CenteredColumn(30);
        var form = Stack();
        form.MaximumSize = new Size(380, 0);
        form.Anchor = AnchorStyles.None;
        form.MinimumSize = new Size(315, 0);
        form.Controls.Add(DesktopTheme.Label("Inicia sesión", DesktopTheme.Display, DesktopTheme.Ink));
        var hint = DesktopTheme.Label("Usa tu cuenta del restaurante.", DesktopTheme.Body, DesktopTheme.Muted);
        hint.Margin = new Padding(0, 8, 0, 25);
        form.Controls.Add(hint);
        AddField(form, "&Correo electrónico", email);
        AddField(form, "Contra&seña", password);
        reveal.ForeColor = DesktopTheme.Muted;
        reveal.Margin = new Padding(0, 5, 0, 15);
        form.Controls.Add(reveal);
        feedback.MaximumSize = new Size(310, 0);
        feedback.MinimumSize = new Size(0, 45);
        feedback.AccessibleRole = AccessibleRole.Alert;
        form.Controls.Add(feedback);
        signIn.Dock = DockStyle.Fill;
        signIn.Margin = Padding.Empty;
        form.Controls.Add(signIn);
        progress.Dock = DockStyle.Fill;
        progress.AccessibleName = "Inicio de sesión en curso";
        progress.Margin = new Padding(0, 8, 0, 0);
        form.Controls.Add(progress);
        host.Controls.Add(form, 0, 1);
        return host;
    }

    private static TableLayoutPanel Stack() => new()
    {
        ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty
    };

    private static TableLayoutPanel CenteredColumn(int padding)
    {
        var column = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(padding), Margin = Padding.Empty };
        column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        column.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        column.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        return column;
    }

    private static void AddField(TableLayoutPanel form, string title, TextBox input)
    {
        var label = DesktopTheme.Label(title, DesktopTheme.Strong, DesktopTheme.Ink);
        label.Margin = new Padding(0, 0, 0, 5);
        form.Controls.Add(label);
        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(0, 0, 0, 12);
        input.BorderStyle = BorderStyle.FixedSingle;
        input.BackColor = Color.White;
        input.ForeColor = DesktopTheme.Ink;
        input.Font = DesktopTheme.Body;
        form.Controls.Add(input);
    }

    private async void SignInAsync(object? sender, EventArgs e)
    {
        if (pendingLogin is not null) return;
        if (!new EmailAddressAttribute().IsValid(email.Text.Trim()) || email.Text.Trim().Length == 0)
        {
            ShowFeedback("Escribe un correo electrónico válido.", isError: true);
            email.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(password.Text))
        {
            ShowFeedback("Escribe tu contraseña.", isError: true);
            password.Focus();
            return;
        }

        using var request = new CancellationTokenSource();
        pendingLogin = request;
        SetBusy(true);
        ShowFeedback("", isError: false);
        var authenticated = false;
        try
        {
            await auth.SignInAsync(email.Text, password.Text, request.Token);
            authenticated = !request.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (ApiException exception)
        {
            if (!request.IsCancellationRequested) ShowFeedback(exception.Message, isError: true);
        }
        finally
        {
            pendingLogin = null;
            if (!IsDisposed)
            {
                password.Clear();
                reveal.Checked = false;
                SetBusy(false);
            }
        }

        if (authenticated && !IsDisposed) Authenticated?.Invoke(this, EventArgs.Empty);
        else if (!IsDisposed) password.Focus();
    }

    private void SetBusy(bool busy)
    {
        email.Enabled = password.Enabled = reveal.Enabled = signIn.Enabled = !busy;
        signIn.Text = busy ? "Iniciando sesión…" : "Iniciar sesión";
        progress.Visible = busy;
        UseWaitCursor = busy;
    }

    private void ShowFeedback(string message, bool isError)
    {
        feedback.ForeColor = isError ? DesktopTheme.Error : DesktopTheme.Muted;
        feedback.Text = message;
        if (message.Length > 0 && feedback.IsHandleCreated)
            feedback.AccessibilityObject.RaiseAutomationNotification(
                AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent, message);
    }
}
