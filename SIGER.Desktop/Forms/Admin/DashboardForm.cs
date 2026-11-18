using System.Globalization;
using System.Windows.Forms.Automation;
using SIGER.Desktop.Controls;
using SIGER.Desktop.Services;
using SIGER.Domain.Enums;

namespace SIGER.Desktop.Forms.Admin;

internal sealed class DashboardForm : UserControl
{
    private readonly TableApiService tables;
    private readonly KitchenApiService kitchen;
    private readonly OrderApiService orders;
    private readonly ReservationApiService reservations;
    private readonly ReportApiService reports;

    private readonly Button refreshButton = DesktopTheme.Button("&Actualizar resumen", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);
    private readonly Label updatedAtLabel = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Muted);
    private readonly Label feedback = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Error);
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Height = 5, Dock = DockStyle.Fill };

    private readonly Label tablesSummaryLabel = DesktopTheme.Label("Mesas: —", DesktopTheme.Strong, DesktopTheme.Ink);
    private readonly Label ordersSummaryLabel = DesktopTheme.Label("Órdenes en preparación/cola: —", DesktopTheme.Strong, DesktopTheme.Ink);
    private readonly Label reservationsSummaryLabel = DesktopTheme.Label("Reservaciones registradas: —", DesktopTheme.Strong, DesktopTheme.Ink);
    private readonly Label salesTodaySummaryLabel = DesktopTheme.Label("Ventas del día actual: —", DesktopTheme.Strong, DesktopTheme.Ink);
    private readonly DataGridView upcomingReservationsGrid = new();

    private CancellationTokenSource? pendingOperation;

    internal DashboardForm(
        TableApiService tables,
        KitchenApiService kitchen,
        OrderApiService orders,
        ReservationApiService reservations,
        ReportApiService reports)
    {
        this.tables = tables;
        this.kitchen = kitchen;
        this.orders = orders;
        this.reservations = reservations;
        this.reports = reports;

        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = DesktopTheme.Cream;
        Font = DesktopTheme.Body;
        Margin = Padding.Empty;

        var root = Stack();
        root.Dock = DockStyle.Top;
        root.Controls.Add(BuildToolbar());
        root.Controls.Add(DesktopTheme.FeedbackBar(feedback, progress, "Carga del resumen administrativo en curso"));
        root.Controls.Add(BuildMetricsCard());
        root.Controls.Add(BuildUpcomingReservationsCard());
        Controls.Add(root);

        refreshButton.Click += async (_, _) => await RefreshDashboardAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            pendingOperation?.Cancel();
            pendingOperation?.Dispose();
        }
        base.Dispose(disposing);
    }

    internal async Task RefreshDashboardAsync()
    {
        if (pendingOperation is not null) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            var localToday = DateTime.Now.Date;
            var todayStart = new DateTimeOffset(localToday, TimeZoneInfo.Local.GetUtcOffset(localToday));
            var todayEnd = new DateTimeOffset(localToday.AddDays(1).AddTicks(-1), TimeZoneInfo.Local.GetUtcOffset(localToday));

            var tablesPageTask = tables.GetPagedAsync(1, 200, cts.Token);
            var availableTablesTask = tables.GetAvailableAsync(cts.Token);
            var kitchenQueueTask = kitchen.GetOrdersAsync(cts.Token);
            var pendingOrdersPageTask = orders.GetPagedAsync(new OrderQueryFilter(1, 1, Status: OrderStatus.Pending), cts.Token);
            var pendingReservationsTask = reservations.GetPagedAsync(1, 20, status: ReservationStatus.Pending, cancellationToken: cts.Token);
            var confirmedReservationsTask = reservations.GetPagedAsync(1, 20, status: ReservationStatus.Confirmed, cancellationToken: cts.Token);
            var salesTodayTask = reports.GetSalesReportAsync(todayStart, todayEnd, cts.Token);

            await Task.WhenAll(
                tablesPageTask,
                availableTablesTask,
                kitchenQueueTask,
                pendingOrdersPageTask,
                pendingReservationsTask,
                confirmedReservationsTask,
                salesTodayTask);

            if (cts.IsCancellationRequested || IsDisposed) return;

            var tablesPage = tablesPageTask.Result;
            var availableCount = availableTablesTask.Result.Count;
            var occupiedInfo = tablesPage.TotalCount <= tablesPage.PageSize
                ? $"{tablesPage.Items.Count(t => t.Status == TableStatus.Occupied)} ocupadas"
                : "consulta el módulo Mesas para el detalle por página";
            tablesSummaryLabel.Text =
                $"Mesas totales (TotalCount): {tablesPage.TotalCount}   |   Disponibles: {availableCount}   |   Estado operativo: {occupiedInfo}";

            var queue = kitchenQueueTask.Result;
            var pendingCount = pendingOrdersPageTask.Result.TotalCount;
            var inPrepCount = queue.Count(o => o.Status == OrderStatus.InPreparation);
            var readyCount = queue.Count(o => o.Status == OrderStatus.Ready);
            ordersSummaryLabel.Text =
                $"Cola de cocina activa: {queue.Count} órdenes   |   Pendientes (TotalCount): {pendingCount}   |   En preparación: {inPrepCount}   |   Listas para servir: {readyCount}";

            var pendingRes = pendingReservationsTask.Result;
            var confirmedRes = confirmedReservationsTask.Result;
            reservationsSummaryLabel.Text =
                $"Reservaciones pendientes (TotalCount): {pendingRes.TotalCount}   |   Confirmadas (TotalCount): {confirmedRes.TotalCount}";

            var salesToday = salesTodayTask.Result;
            salesTodaySummaryLabel.Text =
                $"Ventas del día ({localToday:d}, por fecha de orden pagada): {salesToday.TotalOrders} órdenes pagadas   |   Ingresos: {salesToday.TotalRevenue:C2}";

            var nowUtc = DateTimeOffset.UtcNow;
            var upcoming = pendingRes.Items.Concat(confirmedRes.Items)
                .Where(r => r.ReservationDateTime >= nowUtc.AddHours(-2))
                .OrderBy(r => r.ReservationDateTime)
                .Take(15)
                .ToArray();

            upcomingReservationsGrid.Rows.Clear();
            foreach (var r in upcoming)
            {
                var guest = !string.IsNullOrWhiteSpace(r.ContactName)
                    ? $"{r.ContactName} ({r.ContactPhone ?? "sin tel."})"
                    : r.UserId.HasValue ? $"Usuario #{r.UserId.Value}" : "Sin contacto";
                var tableText = r.TableNumber.HasValue ? $"Mesa #{r.TableNumber.Value:00}" : $"Mesa #{r.TableId}";
                upcomingReservationsGrid.Rows.Add(
                    $"#{r.Id}",
                    r.ReservationDateTime.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    tableText,
                    r.NumberOfPeople.ToString(CultureInfo.InvariantCulture),
                    ReservationApiService.FormatStatus(r.Status),
                    guest);
            }

            updatedAtLabel.Text = $"Actualizado: {DateTime.Now:T}";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (ApiException error)
        {
            if (!cts.IsCancellationRequested && !IsDisposed)
                ShowFeedback(error.Message, isError: true);
        }
        finally
        {
            pendingOperation = null;
            if (!IsDisposed) SetBusy(false);
        }
    }

    private Control BuildToolbar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            BackColor = DesktopTheme.Paper,
            Padding = new Padding(20, 14, 20, 14),
            Margin = new Padding(0, 0, 0, 12)
        };
        refreshButton.Margin = new Padding(0, 0, 16, 0);
        updatedAtLabel.Margin = new Padding(0, 11, 0, 0);
        bar.Controls.Add(refreshButton);
        bar.Controls.Add(updatedAtLabel);
        return bar;
    }

    private Control BuildMetricsCard()
    {
        var card = Stack();
        card.Dock = DockStyle.Top;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(20);
        card.Margin = new Padding(0, 0, 0, 16);

        card.Controls.Add(new Panel { Size = new Size(34, 3), BackColor = DesktopTheme.Olive, Margin = new Padding(0, 0, 0, 10) });
        var heading = DesktopTheme.Label("Indicadores operativos actuales (fuente: SIGER.API)", DesktopTheme.Brand, DesktopTheme.Ink);
        heading.Margin = new Padding(0, 0, 0, 14);
        card.Controls.Add(heading);

        tablesSummaryLabel.Margin = new Padding(0, 0, 0, 10);
        ordersSummaryLabel.Margin = new Padding(0, 0, 0, 10);
        reservationsSummaryLabel.Margin = new Padding(0, 0, 0, 10);
        salesTodaySummaryLabel.Margin = new Padding(0, 0, 0, 4);

        card.Controls.Add(tablesSummaryLabel);
        card.Controls.Add(ordersSummaryLabel);
        card.Controls.Add(reservationsSummaryLabel);
        card.Controls.Add(salesTodaySummaryLabel);
        return card;
    }

    private Control BuildUpcomingReservationsCard()
    {
        var card = Stack();
        card.Dock = DockStyle.Top;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(20);
        card.Margin = Padding.Empty;

        var heading = DesktopTheme.Label("Próximas reservaciones (Pendientes y Confirmadas)", DesktopTheme.Strong, DesktopTheme.Ink);
        heading.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(heading);

        DesktopTheme.ConfigureGrid(upcomingReservationsGrid, "Próximas reservaciones", 220);

        upcomingReservationsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", FillWeight = 12 });
        upcomingReservationsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DateTime", HeaderText = "Fecha y hora", FillWeight = 22 });
        upcomingReservationsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Table", HeaderText = "Mesa", FillWeight = 15 });
        upcomingReservationsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "People", HeaderText = "Personas", FillWeight = 12 });
        upcomingReservationsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Estado", FillWeight = 16 });
        upcomingReservationsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Contact", HeaderText = "Cliente / Invitado", FillWeight = 28 });

        card.Controls.Add(upcomingReservationsGrid);
        return card;
    }

    private void SetBusy(bool busy)
    {
        refreshButton.Enabled = upcomingReservationsGrid.Enabled = !busy;
        progress.Visible = busy;
        UseWaitCursor = busy;
    }

    private void ShowFeedback(string message, bool isError) =>
        DesktopTheme.ShowFeedback(feedback, message, isError);

    private static TableLayoutPanel Stack() => DesktopTheme.Stack();
}
