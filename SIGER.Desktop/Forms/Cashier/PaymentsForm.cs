using System.Globalization;
using System.Windows.Forms.Automation;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.DTOs.Payments;
using SIGER.Desktop.Controls;
using SIGER.Desktop.Services;
using SIGER.Domain.Enums;

namespace SIGER.Desktop.Forms.Cashier;

internal sealed class PaymentsForm : UserControl
{
    private const int DefaultPageSize = 20;

    private readonly OrderApiService orders;
    private readonly PaymentApiService payments;

    // Filter & pager controls
    private readonly ComboBox statusFilterCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly NumericUpDown orderIdFilter = new() { Minimum = 0, Maximum = 9999999, Value = 0, Width = 100 };
    private readonly Button searchButton = DesktopTheme.Button("&Buscar / Actualizar", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);
    private readonly Button clearFiltersButton = DesktopTheme.Button("Limpiar filtros", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button prevPageButton = DesktopTheme.Button("◀ Anterior", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button nextPageButton = DesktopTheme.Button("Siguiente ▶", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Label pageStatusLabel = DesktopTheme.Label("Página 1 de 1", DesktopTheme.Small, DesktopTheme.Muted);

    // Feedback & progress
    private readonly Label feedback = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Error);
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Height = 5, Dock = DockStyle.Fill };

    // Orders grid
    private readonly DataGridView ordersGrid = new();

    // Selected order & payment detail card
    private readonly Label selectedOrderTitle = DesktopTheme.Label("Selecciona una orden del listado", DesktopTheme.Brand, DesktopTheme.Ink);
    private readonly Label selectedOrderMeta = DesktopTheme.Label("", DesktopTheme.Strong, DesktopTheme.Muted);
    private readonly Label paymentSummaryLabel = DesktopTheme.Label("", DesktopTheme.Body, DesktopTheme.Ink);
    private readonly Label operationalHintLabel = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Muted);
    private readonly Button verifyOrderPaymentButton = DesktopTheme.Button("&Consultar orden y pago en servidor", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button openCheckoutButton = DesktopTheme.Button("&Ir a Cobros con esta orden", DesktopTheme.Olive, DesktopTheme.Ink, DesktopTheme.SoftOlive);

    private readonly Dictionary<long, PaymentDto?> knownPayments = [];
    private IReadOnlyList<OrderDto> currentOrders = [];
    private OrderDto? selectedOrder;
    private PaymentDto? selectedPayment;
    private int currentPage = 1;
    private int totalPages = 1;
    private int totalOrdersCount;
    private bool suppressSelectionChanged;
    private CancellationTokenSource? pendingOperation;

    internal event EventHandler<OrderDto>? OpenCheckoutRequested;

    internal PaymentsForm(OrderApiService orders, PaymentApiService payments)
    {
        this.orders = orders;
        this.payments = payments;

        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = DesktopTheme.Cream;
        Font = DesktopTheme.Body;
        Margin = Padding.Empty;

        PopulateStatusFilter();

        var root = Stack();
        root.Dock = DockStyle.Top;
        root.Controls.Add(BuildFilterCard());
        root.Controls.Add(DesktopTheme.FeedbackBar(feedback, progress, "Consulta de órdenes y pagos en curso"));
        root.Controls.Add(BuildOrdersCard());
        root.Controls.Add(BuildDetailCard());
        Controls.Add(root);

        searchButton.Click += async (_, _) => await RefreshOrdersAsync(1, selectedOrder?.Id);
        clearFiltersButton.Click += async (_, _) => await ClearFiltersAsync();
        prevPageButton.Click += async (_, _) =>
        {
            if (currentPage > 1) await RefreshOrdersAsync(currentPage - 1, null);
        };
        nextPageButton.Click += async (_, _) =>
        {
            if (currentPage < totalPages) await RefreshOrdersAsync(currentPage + 1, null);
        };
        verifyOrderPaymentButton.Click += async (_, _) => await InspectSelectedOrderAndPaymentAsync();
        openCheckoutButton.Click += (_, _) =>
        {
            if (selectedOrder is { } order && PaymentApiService.CanProcessPayment(order, selectedPayment))
                OpenCheckoutRequested?.Invoke(this, order);
        };

        ordersGrid.SelectionChanged += async (_, _) =>
        {
            if (suppressSelectionChanged) return;
            selectedOrder = ordersGrid.CurrentRow?.Tag as OrderDto;
            selectedPayment = selectedOrder is not null && knownPayments.TryGetValue(selectedOrder.Id, out var cached) ? cached : null;
            UpdateDetailPanel();
            if (selectedOrder is { } current && !knownPayments.ContainsKey(current.Id))
                await InspectSelectedOrderAndPaymentAsync(silentSuccess: true);
        };

        UpdateDetailPanel();
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

    internal async Task RefreshOrdersAsync(int pageNumber = 1, long? preferredOrderId = null)
    {
        if (pendingOperation is not null) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            var status = (statusFilterCombo.SelectedItem as StatusFilterItem)?.Status;
            var orderIdVal = (long)orderIdFilter.Value;
            var filter = new OrderQueryFilter(
                PageNumber: pageNumber,
                PageSize: DefaultPageSize,
                Status: status,
                OrderId: orderIdVal > 0 ? orderIdVal : null);

            var page = await orders.GetPagedAsync(filter, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentOrders = page.Items.ToArray();
            currentPage = page.PageNumber;
            totalPages = Math.Max(1, page.TotalPages);
            totalOrdersCount = page.TotalCount;

            var targetId = preferredOrderId ?? selectedOrder?.Id ?? currentOrders.FirstOrDefault()?.Id;
            BindOrdersGrid(targetId);

            if (selectedOrder is { } active)
            {
                var payment = await payments.TryGetByOrderIdAsync(active.Id, cts.Token);
                if (!cts.IsCancellationRequested && !IsDisposed)
                {
                    knownPayments[active.Id] = payment;
                    selectedPayment = payment;
                    UpdateRowPaymentCell(active.Id, payment);
                    UpdateDetailPanel();
                }
            }
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

    internal void RecordKnownPayment(OrderDto? order, PaymentDto payment)
    {
        knownPayments[payment.OrderId] = payment;
        if (order is not null)
        {
            var list = currentOrders.ToList();
            var idx = list.FindIndex(o => o.Id == order.Id);
            if (idx >= 0) list[idx] = order;
            currentOrders = list;
        }
        if (selectedOrder?.Id == payment.OrderId)
        {
            if (order is not null) selectedOrder = order;
            selectedPayment = payment;
        }
        BindOrdersGrid(selectedOrder?.Id);
    }

    private async Task InspectSelectedOrderAndPaymentAsync(bool silentSuccess = false)
    {
        if (pendingOperation is not null || selectedOrder is not { } current) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        if (!silentSuccess) ShowFeedback("", isError: false);

        try
        {
            var refreshedOrder = await orders.GetByIdAsync(current.Id, cts.Token);
            var payment = await payments.TryGetByOrderIdAsync(current.Id, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            knownPayments[refreshedOrder.Id] = payment;
            selectedOrder = refreshedOrder;
            selectedPayment = payment;

            var list = currentOrders.ToList();
            var idx = list.FindIndex(o => o.Id == refreshedOrder.Id);
            if (idx >= 0) list[idx] = refreshedOrder;
            currentOrders = list;

            BindOrdersGrid(refreshedOrder.Id);
            if (!silentSuccess)
            {
                var paymentInfo = payment is null
                    ? "sin pago registrado"
                    : $"Pago #{payment.Id} ({PaymentApiService.FormatMethod(payment.Method)}, {PaymentApiService.FormatStatus(payment.Status)})";
                ShowFeedback(
                    $"Orden #{refreshedOrder.Id} verificada: {OrderApiService.FormatStatus(refreshedOrder.Status)}, Total {refreshedOrder.Total:C2}, {paymentInfo}.",
                    isError: false);
            }
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

    private async Task ClearFiltersAsync()
    {
        if (statusFilterCombo.Items.Count > 0) statusFilterCombo.SelectedIndex = 0;
        orderIdFilter.Value = 0;
        await RefreshOrdersAsync(1, null);
    }

    private Control BuildFilterCard()
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

        statusFilterCombo.AccessibleName = "Filtrar órdenes por estado";
        orderIdFilter.AccessibleName = "Buscar orden por identificador";

        bar.Controls.Add(LabeledField("Estado de orden", statusFilterCombo));
        bar.Controls.Add(LabeledField("ID de orden (0=todas)", orderIdFilter));
        searchButton.Margin = new Padding(4, 22, 8, 0);
        clearFiltersButton.Margin = new Padding(0, 22, 0, 0);
        bar.Controls.Add(searchButton);
        bar.Controls.Add(clearFiltersButton);
        return bar;
    }

    private Control BuildOrdersCard()
    {
        var card = Stack();
        card.Dock = DockStyle.Top;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(20);
        card.Margin = new Padding(0, 0, 0, 16);

        var heading = DesktopTheme.Label("1. Órdenes y estado de pago", DesktopTheme.Strong, DesktopTheme.Ink);
        heading.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(heading);

        DesktopTheme.ConfigureGrid(ordersGrid, "Listado de órdenes para caja", 205);

        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "OrderId", HeaderText = "ID Orden", FillWeight = 12 });
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Table", HeaderText = "Mesa", FillWeight = 14 });
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Date", HeaderText = "Fecha", FillWeight = 20 });
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Estado", FillWeight = 16 });
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Total", HeaderText = "Total Servidor", FillWeight = 15 });
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Account", HeaderText = "Cuenta", FillWeight = 14 });
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Payment", HeaderText = "Pago registrado", FillWeight = 24 });

        card.Controls.Add(ordersGrid);

        var pagerBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 10, 0, 0)
        };
        prevPageButton.Margin = new Padding(0, 0, 8, 0);
        nextPageButton.Margin = new Padding(0, 0, 14, 0);
        pageStatusLabel.Margin = new Padding(0, 10, 0, 0);
        pagerBar.Controls.Add(prevPageButton);
        pagerBar.Controls.Add(nextPageButton);
        pagerBar.Controls.Add(pageStatusLabel);
        card.Controls.Add(pagerBar);

        return card;
    }

    private Control BuildDetailCard()
    {
        var card = Stack();
        card.Dock = DockStyle.Top;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(20);
        card.Margin = Padding.Empty;

        card.Controls.Add(new Panel { Size = new Size(34, 3), BackColor = DesktopTheme.Olive, Margin = new Padding(0, 0, 0, 10) });
        selectedOrderTitle.Margin = new Padding(0, 0, 0, 4);
        selectedOrderMeta.Margin = new Padding(0, 0, 0, 8);
        paymentSummaryLabel.MaximumSize = new Size(880, 0);
        paymentSummaryLabel.Margin = new Padding(0, 0, 0, 8);
        operationalHintLabel.MaximumSize = new Size(880, 0);
        operationalHintLabel.Margin = new Padding(0, 0, 0, 14);

        card.Controls.Add(selectedOrderTitle);
        card.Controls.Add(selectedOrderMeta);
        card.Controls.Add(paymentSummaryLabel);
        card.Controls.Add(operationalHintLabel);

        var actionsBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = Padding.Empty
        };
        verifyOrderPaymentButton.Margin = new Padding(0, 0, 10, 6);
        openCheckoutButton.Margin = new Padding(0, 0, 0, 6);
        actionsBar.Controls.Add(verifyOrderPaymentButton);
        actionsBar.Controls.Add(openCheckoutButton);
        card.Controls.Add(actionsBar);

        return card;
    }

    private static Control LabeledField(string caption, Control input) =>
        DesktopTheme.LabeledField(caption, input);

    private void PopulateStatusFilter()
    {
        statusFilterCombo.Items.Clear();
        statusFilterCombo.Items.Add(new StatusFilterItem(null, "Todos los estados"));
        foreach (var status in Enum.GetValues<OrderStatus>())
            statusFilterCombo.Items.Add(new StatusFilterItem(status, OrderApiService.FormatStatus(status)));
        statusFilterCombo.SelectedIndex = 0;
    }

    private void BindOrdersGrid(long? preferredOrderId)
    {
        suppressSelectionChanged = true;
        try
        {
            ordersGrid.Rows.Clear();
            int selectedIndex = -1;
            for (var i = 0; i < currentOrders.Count; i++)
            {
                var order = currentOrders[i];
                var tableText = order.TableNumber.HasValue
                    ? $"Mesa #{order.TableNumber.Value:00}"
                    : order.TableId.HasValue ? $"Mesa #{order.TableId.Value}" : "Para llevar";
                knownPayments.TryGetValue(order.Id, out var knownPayment);
                var paymentText = FormatRowPaymentCell(order, knownPayment);

                var rowIndex = ordersGrid.Rows.Add(
                    $"#{order.Id}",
                    tableText,
                    order.OrderDateTime.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    OrderApiService.FormatStatus(order.Status),
                    order.Total.ToString("C2", CultureInfo.CurrentCulture),
                    order.AccountRequested ? "Solicitada" : "No solicitada",
                    paymentText);
                ordersGrid.Rows[rowIndex].Tag = order;
                if (preferredOrderId.HasValue && order.Id == preferredOrderId.Value)
                    selectedIndex = rowIndex;
            }

            if (selectedIndex < 0 && currentOrders.Count > 0)
                selectedIndex = 0;

            if (selectedIndex >= 0)
            {
                ordersGrid.ClearSelection();
                ordersGrid.Rows[selectedIndex].Selected = true;
                ordersGrid.CurrentCell = ordersGrid.Rows[selectedIndex].Cells[0];
                selectedOrder = currentOrders[selectedIndex];
                knownPayments.TryGetValue(selectedOrder.Id, out selectedPayment);
            }
            else
            {
                selectedOrder = null;
                selectedPayment = null;
            }
        }
        finally
        {
            suppressSelectionChanged = false;
        }

        pageStatusLabel.Text = $"Página {currentPage} de {totalPages} · {totalOrdersCount} {(totalOrdersCount == 1 ? "orden" : "órdenes")}";
        prevPageButton.Enabled = pendingOperation is null && currentPage > 1;
        nextPageButton.Enabled = pendingOperation is null && currentPage < totalPages;
        UpdateDetailPanel();
    }

    private void UpdateRowPaymentCell(long orderId, PaymentDto? payment)
    {
        foreach (DataGridViewRow row in ordersGrid.Rows)
        {
            if (row.Tag is OrderDto order && order.Id == orderId)
            {
                row.Cells["Payment"].Value = FormatRowPaymentCell(order, payment);
                break;
            }
        }
    }

    private static string FormatRowPaymentCell(OrderDto order, PaymentDto? payment)
    {
        if (payment is not null)
            return $"#{payment.Id} · {PaymentApiService.FormatMethod(payment.Method)} ({PaymentApiService.FormatStatus(payment.Status)})";
        if (order.Status == OrderStatus.Paid)
            return "Pagada (consultar detalle)";
        return "Sin pago";
    }

    private void UpdateDetailPanel()
    {
        var busy = pendingOperation is not null;
        if (selectedOrder is not { } order)
        {
            selectedOrderTitle.Text = "Selecciona una orden del listado";
            selectedOrderMeta.Text = "Sin orden seleccionada.";
            paymentSummaryLabel.Text = "Pago registrado: —";
            operationalHintLabel.Text = "Selecciona una orden para verificar su importe oficial del servidor y comprobar si ya tiene un pago registrado.";
            verifyOrderPaymentButton.Enabled = false;
            openCheckoutButton.Enabled = false;
            return;
        }

        var tableText = order.TableNumber.HasValue
            ? $"Mesa #{order.TableNumber.Value:00}"
            : order.TableId.HasValue ? $"Mesa #{order.TableId.Value}" : "Para llevar";
        selectedOrderTitle.Text = $"Orden #{order.Id} · {tableText}";
        selectedOrderMeta.Text =
            $"Estado: {OrderApiService.FormatStatus(order.Status)}   |   " +
            $"Cuenta: {(order.AccountRequested ? "Solicitada" : "No solicitada")}   |   " +
            $"Total servidor: {order.Total:C2}   |   " +
            $"Fecha: {order.OrderDateTime.ToLocalTime():g}";

        if (selectedPayment is { } payment)
        {
            var refText = string.IsNullOrWhiteSpace(payment.Reference) ? "Sin referencia" : payment.Reference;
            paymentSummaryLabel.Text =
                $"Pago registrado: Pago #{payment.Id} · Método: {PaymentApiService.FormatMethod(payment.Method)} · " +
                $"Estado: {PaymentApiService.FormatStatus(payment.Status)} · Monto: {payment.Amount:C2} · " +
                $"Referencia: {refText} · Fecha: {payment.PaymentDate.ToLocalTime():g}";
        }
        else if (knownPayments.ContainsKey(order.Id))
        {
            paymentSummaryLabel.Text = "Pago registrado: No existe pago registrado para esta orden.";
        }
        else
        {
            paymentSummaryLabel.Text = "Pago registrado: Pulsa «Consultar orden y pago en servidor» para verificar.";
        }

        var canPay = PaymentApiService.CanProcessPayment(order, selectedPayment);
        if (!canPay)
        {
            operationalHintLabel.Text = selectedPayment is not null || order.Status == OrderStatus.Paid
                ? "Esta orden ya fue pagada y no admite otro registro de pago."
                : $"El estado actual ({OrderApiService.FormatStatus(order.Status)}) no admite cobro en caja.";
        }
        else if (order.Status != OrderStatus.Served || !order.AccountRequested)
        {
            operationalHintLabel.Text =
                $"Aviso operativo: la orden está «{OrderApiService.FormatStatus(order.Status)}» y {(order.AccountRequested ? "con cuenta solicitada" : "aún sin cuenta solicitada")}. " +
                "Puedes pasarla a Cobros, pero confirma antes con el mesero que el servicio haya concluido.";
        }
        else
        {
            operationalHintLabel.Text = "Orden servida y con cuenta solicitada: lista para registrar su cobro en caja.";
        }

        verifyOrderPaymentButton.Enabled = !busy;
        openCheckoutButton.Enabled = !busy && canPay;
    }

    private void SetBusy(bool busy)
    {
        statusFilterCombo.Enabled = orderIdFilter.Enabled = searchButton.Enabled =
            clearFiltersButton.Enabled = ordersGrid.Enabled = !busy;
        prevPageButton.Enabled = !busy && currentPage > 1;
        nextPageButton.Enabled = !busy && currentPage < totalPages;
        progress.Visible = busy;
        UseWaitCursor = busy;
        UpdateDetailPanel();
    }

    private void ShowFeedback(string message, bool isError) =>
        DesktopTheme.ShowFeedback(feedback, message, isError);

    private static TableLayoutPanel Stack() => DesktopTheme.Stack();

    private sealed record StatusFilterItem(OrderStatus? Status, string Label)
    {
        public override string ToString() => Label;
    }
}
