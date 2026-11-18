using SIGER.Desktop.Controls;
using SIGER.Desktop.Forms.Admin;
using SIGER.Desktop.Forms.Cashier;
using SIGER.Desktop.Forms.Kitchen;
using SIGER.Desktop.Forms.Waiter;
using SIGER.Desktop.Models;
using SIGER.Desktop.Services;
using AdminTablesForm = SIGER.Desktop.Forms.Admin.TablesForm;
using WaiterTablesForm = SIGER.Desktop.Forms.Waiter.TablesForm;

namespace SIGER.Desktop;

internal partial class Form1 : Form
{
    private readonly RoleNavigation navigation;
    private readonly bool isAdmin;
    private readonly bool isWaiter;
    private readonly bool kitchenModuleEnabled;
    private readonly bool cashierModulesEnabled;
    private readonly Dictionary<NavigationModule, Button> moduleButtons = [];
    private readonly Label title = DesktopTheme.Label("", DesktopTheme.Display, DesktopTheme.Ink);
    private readonly Label description = DesktopTheme.Label("", DesktopTheme.Body, DesktopTheme.Muted);
    private readonly Control pendingCard;

    private readonly DashboardForm? dashboardForm;
    private readonly UsersForm? usersForm;
    private readonly CategoriesForm? categoriesForm;
    private readonly ProductsForm? productsForm;
    private readonly AdminTablesForm? adminTablesForm;
    private readonly PromotionsForm? promotionsForm;
    private readonly ReservationsForm? reservationsForm;
    private readonly ReportsForm? reportsForm;
    private readonly AuditsForm? auditsForm;

    private readonly WaiterTablesForm? waiterTablesForm;
    private readonly OrdersForm? ordersForm;
    private readonly KitchenOrdersForm? kitchenOrdersForm;
    private readonly CheckoutForm? checkoutForm;
    private readonly PaymentsForm? paymentsForm;

    internal event EventHandler? SignOutRequested;

    internal Form1(
        SessionIdentity identity,
        RoleNavigation navigation,
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
        this.navigation = navigation;
        isAdmin = string.Equals(identity.RoleName, "Administrador", StringComparison.Ordinal);
        isWaiter = string.Equals(identity.RoleName, "Mesero", StringComparison.Ordinal);
        kitchenModuleEnabled = identity.RoleName is "Cocinero" or "Administrador";
        cashierModulesEnabled = identity.RoleName is "Cajero" or "Administrador";
        pendingCard = BuildPendingCard();

        if (isAdmin)
        {
            dashboardForm = new DashboardForm(tables, kitchen, orders, reservations, reports) { Visible = false };
            usersForm = new UsersForm(users) { Visible = false };
            categoriesForm = new CategoriesForm(categories) { Visible = false };
            productsForm = new ProductsForm(products, categories) { Visible = false };
            adminTablesForm = new AdminTablesForm(tables) { Visible = false };
            promotionsForm = new PromotionsForm(promotions, products) { Visible = false };
            reportsForm = new ReportsForm(reports) { Visible = false };
            auditsForm = new AuditsForm(audits) { Visible = false };
        }

        if (isWaiter)
        {
            waiterTablesForm = new WaiterTablesForm(tables) { Visible = false };
            waiterTablesForm.CreateOrderRequested += async (_, table) =>
            {
                if (FindModule("Órdenes") is { } ordersModule && ordersForm is not null)
                {
                    SelectModuleView(ordersModule);
                    await ordersForm.PrepareNewOrderForTableAsync(table);
                }
            };
            waiterTablesForm.ViewTableOrdersRequested += async (_, table) =>
            {
                if (FindModule("Órdenes") is { } ordersModule && ordersForm is not null)
                {
                    SelectModuleView(ordersModule);
                    await ordersForm.FilterByTableAsync(table);
                }
            };
        }

        if (isWaiter || isAdmin)
        {
            ordersForm = new OrdersForm(tables, products, orders) { Visible = false };
            reservationsForm = new ReservationsForm(reservations, tables, identity.UserId) { Visible = false };
            reservationsForm.OpenOrdersModuleRequested += async (_, nav) =>
            {
                if (FindModule("Órdenes") is { } ordersModule && ordersForm is not null)
                {
                    SelectModuleView(ordersModule);
                    if (nav.LinkedOrder is { } order && order.Status != SIGER.Domain.Enums.OrderStatus.PreOrdered)
                        await ordersForm.OpenOrderAsync(order.Id, order.TableId ?? nav.Reservation.TableId);
                    else
                        await ordersForm.PrepareNewOrderForReservationAsync(
                            nav.Reservation.Id,
                            nav.Reservation.TableId,
                            nav.Reservation.TableNumber);
                }
            };
        }

        if (kitchenModuleEnabled && FindModule("Órdenes de cocina") is not null)
        {
            kitchenOrdersForm = new KitchenOrdersForm(kitchen) { Visible = false };
        }

        if (cashierModulesEnabled && (FindModule("Cobros") is not null || FindModule("Pagos") is not null))
        {
            checkoutForm = new CheckoutForm(orders, payments) { Visible = false };
            paymentsForm = new PaymentsForm(orders, payments) { Visible = false };

            paymentsForm.OpenCheckoutRequested += async (_, order) =>
            {
                if (FindModule("Cobros") is { } checkoutModule)
                {
                    SelectModuleView(checkoutModule);
                }
                else
                {
                    HideAllModuleViews();
                    title.Text = "Cobros";
                    description.Text = "Órdenes listas para registrar el pago.";
                    checkoutForm.Visible = true;
                }
                await checkoutForm.LoadOrderForCheckoutAsync(order.Id);
            };

            checkoutForm.PaymentCompleted += (_, completed) =>
            {
                paymentsForm.RecordKnownPayment(completed.Order, completed.Payment);
            };
        }

        InitializeComponent();

        var layout = Grid(2, 1);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 246));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(BuildSidebar(), 0, 0);
        layout.Controls.Add(BuildWorkspace(identity.DisplayName), 1, 0);
        Controls.Add(layout);
        OpenModule(navigation.Modules[0]);
    }

    private Control BuildSidebar()
    {
        var sidebar = Grid(1, 3);
        sidebar.BackColor = DesktopTheme.Sidebar;
        sidebar.Padding = new Padding(20, 24, 20, 20);
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var brand = Stack();
        brand.Controls.Add(DesktopTheme.Label("Superior", DesktopTheme.Brand, DesktopTheme.Cream));
        brand.Controls.Add(DesktopTheme.Label("Kitchen Essentials", DesktopTheme.BrandSubtitle, DesktopTheme.SoftOlive));
        brand.Controls.Add(new Panel { Size = new Size(34, 3), BackColor = DesktopTheme.Olive, Margin = new Padding(0, 16, 0, 22) });
        sidebar.Controls.Add(brand, 0, 0);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        var modules = Stack();
        modules.Dock = DockStyle.Top;
        modules.AccessibleName = "Módulos para " + navigation.RoleLabel;
        foreach (var module in navigation.Modules)
        {
            var button = DesktopTheme.Button(module.Title, DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);
            button.Dock = DockStyle.Fill;
            button.MinimumSize = new Size(0, 35);
            button.Padding = new Padding(12, 2, 6, 2);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Margin = new Padding(0, 0, 3, 3);
            button.Click += (_, _) => OpenModule(module);
            moduleButtons.Add(module, button);
            modules.Controls.Add(button);
        }
        scroll.Controls.Add(modules);
        sidebar.Controls.Add(scroll, 0, 1);

        var logout = DesktopTheme.Button("Cerrar sesión", DesktopTheme.SidebarHover, DesktopTheme.Cream, DesktopTheme.Muted);
        logout.Dock = DockStyle.Fill;
        logout.Margin = new Padding(0, 16, 0, 0);
        logout.Click += (_, _) => SignOutRequested?.Invoke(this, EventArgs.Empty);
        sidebar.Controls.Add(logout, 0, 2);
        return sidebar;
    }

    private Control BuildWorkspace(string displayName)
    {
        var workspace = Grid(1, 2);
        workspace.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.BackColor = DesktopTheme.Cream;

        var header = Grid(2, 1);
        header.AutoSize = true;
        header.Dock = DockStyle.Top;
        header.BackColor = DesktopTheme.Paper;
        header.Padding = new Padding(32, 14, 32, 14);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var app = DesktopTheme.Label("SIGER Desktop", DesktopTheme.Strong, DesktopTheme.Muted);
        app.Anchor = AnchorStyles.Left;
        header.Controls.Add(app, 0, 0);

        var profile = Stack();
        profile.Anchor = AnchorStyles.Right;
        var name = DesktopTheme.Label(displayName, DesktopTheme.Strong, DesktopTheme.Ink);
        name.UseMnemonic = false;
        name.MaximumSize = new Size(216, 0);
        name.Anchor = AnchorStyles.Right;
        profile.Controls.Add(name);
        var role = DesktopTheme.Label(navigation.RoleLabel, DesktopTheme.Small, DesktopTheme.Muted);
        role.Anchor = AnchorStyles.Right;
        profile.Controls.Add(role);
        header.Controls.Add(profile, 1, 0);
        workspace.Controls.Add(header, 0, 0);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        var content = Stack();
        content.Dock = DockStyle.Top;
        content.Padding = new Padding(32, 30, 32, 30);
        description.Margin = new Padding(0, 10, 0, 24);
        content.Controls.Add(title);
        content.Controls.Add(description);
        content.Controls.Add(pendingCard);
        if (dashboardForm is not null) content.Controls.Add(dashboardForm);
        if (usersForm is not null) content.Controls.Add(usersForm);
        if (categoriesForm is not null) content.Controls.Add(categoriesForm);
        if (productsForm is not null) content.Controls.Add(productsForm);
        if (adminTablesForm is not null) content.Controls.Add(adminTablesForm);
        if (promotionsForm is not null) content.Controls.Add(promotionsForm);
        if (reservationsForm is not null) content.Controls.Add(reservationsForm);
        if (reportsForm is not null) content.Controls.Add(reportsForm);
        if (auditsForm is not null) content.Controls.Add(auditsForm);
        if (waiterTablesForm is not null) content.Controls.Add(waiterTablesForm);
        if (ordersForm is not null) content.Controls.Add(ordersForm);
        if (kitchenOrdersForm is not null) content.Controls.Add(kitchenOrdersForm);
        if (checkoutForm is not null) content.Controls.Add(checkoutForm);
        if (paymentsForm is not null) content.Controls.Add(paymentsForm);
        scroll.Controls.Add(content);
        workspace.Controls.Add(scroll, 0, 1);
        return workspace;
    }

    private static Control BuildPendingCard()
    {
        var card = Stack();
        card.Dock = DockStyle.Top;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(24, 26, 24, 28);
        card.Margin = Padding.Empty;
        card.MinimumSize = new Size(0, 164);
        card.Controls.Add(new Panel { Size = new Size(32, 3), BackColor = DesktopTheme.Olive, Margin = new Padding(0, 0, 0, 10) });
        card.Controls.Add(DesktopTheme.Label("Módulo pendiente de implementación", DesktopTheme.Brand, DesktopTheme.Ink));
        return card;
    }

    private NavigationModule? FindModule(string moduleTitle) =>
        navigation.Modules.FirstOrDefault(m => string.Equals(m.Title, moduleTitle, StringComparison.Ordinal));

    private void HideAllModuleViews()
    {
        if (dashboardForm is not null) dashboardForm.Visible = false;
        if (usersForm is not null) usersForm.Visible = false;
        if (categoriesForm is not null) categoriesForm.Visible = false;
        if (productsForm is not null) productsForm.Visible = false;
        if (adminTablesForm is not null) adminTablesForm.Visible = false;
        if (promotionsForm is not null) promotionsForm.Visible = false;
        if (reservationsForm is not null) reservationsForm.Visible = false;
        if (reportsForm is not null) reportsForm.Visible = false;
        if (auditsForm is not null) auditsForm.Visible = false;
        if (waiterTablesForm is not null) waiterTablesForm.Visible = false;
        if (ordersForm is not null) ordersForm.Visible = false;
        if (kitchenOrdersForm is not null) kitchenOrdersForm.Visible = false;
        if (checkoutForm is not null) checkoutForm.Visible = false;
        if (paymentsForm is not null) paymentsForm.Visible = false;
        pendingCard.Visible = false;
    }

    private Control? ResolveViewForModule(string moduleTitle) => moduleTitle switch
    {
        "Dashboard" => dashboardForm,
        "Usuarios" => usersForm,
        "Categorías" => categoriesForm,
        "Productos" => productsForm,
        "Mesas" => isAdmin ? adminTablesForm : waiterTablesForm,
        "Reservaciones" => reservationsForm,
        "Órdenes" => ordersForm,
        "Pagos" => paymentsForm,
        "Cobros" => checkoutForm,
        "Promociones" => promotionsForm,
        "Reportes" => reportsForm,
        "Auditoría" => auditsForm,
        "Órdenes de cocina" => kitchenOrdersForm,
        _ => null
    };

    private void SelectModuleView(NavigationModule module)
    {
        if (!navigation.Allows(module)) return;

        title.Text = module.Title;
        description.Text = module.Description;
        foreach (var (item, button) in moduleButtons)
        {
            var active = item == module;
            button.BackColor = active ? DesktopTheme.SoftOlive : DesktopTheme.Sidebar;
            button.ForeColor = active ? DesktopTheme.Ink : DesktopTheme.Cream;
            button.FlatAppearance.BorderColor = button.BackColor;
            button.FlatAppearance.MouseOverBackColor = active ? DesktopTheme.Olive : DesktopTheme.SidebarHover;
            button.FlatAppearance.MouseDownBackColor = button.FlatAppearance.MouseOverBackColor;
            button.AccessibleDescription = active ? "Módulo actual" : null;
        }

        HideAllModuleViews();
        var activeView = ResolveViewForModule(module.Title);
        if (activeView is not null) activeView.Visible = true;
        else pendingCard.Visible = true;
    }

    private void OpenModule(NavigationModule module)
    {
        if (!navigation.Allows(module)) return;
        SelectModuleView(module);

        switch (module.Title)
        {
            case "Dashboard" when dashboardForm is not null:
                _ = dashboardForm.RefreshDashboardAsync();
                break;
            case "Usuarios" when usersForm is not null:
                _ = usersForm.EnsureLoadedAsync();
                break;
            case "Categorías" when categoriesForm is not null:
                _ = categoriesForm.RefreshCategoriesAsync();
                break;
            case "Productos" when productsForm is not null:
                _ = productsForm.EnsureLoadedAsync();
                break;
            case "Mesas" when isAdmin && adminTablesForm is not null:
                _ = adminTablesForm.RefreshTablesAsync();
                break;
            case "Mesas" when isWaiter && waiterTablesForm is not null:
                _ = waiterTablesForm.RefreshTablesAsync();
                break;
            case "Reservaciones" when reservationsForm is not null:
                _ = reservationsForm.EnsureLoadedAsync();
                break;
            case "Órdenes" when ordersForm is not null:
                _ = ordersForm.EnsureLoadedAsync();
                break;
            case "Pagos" when paymentsForm is not null:
                _ = paymentsForm.RefreshOrdersAsync(1);
                break;
            case "Cobros" when checkoutForm is not null:
                _ = checkoutForm.EnsureLoadedAsync();
                break;
            case "Promociones" when promotionsForm is not null:
                _ = promotionsForm.EnsureLoadedAsync();
                break;
            case "Reportes" when reportsForm is not null:
                _ = reportsForm.RunReportQueryAsync();
                break;
            case "Auditoría" when auditsForm is not null:
                _ = auditsForm.EnsureLoadedAsync();
                break;
            case "Órdenes de cocina" when kitchenOrdersForm is not null:
                _ = kitchenOrdersForm.RefreshOrdersAsync();
                break;
        }
    }

    private static TableLayoutPanel Grid(int columns, int rows) =>
        new() { ColumnCount = columns, RowCount = rows, Dock = DockStyle.Fill, Margin = Padding.Empty };

    private static TableLayoutPanel Stack() =>
        new() { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
}
