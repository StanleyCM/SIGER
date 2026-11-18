using SIGER.Desktop.Forms.Authentication;
using SIGER.Desktop.Models;
using SIGER.Desktop.Services;

namespace SIGER.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Uri baseAddress;
        try
        {
            baseAddress = ApiClient.ReadBaseAddress(Environment.GetEnvironmentVariable(ApiClient.BaseUrlVariable));
        }
        catch (ApiException error)
        {
            MessageBox.Show(error.Message, "Configuración de SIGER", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = baseAddress
        };
        var session = new SessionService(TimeProvider.System);
        var api = new ApiClient(http, session);
        var auth = new AuthApiService(api, session);
        var tables = new TableApiService(api);
        var products = new ProductApiService(api);
        var orders = new OrderApiService(api);
        var kitchen = new KitchenApiService(api);
        var payments = new PaymentApiService(api);
        var users = new UserApiService(api);
        var categories = new CategoryApiService(api);
        var promotions = new PromotionApiService(api);
        var reservations = new ReservationApiService(api);
        var reports = new ReportApiService(api);
        var audits = new AuditApiService(api);
        using var context = new DesktopContext(
            auth, session, tables, products, orders, kitchen, payments,
            users, categories, promotions, reservations, reports, audits);
        System.Windows.Forms.Application.Run(context);
    }
}

internal sealed class DesktopContext : ApplicationContext
{
    private readonly AuthApiService auth;
    private readonly SessionService session;
    private readonly TableApiService tables;
    private readonly ProductApiService products;
    private readonly OrderApiService orders;
    private readonly KitchenApiService kitchen;
    private readonly PaymentApiService payments;
    private readonly UserApiService users;
    private readonly CategoryApiService categories;
    private readonly PromotionApiService promotions;
    private readonly ReservationApiService reservations;
    private readonly ReportApiService reports;
    private readonly AuditApiService audits;
    private readonly System.Windows.Forms.Timer sessionMonitor = new() { Interval = 1000 };

    internal DesktopContext(
        AuthApiService auth,
        SessionService session,
        TableApiService tables,
        ProductApiService products,
        OrderApiService orders,
        KitchenApiService kitchen,
        PaymentApiService payments,
        UserApiService users,
        CategoryApiService categories,
        PromotionApiService promotions,
        ReservationApiService reservations,
        ReportApiService reports,
        AuditApiService audits)
    {
        this.auth = auth;
        this.session = session;
        this.tables = tables;
        this.products = products;
        this.orders = orders;
        this.kitchen = kitchen;
        this.payments = payments;
        this.users = users;
        this.categories = categories;
        this.promotions = promotions;
        this.reservations = reservations;
        this.reports = reports;
        this.audits = audits;
        sessionMonitor.Tick += CheckSession;
        ShowLogin();
    }

    private void CheckSession(object? sender, EventArgs e)
    {
        if (MainForm is Form1 && session.Current is null)
            ShowLogin("Tu sesión terminó. Inicia sesión de nuevo.");
    }

    private void ShowLogin(string? notice = null)
    {
        sessionMonitor.Stop();
        session.SignOut();
        var login = new LoginForm(auth, notice);
        login.Authenticated += (_, _) =>
        {
            if (session.Current is not { } identity || RoleNavigation.For(identity.RoleName) is not { } navigation)
            {
                ShowLogin("Tu sesión terminó. Inicia sesión de nuevo.");
                return;
            }

            var shell = new Form1(
                identity, navigation, tables, products, orders, kitchen, payments,
                users, categories, promotions, reservations, reports, audits);
            shell.SignOutRequested += (_, _) => ShowLogin("Cerraste sesión.");
            SwitchTo(shell);
            sessionMonitor.Start();
        };
        SwitchTo(login);
    }

    private void SwitchTo(Form window)
    {
        var previous = MainForm;
        MainForm = window;
        window.Show();
        previous?.Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            sessionMonitor.Dispose();
            session.SignOut();
        }
        base.Dispose(disposing);
    }
}
