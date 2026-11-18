using System.Globalization;
using System.Net;
using System.Windows.Forms.Automation;
using SIGER.Application.DTOs.Orders;
using SIGER.Desktop.Controls;
using SIGER.Desktop.Services;
using SIGER.Domain.Enums;

namespace SIGER.Desktop.Forms.Kitchen;

internal sealed class KitchenOrdersForm : UserControl
{
    private readonly KitchenApiService kitchen;

    // Toolbar & filters
    private readonly ComboBox statusFilterCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly Button refreshButton = DesktopTheme.Button("&Actualizar cola", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);
    private readonly Button verifyUncertainButton = DesktopTheme.Button("&Consultar estado actualizado", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Label summaryLabel = DesktopTheme.Label("Cargando órdenes de cocina…", DesktopTheme.Small, DesktopTheme.Muted);

    // Feedback & progress
    private readonly Label feedback = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Error);
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Height = 5, Dock = DockStyle.Fill };

    // Master orders grid
    private readonly DataGridView ordersGrid = new();

    // Selected order detail card
    private readonly Label selectedOrderTitle = DesktopTheme.Label("Selecciona una orden de la cola", DesktopTheme.Brand, DesktopTheme.Ink);
    private readonly Label selectedOrderMeta = DesktopTheme.Label("", DesktopTheme.Strong, DesktopTheme.Muted);
    private readonly Label selectedOrderNotes = DesktopTheme.Label("", DesktopTheme.Body, DesktopTheme.Ink);
    private readonly Label selectedOrderGuidance = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Muted);
    private readonly Button startPreparationButton = DesktopTheme.Button("&Iniciar preparación", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button markReadyButton = DesktopTheme.Button("&Marcar como lista", DesktopTheme.Olive, DesktopTheme.Ink, DesktopTheme.SoftOlive);
    private readonly DataGridView detailsGrid = new();

    private IReadOnlyList<OrderDto> allKitchenOrders = [];
    private IReadOnlyList<OrderDto> filteredOrders = [];
    private OrderDto? selectedOrder;
    private long? uncertainOrderId;
    private bool suppressSelectionChanged;
    private CancellationTokenSource? pendingOperation;

    internal KitchenOrdersForm(KitchenApiService kitchen)
    {
        this.kitchen = kitchen;
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = DesktopTheme.Cream;
        Font = DesktopTheme.Body;
        Margin = Padding.Empty;

        PopulateStatusFilter();

        var root = Stack();
        root.Dock = DockStyle.Top;
        root.Controls.Add(BuildToolbar());
        root.Controls.Add(DesktopTheme.FeedbackBar(feedback, progress, "Operación de cocina en curso"));
        root.Controls.Add(BuildOrdersCard());
        root.Controls.Add(BuildDetailCard());
        Controls.Add(root);

        statusFilterCombo.SelectedIndexChanged += (_, _) => ApplyFilterAndBind(selectedOrder?.Id);
        refreshButton.Click += async (_, _) => await RefreshOrdersAsync(selectedOrder?.Id);
        verifyUncertainButton.Click += async (_, _) => await VerifyUncertainOrderAsync();
        startPreparationButton.Click += async (_, _) => await TransitionSelectedOrderAsync(OrderStatus.InPreparation);
        markReadyButton.Click += async (_, _) => await TransitionSelectedOrderAsync(OrderStatus.Ready);

        ordersGrid.SelectionChanged += (_, _) =>
        {
            if (suppressSelectionChanged) return;
            selectedOrder = ordersGrid.CurrentRow?.Tag as OrderDto;
            UpdateSelectedOrderPanel();
        };

        UpdateSelectedOrderPanel();
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

    internal async Task RefreshOrdersAsync(long? preferredOrderId = null)
    {
        if (pendingOperation is not null) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            await LoadQueueCoreAsync(preferredOrderId ?? selectedOrder?.Id, cts.Token);
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

    private async Task LoadQueueCoreAsync(long? preferredOrderId, CancellationToken token)
    {
        var loaded = await kitchen.GetOrdersAsync(token);
        if (token.IsCancellationRequested || IsDisposed) return;

        allKitchenOrders = loaded.ToArray();
        var pendingCount = allKitchenOrders.Count(o => o.Status == OrderStatus.Pending);
        var inPrepCount = allKitchenOrders.Count(o => o.Status == OrderStatus.InPreparation);
        var readyCount = allKitchenOrders.Count(o => o.Status == OrderStatus.Ready);
        summaryLabel.Text =
            $"Cola activa: {allKitchenOrders.Count} · Pendientes: {pendingCount} · En preparación: {inPrepCount} · Listas: {readyCount}";

        ApplyFilterAndBind(preferredOrderId);
    }

    private async Task VerifyUncertainOrderAsync()
    {
        var targetId = uncertainOrderId ?? selectedOrder?.Id;
        uncertainOrderId = null;
        await RefreshOrdersAsync(targetId);
        if (IsDisposed) return;

        if (targetId.HasValue && allKitchenOrders.FirstOrDefault(o => o.Id == targetId.Value) is { } updated)
        {
            ShowFeedback(
                $"Estado verificado en servidor: la Orden #{updated.Id} está «{OrderApiService.FormatStatus(updated.Status)}».",
                isError: false);
        }
        else if (targetId.HasValue)
        {
            ShowFeedback(
                $"Se actualizó la cola de cocina. La Orden #{targetId.Value} ya no se encuentra en la cola activa.",
                isError: false);
        }
    }

    private async Task TransitionSelectedOrderAsync(OrderStatus targetStatus)
    {
        if (pendingOperation is not null || selectedOrder is not { } current) return;

        if (uncertainOrderId == current.Id)
        {
            ShowFeedback(
                $"La última solicitud para la Orden #{current.Id} quedó incierta por red. Pulsa «Consultar estado actualizado» antes de repetir la acción.",
                isError: true);
            verifyUncertainButton.Focus();
            return;
        }

        if (targetStatus == OrderStatus.InPreparation && !KitchenApiService.CanStartPreparation(current.Status))
        {
            ShowFeedback("Solo se puede iniciar preparación cuando la orden está Pendiente.", isError: true);
            return;
        }
        if (targetStatus == OrderStatus.Ready && !KitchenApiService.CanMarkReady(current.Status))
        {
            ShowFeedback("Solo se puede marcar como Lista cuando la orden está En preparación.", isError: true);
            return;
        }

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            var updated = targetStatus == OrderStatus.InPreparation
                ? await kitchen.MarkInPreparationAsync(current.Id, current.Status, cts.Token)
                : await kitchen.MarkReadyAsync(current.Id, current.Status, cts.Token);

            if (cts.IsCancellationRequested || IsDisposed) return;

            uncertainOrderId = null;
            ReplaceOrReloadOrder(updated);
            await LoadQueueCoreAsync(updated.Id, cts.Token);

            var actionLabel = targetStatus == OrderStatus.InPreparation
                ? "pasó a «En preparación»"
                : "quedó marcada como «Lista para servir»";
            ShowFeedback($"Orden #{updated.Id} {actionLabel}.", isError: false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (ApiException error)
        {
            if (cts.IsCancellationRequested || IsDisposed) return;

            if (error.StatusCode == HttpStatusCode.Conflict)
            {
                try
                {
                    await LoadQueueCoreAsync(current.Id, cts.Token);
                    if (!cts.IsCancellationRequested && !IsDisposed)
                    {
                        ShowFeedback(
                            $"La Orden #{current.Id} cambió de estado en el servidor. Se recargó la cola para que revises su estado actual sin repetir la escritura.",
                            isError: true);
                        return;
                    }
                }
                catch (ApiException)
                {
                    // Keep original conflict message if refresh also fails.
                }
            }
            else if (error.StatusCode is null)
            {
                uncertainOrderId = current.Id;
                ShowFeedback(
                    $"{error.Message} No se confirmó si el cambio de estado de la Orden #{current.Id} se aplicó. Usa «Consultar estado actualizado» antes de volver a intentarlo.",
                    isError: true);
                return;
            }

            ShowFeedback(error.Message, isError: true);
        }
        finally
        {
            pendingOperation = null;
            if (!IsDisposed) SetBusy(false);
        }
    }

    private void ReplaceOrReloadOrder(OrderDto updated)
    {
        if (!KitchenApiService.IsKitchenQueueStatus(updated.Status))
        {
            allKitchenOrders = allKitchenOrders.Where(o => o.Id != updated.Id).ToArray();
        }
        else
        {
            var list = allKitchenOrders.ToList();
            var index = list.FindIndex(o => o.Id == updated.Id);
            if (index >= 0) list[index] = updated;
            else list.Add(updated);
            allKitchenOrders = list.OrderBy(o => o.OrderDateTime).ThenBy(o => o.Id).ToArray();
        }

        ApplyFilterAndBind(updated.Id);
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

        var filterLabel = DesktopTheme.Label("Estado:", DesktopTheme.Small, DesktopTheme.Muted);
        filterLabel.Margin = new Padding(0, 11, 8, 0);
        statusFilterCombo.AccessibleName = "Filtrar órdenes de cocina por estado";
        statusFilterCombo.Margin = new Padding(0, 6, 16, 6);
        refreshButton.Margin = new Padding(0, 0, 10, 0);
        verifyUncertainButton.Margin = new Padding(0, 0, 16, 0);
        verifyUncertainButton.Visible = false;
        summaryLabel.Margin = new Padding(0, 11, 0, 0);

        bar.Controls.Add(filterLabel);
        bar.Controls.Add(statusFilterCombo);
        bar.Controls.Add(refreshButton);
        bar.Controls.Add(verifyUncertainButton);
        bar.Controls.Add(summaryLabel);
        return bar;
    }

    private Control BuildOrdersCard()
    {
        var card = Stack();
        card.Dock = DockStyle.Top;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(20);
        card.Margin = new Padding(0, 0, 0, 16);

        var heading = DesktopTheme.Label("1. Cola operativa de órdenes en cocina", DesktopTheme.Strong, DesktopTheme.Ink);
        heading.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(heading);

        ConfigureGrid(ordersGrid, "Cola de órdenes de cocina", 210);
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "OrderId", HeaderText = "Orden", FillWeight = 12 });
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Table", HeaderText = "Mesa", FillWeight = 15 });
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DateTime", HeaderText = "Fecha y hora", FillWeight = 22 });
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Estado actual", FillWeight = 18 });
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ItemsCount", HeaderText = "Productos", FillWeight = 12 });
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Notes", HeaderText = "Observaciones generales", FillWeight = 31 });

        card.Controls.Add(ordersGrid);
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
        selectedOrderMeta.Margin = new Padding(0, 0, 0, 6);
        selectedOrderNotes.MaximumSize = new Size(880, 0);
        selectedOrderNotes.Margin = new Padding(0, 0, 0, 6);
        selectedOrderGuidance.MaximumSize = new Size(880, 0);
        selectedOrderGuidance.Margin = new Padding(0, 0, 0, 12);

        card.Controls.Add(selectedOrderTitle);
        card.Controls.Add(selectedOrderMeta);
        card.Controls.Add(selectedOrderNotes);
        card.Controls.Add(selectedOrderGuidance);

        var actionsBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 12)
        };
        startPreparationButton.Margin = new Padding(0, 0, 10, 6);
        markReadyButton.Margin = new Padding(0, 0, 0, 6);
        actionsBar.Controls.Add(startPreparationButton);
        actionsBar.Controls.Add(markReadyButton);
        card.Controls.Add(actionsBar);

        var detailsHeading = DesktopTheme.Label("Productos solicitados y notas de preparación", DesktopTheme.Strong, DesktopTheme.Ink);
        detailsHeading.Margin = new Padding(0, 0, 0, 8);
        card.Controls.Add(detailsHeading);

        ConfigureGrid(detailsGrid, "Productos solicitados de la orden seleccionada", 180);
        detailsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Product", HeaderText = "Producto solicitado", FillWeight = 38 });
        detailsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "Cantidad", FillWeight = 14 });
        detailsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Note", HeaderText = "Nota de preparación del producto", FillWeight = 48 });
        card.Controls.Add(detailsGrid);

        return card;
    }

    private static void ConfigureGrid(DataGridView grid, string accessibleName, int height) =>
        DesktopTheme.ConfigureGrid(grid, accessibleName, height);

    private void PopulateStatusFilter()
    {
        statusFilterCombo.Items.Clear();
        statusFilterCombo.Items.Add(new StatusFilterItem(null, "Todas las órdenes de cocina"));
        statusFilterCombo.Items.Add(new StatusFilterItem(OrderStatus.Pending, "Pendientes"));
        statusFilterCombo.Items.Add(new StatusFilterItem(OrderStatus.InPreparation, "En preparación"));
        statusFilterCombo.Items.Add(new StatusFilterItem(OrderStatus.Ready, "Listas para servir"));
        statusFilterCombo.SelectedIndex = 0;
    }

    private void ApplyFilterAndBind(long? preferredOrderId)
    {
        var filterStatus = (statusFilterCombo.SelectedItem as StatusFilterItem)?.Status;
        filteredOrders = filterStatus.HasValue
            ? allKitchenOrders.Where(o => o.Status == filterStatus.Value).ToArray()
            : allKitchenOrders;

        suppressSelectionChanged = true;
        try
        {
            ordersGrid.Rows.Clear();
            int selectedIndex = -1;
            for (var i = 0; i < filteredOrders.Count; i++)
            {
                var order = filteredOrders[i];
                var tableText = order.TableNumber.HasValue
                    ? $"Mesa #{order.TableNumber.Value:00}"
                    : order.TableId.HasValue ? $"Mesa #{order.TableId.Value}" : "Para llevar";
                var totalUnits = order.Details.Sum(d => d.Quantity);
                var rowIndex = ordersGrid.Rows.Add(
                    $"#{order.Id}",
                    tableText,
                    order.OrderDateTime.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    OrderApiService.FormatStatus(order.Status),
                    $"{order.Details.Count} lín. ({totalUnits} u.)",
                    string.IsNullOrWhiteSpace(order.Notes) ? "—" : order.Notes);
                ordersGrid.Rows[rowIndex].Tag = order;
                if (preferredOrderId.HasValue && order.Id == preferredOrderId.Value)
                    selectedIndex = rowIndex;
            }

            if (selectedIndex < 0 && filteredOrders.Count > 0)
                selectedIndex = 0;

            if (selectedIndex >= 0)
            {
                ordersGrid.ClearSelection();
                ordersGrid.Rows[selectedIndex].Selected = true;
                ordersGrid.CurrentCell = ordersGrid.Rows[selectedIndex].Cells[0];
                selectedOrder = filteredOrders[selectedIndex];
            }
            else
            {
                selectedOrder = null;
            }
        }
        finally
        {
            suppressSelectionChanged = false;
        }

        UpdateSelectedOrderPanel();
    }

    private void UpdateSelectedOrderPanel()
    {
        var busy = pendingOperation is not null;
        verifyUncertainButton.Visible = uncertainOrderId.HasValue;
        verifyUncertainButton.Enabled = !busy && uncertainOrderId.HasValue;

        detailsGrid.Rows.Clear();
        if (selectedOrder is not { } order)
        {
            selectedOrderTitle.Text = "Selecciona una orden de la cola";
            selectedOrderMeta.Text = "Sin orden seleccionada.";
            selectedOrderNotes.Text = "Observaciones generales: —";
            selectedOrderGuidance.Text = "Las órdenes operativas avanzan de Pendiente a En preparación y luego a Lista para servir. Las preórdenes Web no entran a esta cola.";
            startPreparationButton.Enabled = false;
            markReadyButton.Enabled = false;
            return;
        }

        var tableText = order.TableNumber.HasValue
            ? $"Mesa #{order.TableNumber.Value:00}"
            : order.TableId.HasValue ? $"Mesa #{order.TableId.Value}" : "Para llevar";
        selectedOrderTitle.Text = $"Orden #{order.Id} · {tableText}";
        selectedOrderMeta.Text =
            $"Estado actual: {OrderApiService.FormatStatus(order.Status)}   |   " +
            $"Recibida: {order.OrderDateTime.ToLocalTime():g}   |   " +
            $"Versión: {order.Version}";
        selectedOrderNotes.Text = string.IsNullOrWhiteSpace(order.Notes)
            ? "Observaciones generales: Sin observaciones."
            : $"Observaciones generales: {order.Notes}";

        selectedOrderGuidance.Text = order.Status switch
        {
            OrderStatus.Pending =>
                "Orden pendiente de iniciar: pulsa «Iniciar preparación» cuando cocina comience a trabajarla.",
            OrderStatus.InPreparation =>
                "Orden en preparación: pulsa «Marcar como lista» cuando todos los productos estén terminados para que el mesero pueda servirla.",
            OrderStatus.Ready =>
                "Orden lista para servir: el mesero es responsable de entregarla en mesa y marcarla como Servida.",
            _ => ""
        };

        foreach (var detail in order.Details)
        {
            detailsGrid.Rows.Add(
                detail.ProductName,
                detail.Quantity.ToString(CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(detail.Note) ? "Sin nota" : detail.Note);
        }

        var isUncertain = uncertainOrderId == order.Id;
        startPreparationButton.Enabled = !busy && !isUncertain && KitchenApiService.CanStartPreparation(order.Status);
        markReadyButton.Enabled = !busy && !isUncertain && KitchenApiService.CanMarkReady(order.Status);
    }

    private void SetBusy(bool busy)
    {
        statusFilterCombo.Enabled = refreshButton.Enabled = ordersGrid.Enabled = detailsGrid.Enabled = !busy;
        progress.Visible = busy;
        UseWaitCursor = busy;
        UpdateSelectedOrderPanel();
    }

    private void ShowFeedback(string message, bool isError) =>
        DesktopTheme.ShowFeedback(feedback, message, isError);

    private static TableLayoutPanel Stack() => DesktopTheme.Stack();

    private sealed record StatusFilterItem(OrderStatus? Status, string Label)
    {
        public override string ToString() => Label;
    }
}
