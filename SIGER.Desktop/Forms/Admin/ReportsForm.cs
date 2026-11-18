using System.Globalization;
using System.Windows.Forms.Automation;
using SIGER.Application.DTOs.Reports;
using SIGER.Desktop.Controls;
using SIGER.Desktop.Services;

namespace SIGER.Desktop.Forms.Admin;

internal sealed class ReportsForm : UserControl
{
    private readonly ReportApiService reports;

    private readonly DateTimePicker startDatePicker = new() { Format = DateTimePickerFormat.Short, Width = 125 };
    private readonly DateTimePicker endDatePicker = new() { Format = DateTimePickerFormat.Short, Width = 125 };
    private readonly NumericUpDown limitInput = new() { Minimum = 1, Maximum = 200, Value = 10, Width = 80 };
    private readonly Button queryButton = DesktopTheme.Button("&Consultar reportes", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);

    private readonly Label feedback = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Error);
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Height = 5, Dock = DockStyle.Fill };

    private readonly Label salesSummaryLabel = DesktopTheme.Label("Selecciona un rango de fechas y pulsa «Consultar reportes».", DesktopTheme.BrandSubtitle, DesktopTheme.Ink);
    private readonly Label dateSemanticsNoteLabel = DesktopTheme.Label(
        "Semántica del backend: el reporte de ventas contabiliza órdenes en estado Pagada filtradas por la fecha de creación de la orden (OrderDateTime).",
        DesktopTheme.Small,
        DesktopTheme.Muted);
    private readonly DataGridView topProductsGrid = new();

    private CancellationTokenSource? pendingOperation;

    internal ReportsForm(ReportApiService reports)
    {
        this.reports = reports;
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = DesktopTheme.Cream;
        Font = DesktopTheme.Body;
        Margin = Padding.Empty;

        startDatePicker.Value = DateTime.Now.Date.AddDays(-7);
        endDatePicker.Value = DateTime.Now.Date;

        var root = Stack();
        root.Dock = DockStyle.Top;
        root.Controls.Add(BuildFilterBar());
        root.Controls.Add(DesktopTheme.FeedbackBar(feedback, progress, "Consulta de reportes en curso"));
        root.Controls.Add(BuildSummaryCard());
        root.Controls.Add(BuildTopProductsCard());
        Controls.Add(root);

        queryButton.Click += async (_, _) => await RunReportQueryAsync();
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

    internal async Task RunReportQueryAsync()
    {
        if (pendingOperation is not null) return;

        var localStart = startDatePicker.Value.Date;
        var localEnd = endDatePicker.Value.Date.AddDays(1).AddTicks(-1);
        if (localEnd < localStart)
        {
            ShowFeedback("La fecha final no puede ser anterior a la fecha inicial.", isError: true);
            return;
        }

        var startDto = new DateTimeOffset(localStart, TimeZoneInfo.Local.GetUtcOffset(localStart));
        var endDto = new DateTimeOffset(localEnd, TimeZoneInfo.Local.GetUtcOffset(localEnd));
        var limit = (int)limitInput.Value;

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            var salesTask = reports.GetSalesReportAsync(startDto, endDto, cts.Token);
            var topTask = reports.GetTopProductsAsync(startDto, endDto, limit, cts.Token);
            await Task.WhenAll(salesTask, topTask);

            if (cts.IsCancellationRequested || IsDisposed) return;

            var sales = salesTask.Result;
            var topProducts = topTask.Result;

            salesSummaryLabel.Text =
                $"Período {localStart:d} al {endDatePicker.Value.Date:d}   |   " +
                $"Órdenes pagadas: {sales.TotalOrders}   |   " +
                $"Ingresos totales: {sales.TotalRevenue:C2}";

            BindTopProducts(topProducts);
            if (sales.TotalOrders == 0 && topProducts.Count == 0)
                ShowFeedback("No se encontraron órdenes pagadas en el rango de fechas seleccionado.", isError: false);
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

    private void BindTopProducts(IReadOnlyCollection<TopSellingProductDto> items)
    {
        topProductsGrid.Rows.Clear();
        int rank = 1;
        foreach (var item in items)
        {
            topProductsGrid.Rows.Add(
                rank.ToString(CultureInfo.InvariantCulture),
                $"#{item.ProductId}",
                item.ProductName,
                item.QuantitySold.ToString(CultureInfo.InvariantCulture),
                item.Revenue.ToString("C2", CultureInfo.CurrentCulture));
            rank++;
        }
    }

    private Control BuildFilterBar()
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
        startDatePicker.AccessibleName = "Fecha inicial del reporte";
        endDatePicker.AccessibleName = "Fecha final del reporte";
        limitInput.AccessibleName = "Límite de productos más vendidos";

        bar.Controls.Add(LabeledField("Desde", startDatePicker));
        bar.Controls.Add(LabeledField("Hasta", endDatePicker));
        bar.Controls.Add(LabeledField("Top productos", limitInput));
        queryButton.Margin = new Padding(4, 22, 0, 0);
        bar.Controls.Add(queryButton);
        return bar;
    }

    private Control BuildSummaryCard()
    {
        var card = Stack();
        card.Dock = DockStyle.Top;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(20);
        card.Margin = new Padding(0, 0, 0, 16);

        card.Controls.Add(new Panel { Size = new Size(34, 3), BackColor = DesktopTheme.Olive, Margin = new Padding(0, 0, 0, 10) });
        salesSummaryLabel.Margin = new Padding(0, 0, 0, 8);
        dateSemanticsNoteLabel.MaximumSize = new Size(880, 0);
        card.Controls.Add(salesSummaryLabel);
        card.Controls.Add(dateSemanticsNoteLabel);
        return card;
    }

    private Control BuildTopProductsCard()
    {
        var card = Stack();
        card.Dock = DockStyle.Top;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(20);
        card.Margin = Padding.Empty;

        var heading = DesktopTheme.Label("Productos más vendidos en el período", DesktopTheme.Strong, DesktopTheme.Ink);
        heading.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(heading);

        DesktopTheme.ConfigureGrid(topProductsGrid, "Tabla de productos más vendidos", 250);

        topProductsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Rank", HeaderText = "#", FillWeight = 10 });
        topProductsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProductId", HeaderText = "ID Producto", FillWeight = 16 });
        topProductsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProductName", HeaderText = "Producto", FillWeight = 38 });
        topProductsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "QuantitySold", HeaderText = "Unidades vendidas", FillWeight = 18 });
        topProductsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Revenue", HeaderText = "Ingresos generados", FillWeight = 20 });

        card.Controls.Add(topProductsGrid);
        return card;
    }

    private static Control LabeledField(string caption, Control input) =>
        DesktopTheme.LabeledField(caption, input);

    private void SetBusy(bool busy)
    {
        startDatePicker.Enabled = endDatePicker.Enabled = limitInput.Enabled =
            queryButton.Enabled = topProductsGrid.Enabled = !busy;
        progress.Visible = busy;
        UseWaitCursor = busy;
    }

    private void ShowFeedback(string message, bool isError) =>
        DesktopTheme.ShowFeedback(feedback, message, isError);

    private static TableLayoutPanel Stack() => DesktopTheme.Stack();
}
