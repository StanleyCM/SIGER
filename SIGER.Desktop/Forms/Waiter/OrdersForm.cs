using System.Globalization;
using System.Net;
using System.Windows.Forms.Automation;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.DTOs.Products;
using SIGER.Application.DTOs.Tables;
using SIGER.Desktop.Controls;
using SIGER.Desktop.Services;
using SIGER.Domain.Enums;

namespace SIGER.Desktop.Forms.Waiter;

internal sealed class OrdersForm : UserControl
{
    private const int DefaultPageSize = 20;

    private readonly TableApiService tables;
    private readonly ProductApiService products;
    private readonly OrderApiService orders;

    // Feedback & progress
    private readonly Label feedback = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Error);
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Height = 5, Dock = DockStyle.Fill };

    // Section 1: New order builder
    private readonly CheckBox takeAwayToggle = new() { Text = "Para llevar", AutoSize = true };
    private readonly ComboBox newOrderTableCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly ComboBox newOrderProductCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 310 };
    private readonly NumericUpDown newOrderQuantity = new() { Minimum = 1, Maximum = 100, Value = 1, Width = 75 };
    private readonly TextBox newOrderItemNote = new() { MaxLength = 300, PlaceholderText = "Nota de línea (opcional)", Width = 220 };
    private readonly Button addDraftButton = DesktopTheme.Button("Agregar al &borrador", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button removeDraftButton = DesktopTheme.Button("Quitar línea", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);
    private readonly Button clearDraftButton = DesktopTheme.Button("Vaciar borrador", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);
    private readonly Button reloadCatalogButton = DesktopTheme.Button("Recargar mesas/catálogo", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly DataGridView draftGrid = new();
    private readonly TextBox newOrderNotes = new() { MaxLength = 300, PlaceholderText = "Notas generales de la orden (opcional)", Width = 340 };
    private readonly Label draftTotalLabel = DesktopTheme.Label("Total estimado borrador: $0.00", DesktopTheme.Strong, DesktopTheme.Ink);
    private readonly Button createOrderButton = DesktopTheme.Button("&Crear orden en servidor", DesktopTheme.Olive, DesktopTheme.Ink, DesktopTheme.SoftOlive);
    private readonly Button verifyUncertainButton = DesktopTheme.Button("Consultar mesa y órdenes recientes", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);

    // Section 2: Orders filter & list
    private readonly ComboBox tableFilterCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly ComboBox statusFilterCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175 };
    private readonly NumericUpDown orderIdFilter = new() { Minimum = 0, Maximum = 9999999, Value = 0, Width = 95 };
    private readonly CheckBox dateRangeToggle = new() { Text = "Por fecha", AutoSize = true };
    private readonly DateTimePicker startDatePicker = new() { Format = DateTimePickerFormat.Short, Width = 115, Enabled = false };
    private readonly DateTimePicker endDatePicker = new() { Format = DateTimePickerFormat.Short, Width = 115, Enabled = false };
    private readonly Button searchOrdersButton = DesktopTheme.Button("&Buscar / Actualizar", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);
    private readonly Button clearFiltersButton = DesktopTheme.Button("Limpiar filtros", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button prevPageButton = DesktopTheme.Button("◀ Anterior", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button nextPageButton = DesktopTheme.Button("Siguiente ▶", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Label pageStatusLabel = DesktopTheme.Label("Página 1 de 1", DesktopTheme.Small, DesktopTheme.Muted);
    private readonly DataGridView ordersGrid = new();

    // Section 3: Selected order detail, item mutations & status actions
    private readonly Label selectedOrderTitle = DesktopTheme.Label("Selecciona una orden o crea una nueva", DesktopTheme.Brand, DesktopTheme.Ink);
    private readonly Label selectedOrderMeta = DesktopTheme.Label("", DesktopTheme.Strong, DesktopTheme.Muted);
    private readonly Label selectedOrderNotes = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Ink);
    private readonly Label selectedOrderGuidance = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Muted);
    private readonly Button reloadOrderButton = DesktopTheme.Button("&Recargar orden", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button markServedButton = DesktopTheme.Button("Marcar como &servida", DesktopTheme.Olive, DesktopTheme.Ink, DesktopTheme.SoftOlive);
    private readonly Button requestAccountButton = DesktopTheme.Button("Solicitar &cuenta", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button cancelOrderButton = DesktopTheme.Button("Cancelar orden", DesktopTheme.Error, DesktopTheme.Paper, DesktopTheme.SidebarHover);
    private readonly DataGridView detailsGrid = new();

    // Add item to existing order
    private readonly ComboBox addLineProductCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 290 };
    private readonly NumericUpDown addLineQuantity = new() { Minimum = 1, Maximum = 100, Value = 1, Width = 75 };
    private readonly TextBox addLineNote = new() { MaxLength = 300, PlaceholderText = "Nota de la nueva línea", Width = 210 };
    private readonly Button addLineButton = DesktopTheme.Button("Agregar producto a orden", DesktopTheme.Olive, DesktopTheme.Ink, DesktopTheme.SoftOlive);

    // Edit/delete existing line
    private readonly Label selectedLineLabel = DesktopTheme.Label("Línea seleccionada: ninguna", DesktopTheme.Strong, DesktopTheme.Ink);
    private readonly NumericUpDown editLineQuantity = new() { Minimum = 1, Maximum = 100, Value = 1, Width = 75 };
    private readonly TextBox editLineNote = new() { MaxLength = 300, PlaceholderText = "Nota de la línea seleccionada", Width = 210 };
    private readonly Button updateLineButton = DesktopTheme.Button("Actualizar línea", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button removeLineButton = DesktopTheme.Button("Eliminar línea", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);

    private readonly List<DraftLine> draftItems = [];
    private IReadOnlyList<TableDto> allTables = [];
    private IReadOnlyList<TableDto> availableTables = [];
    private IReadOnlyList<ProductDto> availableProducts = [];
    private IReadOnlyList<OrderDto> currentOrders = [];
    private OrderDto? selectedOrder;
    private OrderDetailDto? selectedDetail;
    private long? draftReservationId;
    private long? draftReservationTableId;
    private long? uncertainSubmissionTableId;
    private bool uncertainTakeAwaySubmission;
    private int currentPage = 1;
    private int totalPages = 1;
    private int totalOrdersCount;
    private bool initialized;
    private bool suppressOrdersSelection;
    private bool suppressDetailsSelection;
    private CancellationTokenSource? pendingOperation;

    internal OrdersForm(TableApiService tables, ProductApiService products, OrderApiService orders)
    {
        this.tables = tables;
        this.products = products;
        this.orders = orders;

        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = DesktopTheme.Cream;
        Font = DesktopTheme.Body;
        Margin = Padding.Empty;

        PopulateStatusFilter();
        var root = DesktopTheme.Stack();
        root.Dock = DockStyle.Top;
        root.Controls.Add(DesktopTheme.FeedbackBar(feedback, progress, "Operación de órdenes en curso"));
        root.Controls.Add(BuildNewOrderCard());
        root.Controls.Add(BuildOrdersListCard());
        root.Controls.Add(BuildOrderDetailCard());
        Controls.Add(root);

        WireEvents();
        RefreshDraftView();
        UpdateSelectedOrderView();
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

    internal async Task EnsureLoadedAsync()
    {
        if (!initialized)
            await LoadWorkspaceAsync(preferredAvailableTableId: null, filterTableId: null, selectOrderId: null);
        else
            await LoadOrdersPageAsync(currentPage, selectedOrder?.Id);
    }

    internal async Task PrepareNewOrderForTableAsync(TableDto table)
    {
        draftReservationId = null;
        draftReservationTableId = null;
        takeAwayToggle.Checked = false;
        await LoadWorkspaceAsync(preferredAvailableTableId: table.Id, filterTableId: table.Id, selectOrderId: null);
        RefreshDraftView();
        ShowFeedback($"Mesa #{table.Number:00} seleccionada para nueva orden. Agrega productos al borrador y envíala al servidor.", isError: false);
        newOrderProductCombo.Focus();
    }

    internal async Task PrepareNewOrderForReservationAsync(long reservationId, long tableId, int? tableNumber)
    {
        draftReservationId = reservationId;
        draftReservationTableId = tableId;
        takeAwayToggle.Checked = false;
        await LoadWorkspaceAsync(preferredAvailableTableId: tableId, filterTableId: tableId, selectOrderId: null);
        RefreshDraftView();
        var tableText = tableNumber.HasValue ? $"Mesa #{tableNumber.Value:00}" : $"Mesa #{tableId}";
        if (IsDisposed) return;
        if (newOrderTableCombo.SelectedItem is TableComboItem { Table.Id: var selectedId } && selectedId == tableId)
            ShowFeedback($"Reservación #{reservationId} recibida en {tableText}. Agrega productos al borrador para abrir su orden operativa.", isError: false);
        newOrderProductCombo.Focus();
    }

    internal async Task OpenOrderAsync(long orderId, long? tableId = null)
    {
        if (orderId <= 0) return;
        orderIdFilter.Value = Math.Clamp(orderId, (long)orderIdFilter.Minimum, (long)orderIdFilter.Maximum);
        await LoadWorkspaceAsync(preferredAvailableTableId: null, filterTableId: tableId, selectOrderId: orderId);
        if (!IsDisposed && selectedOrder?.Id == orderId)
            ShowFeedback($"Mostrando Orden #{orderId}.", isError: false);
    }

    internal async Task FilterByTableAsync(TableDto table)
    {
        await LoadWorkspaceAsync(preferredAvailableTableId: null, filterTableId: table.Id, selectOrderId: null);
        ShowFeedback($"Mostrando las órdenes registradas de la Mesa #{table.Number:00}.", isError: false);
    }

    private void WireEvents()
    {
        reloadCatalogButton.Click += async (_, _) =>
            await LoadCatalogAndTablesAsync((newOrderTableCombo.SelectedItem as TableComboItem)?.Table.Id, (tableFilterCombo.SelectedItem as TableFilterItem)?.TableId);
        addDraftButton.Click += (_, _) => AddDraftItem();
        removeDraftButton.Click += (_, _) => RemoveSelectedDraftItem();
        clearDraftButton.Click += (_, _) => ClearDraft();
        takeAwayToggle.CheckedChanged += (_, _) => UpdateDraftActionStates();
        newOrderTableCombo.SelectedIndexChanged += (_, _) => UpdateDraftActionStates();
        createOrderButton.Click += async (_, _) => await CreateOrderOnServerAsync();
        verifyUncertainButton.Click += async (_, _) => await VerifyUncertainOrderAsync();

        dateRangeToggle.CheckedChanged += (_, _) =>
        {
            startDatePicker.Enabled = endDatePicker.Enabled = dateRangeToggle.Checked && pendingOperation is null;
        };
        searchOrdersButton.Click += async (_, _) => await LoadOrdersPageAsync(1, selectedOrder?.Id);
        clearFiltersButton.Click += async (_, _) => await ClearFiltersAsync();
        prevPageButton.Click += async (_, _) =>
        {
            if (currentPage > 1) await LoadOrdersPageAsync(currentPage - 1, null);
        };
        nextPageButton.Click += async (_, _) =>
        {
            if (currentPage < totalPages) await LoadOrdersPageAsync(currentPage + 1, null);
        };

        ordersGrid.SelectionChanged += (_, _) =>
        {
            if (suppressOrdersSelection) return;
            selectedOrder = ordersGrid.CurrentRow?.Tag as OrderDto;
            selectedDetail = null;
            UpdateSelectedOrderView();
        };

        detailsGrid.SelectionChanged += (_, _) =>
        {
            if (suppressDetailsSelection) return;
            selectedDetail = detailsGrid.CurrentRow?.Tag as OrderDetailDto;
            SyncSelectedDetailEditor();
        };

        reloadOrderButton.Click += async (_, _) => await ReloadSelectedOrderAsync();
        markServedButton.Click += async (_, _) => await ChangeSelectedOrderStatusAsync(OrderStatus.Served);
        requestAccountButton.Click += async (_, _) => await RequestSelectedOrderAccountAsync();
        cancelOrderButton.Click += async (_, _) => await ConfirmAndCancelSelectedOrderAsync();
        addLineButton.Click += async (_, _) => await AddLineToSelectedOrderAsync();
        updateLineButton.Click += async (_, _) => await UpdateSelectedLineAsync();
        removeLineButton.Click += async (_, _) => await RemoveSelectedLineAsync();
    }

    private async Task LoadWorkspaceAsync(long? preferredAvailableTableId, long? filterTableId, long? selectOrderId)
    {
        if (pendingOperation is not null) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            await LoadCatalogAndTablesCoreAsync(preferredAvailableTableId, filterTableId, cts.Token);
            await LoadOrdersPageCoreAsync(1, selectOrderId, cts.Token);
            initialized = true;
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

    private async Task LoadCatalogAndTablesAsync(long? preferredAvailableTableId, long? filterTableId)
    {
        if (pendingOperation is not null) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            await LoadCatalogAndTablesCoreAsync(preferredAvailableTableId, filterTableId, cts.Token);
            ShowFeedback("Mesas y catálogo de productos actualizados.", isError: false);
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

    private async Task LoadCatalogAndTablesCoreAsync(long? preferredAvailableTableId, long? filterTableId, CancellationToken token)
    {
        var allTablesTask = tables.GetPagedAsync(1, 200, token);
        var availableTablesTask = LoadDraftTablesAsync(token);
        var availableProductsTask = products.GetAvailableAsync(null, token);
        await Task.WhenAll(allTablesTask, availableTablesTask, availableProductsTask);

        if (token.IsCancellationRequested || IsDisposed) return;

        allTables = allTablesTask.Result.Items.OrderBy(t => t.Number).ToArray();
        availableTables = availableTablesTask.Result.OrderBy(t => t.Number).ToArray();
        availableProducts = availableProductsTask.Result.OrderBy(p => p.CategoryName).ThenBy(p => p.Name).ToArray();

        BindAvailableTables(preferredAvailableTableId);
        BindTableFilter(filterTableId);
        BindProductCombos();
    }

    private async Task<IReadOnlyCollection<TableDto>> LoadDraftTablesAsync(CancellationToken token)
    {
        if (draftReservationTableId is not { } receivedTableId)
            return await tables.GetAvailableAsync(token);

        var table = await tables.GetByIdAsync(receivedTableId, token);
        return table.Status is TableStatus.Available or TableStatus.Reserved ? [table] : [];
    }

    private async Task LoadOrdersPageAsync(int pageNumber, long? preferredOrderId)
    {
        if (pendingOperation is not null) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            await LoadOrdersPageCoreAsync(pageNumber, preferredOrderId, cts.Token);
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

    private async Task LoadOrdersPageCoreAsync(int pageNumber, long? preferredOrderId, CancellationToken token)
    {
        var filter = BuildCurrentFilter(pageNumber);
        var page = await orders.GetPagedAsync(filter, token);
        if (token.IsCancellationRequested || IsDisposed) return;

        currentOrders = page.Items.ToArray();
        currentPage = page.PageNumber;
        totalPages = Math.Max(1, page.TotalPages);
        totalOrdersCount = page.TotalCount;

        var targetOrderId = preferredOrderId
            ?? currentOrders.FirstOrDefault(o => OrderApiService.IsActiveOrder(o.Status))?.Id
            ?? currentOrders.FirstOrDefault()?.Id;

        BindOrdersGrid(targetOrderId);
    }

    private OrderQueryFilter BuildCurrentFilter(int pageNumber)
    {
        var tableId = (tableFilterCombo.SelectedItem as TableFilterItem)?.TableId;
        var status = (statusFilterCombo.SelectedItem as StatusFilterItem)?.Status;
        var orderIdValue = (long)orderIdFilter.Value;
        long? orderId = orderIdValue > 0 ? orderIdValue : null;

        DateTimeOffset? startDate = null;
        DateTimeOffset? endDate = null;
        if (dateRangeToggle.Checked)
        {
            var localStart = startDatePicker.Value.Date;
            var localEnd = endDatePicker.Value.Date.AddDays(1).AddTicks(-1);
            startDate = new DateTimeOffset(localStart, TimeZoneInfo.Local.GetUtcOffset(localStart));
            endDate = new DateTimeOffset(localEnd, TimeZoneInfo.Local.GetUtcOffset(localEnd));
        }

        return new OrderQueryFilter(
            PageNumber: pageNumber,
            PageSize: DefaultPageSize,
            TableId: tableId,
            Status: status,
            OrderId: orderId,
            StartDate: startDate,
            EndDate: endDate);
    }

    private async Task ClearFiltersAsync()
    {
        ResetFilters();
        await LoadOrdersPageAsync(1, null);
    }

    private void ResetFilters()
    {
        if (tableFilterCombo.Items.Count > 0) tableFilterCombo.SelectedIndex = 0;
        if (statusFilterCombo.Items.Count > 0) statusFilterCombo.SelectedIndex = 0;
        orderIdFilter.Value = 0;
        dateRangeToggle.Checked = false;
    }

    private void AddDraftItem()
    {
        if (newOrderProductCombo.SelectedItem is not ProductComboItem { Product: var product })
        {
            ShowFeedback("Selecciona un producto disponible para agregar al borrador.", isError: true);
            return;
        }

        var quantity = (int)newOrderQuantity.Value;
        var note = string.IsNullOrWhiteSpace(newOrderItemNote.Text) ? null : newOrderItemNote.Text.Trim();
        var existing = draftItems.FirstOrDefault(item => item.ProductId == product.Id && string.Equals(item.Note, note, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.Quantity = Math.Min(100, existing.Quantity + quantity);
        }
        else
        {
            draftItems.Add(new DraftLine
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.Price,
                Quantity = quantity,
                Note = note
            });
        }

        newOrderQuantity.Value = 1;
        newOrderItemNote.Clear();
        uncertainSubmissionTableId = null;
        RefreshDraftView();
        ShowFeedback($"Agregado al borrador: {product.Name} (x{quantity}).", isError: false);
    }

    private void RemoveSelectedDraftItem()
    {
        if (draftGrid.CurrentRow?.Tag is not DraftLine line) return;
        draftItems.Remove(line);
        uncertainSubmissionTableId = null;
        RefreshDraftView();
    }

    private void ClearDraft()
    {
        draftItems.Clear();
        newOrderNotes.Clear();
        draftReservationId = null;
        draftReservationTableId = null;
        uncertainSubmissionTableId = null;
        uncertainTakeAwaySubmission = false;
        RefreshDraftView();
    }

    private async Task CreateOrderOnServerAsync()
    {
        if (pendingOperation is not null || uncertainTakeAwaySubmission) return;
        var takeAway = takeAwayToggle.Checked;
        var table = takeAway ? null : (newOrderTableCombo.SelectedItem as TableComboItem)?.Table;
        if (!takeAway && table is null)
        {
            ShowFeedback("Selecciona una mesa disponible antes de crear la orden.", isError: true);
            newOrderTableCombo.Focus();
            return;
        }
        if (draftItems.Count == 0)
        {
            ShowFeedback("Agrega al menos un producto al borrador antes de enviar la orden.", isError: true);
            newOrderProductCombo.Focus();
            return;
        }
        if (table is not null && uncertainSubmissionTableId == table.Id)
        {
            ShowFeedback("El envío anterior para esta mesa quedó incierto. Usa «Consultar mesa y órdenes recientes» antes de volver a enviar.", isError: true);
            verifyUncertainButton.Focus();
            return;
        }

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);
        OrderDto? confirmedOrder = null;

        try
        {
            var items = draftItems.Select(line => new CreateOrderDetailRequestDto
            {
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                Note = line.Note
            }).ToArray();

            var created = table is null
                ? await orders.CreateTakeAwayOrderAsync(items, newOrderNotes.Text, cts.Token)
                : await orders.CreateTableOrderAsync(table.Id, items, newOrderNotes.Text, draftReservationId, cts.Token);
            confirmedOrder = created;
            if (cts.IsCancellationRequested || IsDisposed) return;

            draftItems.Clear();
            newOrderNotes.Clear();
            draftReservationId = null;
            draftReservationTableId = null;
            uncertainSubmissionTableId = null;
            RefreshDraftView();

            if (table is null)
                ResetFilters();
            else
                await LoadCatalogAndTablesCoreAsync(null, table.Id, cts.Token);
            await LoadOrdersPageCoreAsync(1, created.Id, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;
            ReplaceSelectedOrder(created);

            var destination = table is null ? "para llevar" : $"en Mesa #{created.TableNumber ?? table.Number:00}";
            ShowFeedback(
                $"Orden #{created.Id} creada {destination}. Total del servidor: {created.Total:C2}.",
                isError: false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (ApiException error)
        {
            if (cts.IsCancellationRequested || IsDisposed) return;
            if (confirmedOrder is not null)
            {
                ShowFeedback($"Orden #{confirmedOrder.Id} creada. No se pudo actualizar la vista: {error.Message}", isError: true);
                return;
            }
            if (error.StatusCode is null || (int)error.StatusCode.Value >= 500)
            {
                // Transport or timeout error after sending POST /orders: keep draft and require consulting before retry.
                uncertainSubmissionTableId = table?.Id;
                uncertainTakeAwaySubmission = takeAway;
                ShowFeedback(
                    $"{error.Message} No se confirmó si la orden se guardó. Conservamos el borrador. Pulsa «{UncertainOrderAction}» para comprobarlo.",
                    isError: true);
            }
            else
            {
                if (error.StatusCode == HttpStatusCode.Conflict)
                {
                    try { await LoadCatalogAndTablesCoreAsync(null, (tableFilterCombo.SelectedItem as TableFilterItem)?.TableId, cts.Token); }
                    catch (ApiException) { /* Best-effort refresh on conflict */ }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested) { return; }
                }
                ShowFeedback(error.Message, isError: true);
            }
        }
        finally
        {
            pendingOperation = null;
            if (!IsDisposed) SetBusy(false);
        }
    }

    private async Task VerifyUncertainOrderAsync()
    {
        if (pendingOperation is not null) return;
        var tableId = uncertainSubmissionTableId ?? (newOrderTableCombo.SelectedItem as TableComboItem)?.Table.Id;
        if (!uncertainTakeAwaySubmission && !tableId.HasValue) return;

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            if (uncertainTakeAwaySubmission)
            {
                ResetFilters();
                await LoadOrdersPageCoreAsync(1, null, cts.Token);
                if (cts.IsCancellationRequested || IsDisposed) return;
                ShowFeedback("Revisa las órdenes recientes para comprobar si se registró el pedido para llevar. Vacía el borrador antes de crear otro pedido.", isError: false);
                return;
            }

            var tableState = await tables.GetByIdAsync(tableId!.Value, cts.Token);
            await LoadCatalogAndTablesCoreAsync(tableState.Status == TableStatus.Available ? tableState.Id : null, tableState.Id, cts.Token);
            await LoadOrdersPageCoreAsync(1, null, cts.Token);
            uncertainSubmissionTableId = null;

            var activeOrder = currentOrders.FirstOrDefault(o => o.TableId == tableState.Id && OrderApiService.IsActiveOrder(o.Status));
            if (activeOrder is not null)
            {
                var detailed = await orders.GetByIdAsync(activeOrder.Id, cts.Token);
                ReplaceSelectedOrder(detailed);
                ShowFeedback(
                    $"Se encontró la Orden #{detailed.Id} activa en la Mesa #{tableState.Number:00} ({OrderApiService.FormatStatus(detailed.Status)}, Total {detailed.Total:C2}). Revisa sus líneas antes de vaciar o enviar el borrador.",
                    isError: false);
            }
            else
            {
                ShowFeedback(
                    $"La Mesa #{tableState.Number:00} sigue {TableApiService.FormatStatus(tableState.Status)} y no registra una orden activa nueva. Ya puedes enviar el borrador si corresponde.",
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

    private async Task ReloadSelectedOrderAsync()
    {
        if (pendingOperation is not null || selectedOrder is not { } current) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            var refreshed = await orders.GetByIdAsync(current.Id, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;
            ReplaceSelectedOrder(refreshed);
            ShowFeedback($"Orden #{refreshed.Id} recargada (Versión {refreshed.Version}, Estado: {OrderApiService.FormatStatus(refreshed.Status)}).", isError: false);
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

    private async Task AddLineToSelectedOrderAsync()
    {
        if (pendingOperation is not null || selectedOrder is not { } current) return;
        if (addLineProductCombo.SelectedItem is not ProductComboItem { Product: var product })
        {
            ShowFeedback("Selecciona un producto disponible para agregar a la orden.", isError: true);
            return;
        }

        var quantity = (int)addLineQuantity.Value;
        var note = addLineNote.Text;
        await ExecuteOrderMutationAsync(
            current.Id,
            token => orders.AddItemAsync(current.Id, product.Id, quantity, note, token),
            updated =>
            {
                addLineQuantity.Value = 1;
                addLineNote.Clear();
                return $"Línea agregada a la Orden #{updated.Id}. Nuevo total del servidor: {updated.Total:C2}.";
            });
    }

    private async Task UpdateSelectedLineAsync()
    {
        if (pendingOperation is not null || selectedOrder is not { } current || selectedDetail is not { } detail) return;
        var quantity = (int)editLineQuantity.Value;
        var note = editLineNote.Text;

        await ExecuteOrderMutationAsync(
            current.Id,
            token => orders.UpdateItemAsync(current.Id, detail.Id, quantity, note, token),
            updated => $"Línea #{detail.Id} actualizada en la Orden #{updated.Id}. Total del servidor: {updated.Total:C2}.");
    }

    private async Task RemoveSelectedLineAsync()
    {
        if (pendingOperation is not null || selectedOrder is not { } current || selectedDetail is not { } detail) return;

        await ExecuteOrderMutationAsync(
            current.Id,
            token => orders.RemoveItemAsync(current.Id, detail.Id, token),
            updated => $"Línea #{detail.Id} eliminada de la Orden #{updated.Id}. Total del servidor: {updated.Total:C2}.");
    }

    private async Task ChangeSelectedOrderStatusAsync(OrderStatus nextStatus)
    {
        if (pendingOperation is not null || selectedOrder is not { } current) return;

        await ExecuteOrderMutationAsync(
            current.Id,
            token => orders.ChangeStatusAsync(current.Id, nextStatus, current.Version, token),
            updated => $"Orden #{updated.Id} actualizada a estado {OrderApiService.FormatStatus(updated.Status)}.",
            refreshTablesAfterSuccess: nextStatus == OrderStatus.Cancelled);
    }

    private async Task ConfirmAndCancelSelectedOrderAsync()
    {
        if (pendingOperation is not null || selectedOrder is not { } current) return;
        var message = current.TableId.HasValue
            ? $"¿Deseas cancelar la Orden #{current.Id} de la Mesa #{current.TableNumber ?? current.TableId}?\n\nSi no hay otra orden activa en la mesa, esta quedará disponible."
            : $"¿Deseas cancelar la Orden #{current.Id} para llevar?";
        var confirmation = MessageBox.Show(
            this,
            message,
            "Confirmar cancelación de orden",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirmation == DialogResult.Yes)
            await ChangeSelectedOrderStatusAsync(OrderStatus.Cancelled);
    }

    private async Task RequestSelectedOrderAccountAsync()
    {
        if (pendingOperation is not null || selectedOrder is not { } current) return;

        await ExecuteOrderMutationAsync(
            current.Id,
            token => orders.RequestAccountAsync(current.Id, current.Version, accountRequested: true, token),
            updated => $"Cuenta solicitada para la Orden #{updated.Id} (Total: {updated.Total:C2}).");
    }

    private async Task ExecuteOrderMutationAsync(
        long orderId,
        Func<CancellationToken, Task<OrderDto>> mutation,
        Func<OrderDto, string> successMessage,
        bool refreshTablesAfterSuccess = false)
    {
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            var updated = await mutation(cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            if (refreshTablesAfterSuccess)
                await LoadCatalogAndTablesCoreAsync(null, (tableFilterCombo.SelectedItem as TableFilterItem)?.TableId, cts.Token);

            ReplaceSelectedOrder(updated);
            ShowFeedback(successMessage(updated), isError: false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (ApiException error)
        {
            if (cts.IsCancellationRequested || IsDisposed) return;

            if (error.StatusCode == HttpStatusCode.Conflict || error.StatusCode is null || (int)error.StatusCode.Value >= 500)
            {
                try
                {
                    var reloaded = await orders.GetByIdAsync(orderId, cts.Token);
                    if (!cts.IsCancellationRequested && !IsDisposed)
                    {
                        ReplaceSelectedOrder(reloaded);
                        ShowFeedback(
                            $"{error.Message} Se recargó la Orden #{reloaded.Id} (Versión {reloaded.Version}, Estado: {OrderApiService.FormatStatus(reloaded.Status)}) para que revises los datos actuales.",
                            isError: true);
                        return;
                    }
                }
                catch (ApiException)
                {
                    // Keep original error message if reload also fails.
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested) { return; }
            }

            ShowFeedback(error.Message, isError: true);
        }
        finally
        {
            pendingOperation = null;
            if (!IsDisposed) SetBusy(false);
        }
    }

    private void ReplaceSelectedOrder(OrderDto updated)
    {
        var preferredDetailId = selectedDetail?.Id;
        selectedOrder = updated;

        var list = currentOrders.ToList();
        var index = list.FindIndex(o => o.Id == updated.Id);
        if (index >= 0) list[index] = updated;
        else list.Insert(0, updated);
        currentOrders = list;

        BindOrdersGrid(updated.Id);
        BindDetailsGrid(preferredDetailId);
        UpdateSelectedOrderView();
    }

    private Control BuildNewOrderCard()
    {
        var card = DesktopTheme.Stack();
        card.Dock = DockStyle.Top;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(20);
        card.Margin = new Padding(0, 0, 0, 16);

        var headerRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 10)
        };
        var titleLabel = DesktopTheme.Label("1. Crear nueva orden", DesktopTheme.Strong, DesktopTheme.Ink);
        titleLabel.Margin = new Padding(0, 8, 16, 0);
        headerRow.Controls.Add(titleLabel);
        headerRow.Controls.Add(reloadCatalogButton);
        card.Controls.Add(headerRow);

        var inputBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 10)
        };

        newOrderTableCombo.AccessibleName = "Mesa disponible para la nueva orden";
        newOrderProductCombo.AccessibleName = "Producto disponible para agregar al borrador";
        newOrderQuantity.AccessibleName = "Cantidad del producto para el borrador";
        newOrderItemNote.AccessibleName = "Nota del producto en el borrador";

        inputBar.Controls.Add(LabeledField("Mesa disponible", newOrderTableCombo));
        takeAwayToggle.Margin = new Padding(0, 25, 12, 0);
        inputBar.Controls.Add(takeAwayToggle);
        inputBar.Controls.Add(LabeledField("Producto (ID · Nombre · Precio)", newOrderProductCombo));
        inputBar.Controls.Add(LabeledField("Cant.", newOrderQuantity));
        inputBar.Controls.Add(LabeledField("Nota de línea", newOrderItemNote));
        addDraftButton.Margin = new Padding(0, 22, 8, 0);
        inputBar.Controls.Add(addDraftButton);
        card.Controls.Add(inputBar);

        ConfigureGrid(draftGrid, "Líneas en borrador de la nueva orden", 130);
        draftGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProductId", HeaderText = "ID Prod.", FillWeight = 12 });
        draftGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProductName", HeaderText = "Producto", FillWeight = 34 });
        draftGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "Cant.", FillWeight = 12 });
        draftGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "UnitPrice", HeaderText = "Precio Catálogo", FillWeight = 16 });
        draftGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Subtotal", HeaderText = "Subtotal Est.", FillWeight = 16 });
        draftGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Note", HeaderText = "Nota", FillWeight = 22 });
        draftGrid.SelectionChanged += (_, _) => UpdateDraftActionStates();
        card.Controls.Add(draftGrid);

        var footerBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 10, 0, 0)
        };
        newOrderNotes.AccessibleName = "Notas generales de la nueva orden";
        newOrderNotes.Margin = new Padding(0, 6, 12, 6);
        removeDraftButton.Margin = new Padding(0, 0, 8, 6);
        clearDraftButton.Margin = new Padding(0, 0, 16, 6);
        draftTotalLabel.Margin = new Padding(0, 9, 16, 6);
        createOrderButton.Margin = new Padding(0, 0, 8, 6);
        verifyUncertainButton.Margin = new Padding(0, 0, 0, 6);
        verifyUncertainButton.Visible = false;

        footerBar.Controls.Add(removeDraftButton);
        footerBar.Controls.Add(clearDraftButton);
        footerBar.Controls.Add(newOrderNotes);
        footerBar.Controls.Add(draftTotalLabel);
        footerBar.Controls.Add(createOrderButton);
        footerBar.Controls.Add(verifyUncertainButton);
        card.Controls.Add(footerBar);

        return card;
    }

    private Control BuildOrdersListCard()
    {
        var card = Stack();
        card.Dock = DockStyle.Top;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(20);
        card.Margin = new Padding(0, 0, 0, 16);

        var heading = DesktopTheme.Label("2. Consultar y filtrar órdenes", DesktopTheme.Strong, DesktopTheme.Ink);
        heading.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(heading);

        var filterBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 10)
        };

        tableFilterCombo.AccessibleName = "Filtrar órdenes por mesa";
        statusFilterCombo.AccessibleName = "Filtrar órdenes por estado";
        orderIdFilter.AccessibleName = "Filtrar por número de orden";
        startDatePicker.AccessibleName = "Fecha inicial del filtro";
        endDatePicker.AccessibleName = "Fecha final del filtro";

        filterBar.Controls.Add(LabeledField("Mesa", tableFilterCombo));
        filterBar.Controls.Add(LabeledField("Estado", statusFilterCombo));
        filterBar.Controls.Add(LabeledField("ID Orden (0=todas)", orderIdFilter));

        dateRangeToggle.ForeColor = DesktopTheme.Ink;
        dateRangeToggle.Margin = new Padding(6, 28, 8, 0);
        filterBar.Controls.Add(dateRangeToggle);
        filterBar.Controls.Add(LabeledField("Desde", startDatePicker));
        filterBar.Controls.Add(LabeledField("Hasta", endDatePicker));

        searchOrdersButton.Margin = new Padding(4, 22, 8, 0);
        clearFiltersButton.Margin = new Padding(0, 22, 0, 0);
        filterBar.Controls.Add(searchOrdersButton);
        filterBar.Controls.Add(clearFiltersButton);
        card.Controls.Add(filterBar);

        ConfigureGrid(ordersGrid, "Listado de órdenes", 165);
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "OrderId", HeaderText = "ID Orden", FillWeight = 13 });
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Table", HeaderText = "Mesa", FillWeight = 15 });
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Estado", FillWeight = 18 });
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Account", HeaderText = "Cuenta", FillWeight = 14 });
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Total", HeaderText = "Total Servidor", FillWeight = 16 });
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Items", HeaderText = "Líneas", FillWeight = 10 });
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Date", HeaderText = "Fecha", FillWeight = 22 });
        ordersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Version", HeaderText = "Versión", FillWeight = 10 });
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

    private Control BuildOrderDetailCard()
    {
        var card = Stack();
        card.Dock = DockStyle.Top;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(20);
        card.Margin = Padding.Empty;

        card.Controls.Add(new Panel { Size = new Size(34, 3), BackColor = DesktopTheme.Olive, Margin = new Padding(0, 0, 0, 10) });
        selectedOrderTitle.Margin = new Padding(0, 0, 0, 4);
        selectedOrderMeta.Margin = new Padding(0, 0, 0, 6);
        selectedOrderNotes.Margin = new Padding(0, 0, 0, 4);
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
        reloadOrderButton.Margin = new Padding(0, 0, 8, 6);
        markServedButton.Margin = new Padding(0, 0, 8, 6);
        requestAccountButton.Margin = new Padding(0, 0, 8, 6);
        cancelOrderButton.Margin = new Padding(0, 0, 0, 6);

        actionsBar.Controls.Add(reloadOrderButton);
        actionsBar.Controls.Add(markServedButton);
        actionsBar.Controls.Add(requestAccountButton);
        actionsBar.Controls.Add(cancelOrderButton);
        card.Controls.Add(actionsBar);

        ConfigureGrid(detailsGrid, "Líneas de la orden seleccionada", 155);
        detailsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DetailId", HeaderText = "ID Línea", FillWeight = 12 });
        detailsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProductId", HeaderText = "ID Producto", FillWeight = 13 });
        detailsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProductName", HeaderText = "Producto", FillWeight = 29 });
        detailsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "Cant.", FillWeight = 10 });
        detailsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "UnitPrice", HeaderText = "Precio Unit.", FillWeight = 15 });
        detailsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Subtotal", HeaderText = "Subtotal", FillWeight = 15 });
        detailsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Note", HeaderText = "Nota", FillWeight = 22 });
        card.Controls.Add(detailsGrid);

        var addLineBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 12, 0, 6)
        };
        addLineProductCombo.AccessibleName = "Producto para agregar a la orden seleccionada";
        addLineQuantity.AccessibleName = "Cantidad para agregar a la orden seleccionada";
        addLineNote.AccessibleName = "Nota para la nueva línea de la orden";
        addLineButton.Margin = new Padding(0, 22, 0, 0);

        addLineBar.Controls.Add(LabeledField("Agregar producto a esta orden", addLineProductCombo));
        addLineBar.Controls.Add(LabeledField("Cant.", addLineQuantity));
        addLineBar.Controls.Add(LabeledField("Nota", addLineNote));
        addLineBar.Controls.Add(addLineButton);
        card.Controls.Add(addLineBar);

        var editLineBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 6, 0, 0)
        };
        selectedLineLabel.Margin = new Padding(0, 26, 14, 0);
        editLineQuantity.AccessibleName = "Nueva cantidad para la línea seleccionada";
        editLineNote.AccessibleName = "Nueva nota para la línea seleccionada";
        updateLineButton.Margin = new Padding(0, 22, 8, 0);
        removeLineButton.Margin = new Padding(0, 22, 0, 0);

        editLineBar.Controls.Add(selectedLineLabel);
        editLineBar.Controls.Add(DesktopTheme.LabeledField("Cant. línea", editLineQuantity));
        editLineBar.Controls.Add(DesktopTheme.LabeledField("Nota línea", editLineNote));
        editLineBar.Controls.Add(updateLineButton);
        editLineBar.Controls.Add(removeLineButton);
        card.Controls.Add(editLineBar);

        return card;
    }

    private static void ConfigureGrid(DataGridView grid, string accessibleName, int height) =>
        DesktopTheme.ConfigureGrid(grid, accessibleName, height);

    private static Control LabeledField(string caption, Control input) =>
        DesktopTheme.LabeledField(caption, input);

    private static TableLayoutPanel Stack() => DesktopTheme.Stack();

    private void PopulateStatusFilter()
    {
        statusFilterCombo.Items.Clear();
        statusFilterCombo.Items.Add(new StatusFilterItem(null, "Todos los estados"));
        foreach (var status in Enum.GetValues<OrderStatus>())
            statusFilterCombo.Items.Add(new StatusFilterItem(status, OrderApiService.FormatStatus(status)));
        statusFilterCombo.SelectedIndex = 0;
    }

    private void BindAvailableTables(long? preferredTableId)
    {
        var targetId = preferredTableId ?? (newOrderTableCombo.SelectedItem as TableComboItem)?.Table.Id;
        newOrderTableCombo.Items.Clear();
        int selectedIndex = -1;
        for (var i = 0; i < availableTables.Count; i++)
        {
            var table = availableTables[i];
            var loc = string.IsNullOrWhiteSpace(table.Location) ? "Salón" : table.Location;
            var item = new TableComboItem(table, $"Mesa #{table.Number:00} · {table.Capacity}p ({loc})");
            newOrderTableCombo.Items.Add(item);
            if (targetId.HasValue && table.Id == targetId.Value)
                selectedIndex = i;
        }

        if (selectedIndex >= 0) newOrderTableCombo.SelectedIndex = selectedIndex;
        else if (!draftReservationId.HasValue && newOrderTableCombo.Items.Count > 0) newOrderTableCombo.SelectedIndex = 0;
    }

    private void BindTableFilter(long? filterTableId)
    {
        var targetId = filterTableId ?? (tableFilterCombo.SelectedItem as TableFilterItem)?.TableId;
        tableFilterCombo.Items.Clear();
        tableFilterCombo.Items.Add(new TableFilterItem(null, "Todas las mesas"));
        int selectedIndex = 0;
        for (var i = 0; i < allTables.Count; i++)
        {
            var table = allTables[i];
            tableFilterCombo.Items.Add(new TableFilterItem(table.Id, $"Mesa #{table.Number:00} ({TableApiService.FormatStatus(table.Status)})"));
            if (targetId.HasValue && table.Id == targetId.Value)
                selectedIndex = i + 1;
        }
        tableFilterCombo.SelectedIndex = selectedIndex;
    }

    private void BindProductCombos()
    {
        var prevNewId = (newOrderProductCombo.SelectedItem as ProductComboItem)?.Product.Id;
        var prevAddId = (addLineProductCombo.SelectedItem as ProductComboItem)?.Product.Id;
        newOrderProductCombo.Items.Clear();
        addLineProductCombo.Items.Clear();

        int newIdx = -1;
        int addIdx = -1;
        for (var i = 0; i < availableProducts.Count; i++)
        {
            var p = availableProducts[i];
            var text = $"[#{p.Id}] {p.Name} — {p.Price:C2} ({p.CategoryName})";
            var item = new ProductComboItem(p, text);
            newOrderProductCombo.Items.Add(item);
            addLineProductCombo.Items.Add(item);
            if (prevNewId.HasValue && p.Id == prevNewId.Value) newIdx = i;
            if (prevAddId.HasValue && p.Id == prevAddId.Value) addIdx = i;
        }

        if (newIdx >= 0) newOrderProductCombo.SelectedIndex = newIdx;
        else if (newOrderProductCombo.Items.Count > 0) newOrderProductCombo.SelectedIndex = 0;

        if (addIdx >= 0) addLineProductCombo.SelectedIndex = addIdx;
        else if (addLineProductCombo.Items.Count > 0) addLineProductCombo.SelectedIndex = 0;
    }

    private void RefreshDraftView()
    {
        draftGrid.Rows.Clear();
        decimal estimatedTotal = 0m;
        foreach (var line in draftItems)
        {
            var subtotal = line.UnitPrice * line.Quantity;
            estimatedTotal += subtotal;
            var rowIndex = draftGrid.Rows.Add(
                $"#{line.ProductId}",
                line.ProductName,
                line.Quantity.ToString(CultureInfo.InvariantCulture),
                line.UnitPrice.ToString("C2", CultureInfo.CurrentCulture),
                subtotal.ToString("C2", CultureInfo.CurrentCulture),
                line.Note ?? "");
            draftGrid.Rows[rowIndex].Tag = line;
        }

        var reservationNote = draftReservationId.HasValue ? $" · Vinculada a Reserva #{draftReservationId.Value}" : "";
        draftTotalLabel.Text = $"Total estimado borrador: {estimatedTotal:C2}{reservationNote} (el servidor calcula el importe final)";
        UpdateDraftActionStates();
    }

    private void UpdateDraftActionStates()
    {
        var busy = pendingOperation is not null;
        var hasDraft = draftItems.Count > 0;
        var hasDraftSelection = draftGrid.CurrentRow?.Tag is DraftLine;
        var hasAvailableTable = newOrderTableCombo.SelectedItem is TableComboItem;
        var hasAvailableProduct = newOrderProductCombo.SelectedItem is ProductComboItem;
        var canEditDraft = !busy && !uncertainTakeAwaySubmission;

        takeAwayToggle.Enabled = canEditDraft && !draftReservationId.HasValue;
        newOrderTableCombo.Enabled = canEditDraft && !takeAwayToggle.Checked && !draftReservationId.HasValue;
        addDraftButton.Enabled = canEditDraft && hasAvailableProduct;
        removeDraftButton.Enabled = canEditDraft && hasDraftSelection;
        clearDraftButton.Enabled = !busy && (hasDraft || newOrderNotes.TextLength > 0);
        createOrderButton.Enabled = canEditDraft && hasDraft && (takeAwayToggle.Checked || hasAvailableTable);
        var uncertain = uncertainSubmissionTableId.HasValue || uncertainTakeAwaySubmission;
        verifyUncertainButton.Text = UncertainOrderAction;
        verifyUncertainButton.Visible = uncertain;
        verifyUncertainButton.Enabled = !busy && uncertain;
    }

    private string UncertainOrderAction => uncertainTakeAwaySubmission ? "Consultar órdenes recientes" : "Consultar mesa y órdenes recientes";

    private void BindOrdersGrid(long? preferredOrderId)
    {
        suppressOrdersSelection = true;
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
                var rowIndex = ordersGrid.Rows.Add(
                    $"#{order.Id}",
                    tableText,
                    OrderApiService.FormatStatus(order.Status),
                    order.AccountRequested ? "Solicitada" : "No",
                    order.Total.ToString("C2", CultureInfo.CurrentCulture),
                    order.Details.Count.ToString(CultureInfo.InvariantCulture),
                    order.OrderDateTime.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    order.Version.ToString(CultureInfo.InvariantCulture));
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
            }
            else
            {
                selectedOrder = null;
                selectedDetail = null;
            }
        }
        finally
        {
            suppressOrdersSelection = false;
        }

        pageStatusLabel.Text = $"Página {currentPage} de {totalPages} · {totalOrdersCount} {(totalOrdersCount == 1 ? "orden" : "órdenes")}";
        prevPageButton.Enabled = pendingOperation is null && currentPage > 1;
        nextPageButton.Enabled = pendingOperation is null && currentPage < totalPages;
        BindDetailsGrid(selectedDetail?.Id);
        UpdateSelectedOrderView();
    }

    private void BindDetailsGrid(long? preferredDetailId)
    {
        suppressDetailsSelection = true;
        try
        {
            detailsGrid.Rows.Clear();
            var details = selectedOrder?.Details?.ToArray() ?? [];
            int selectedIndex = -1;
            for (var i = 0; i < details.Length; i++)
            {
                var d = details[i];
                var rowIndex = detailsGrid.Rows.Add(
                    $"#{d.Id}",
                    $"#{d.ProductId}",
                    d.ProductName,
                    d.Quantity.ToString(CultureInfo.InvariantCulture),
                    d.UnitPrice.ToString("C2", CultureInfo.CurrentCulture),
                    d.Subtotal.ToString("C2", CultureInfo.CurrentCulture),
                    d.Note ?? "");
                detailsGrid.Rows[rowIndex].Tag = d;
                if (preferredDetailId.HasValue && d.Id == preferredDetailId.Value)
                    selectedIndex = rowIndex;
            }

            if (selectedIndex < 0 && details.Length > 0)
                selectedIndex = 0;

            if (selectedIndex >= 0)
            {
                detailsGrid.ClearSelection();
                detailsGrid.Rows[selectedIndex].Selected = true;
                detailsGrid.CurrentCell = detailsGrid.Rows[selectedIndex].Cells[0];
                selectedDetail = details[selectedIndex];
            }
            else
            {
                selectedDetail = null;
            }
        }
        finally
        {
            suppressDetailsSelection = false;
        }

        SyncSelectedDetailEditor();
    }

    private void SyncSelectedDetailEditor()
    {
        var busy = pendingOperation is not null;
        var canEdit = selectedOrder is not null && OrderApiService.CanEditItems(selectedOrder.Status);
        if (selectedDetail is { } detail)
        {
            selectedLineLabel.Text = $"Línea #{detail.Id} (Prod. #{detail.ProductId} · {detail.ProductName})";
            editLineQuantity.Value = Math.Clamp(detail.Quantity, (int)editLineQuantity.Minimum, (int)editLineQuantity.Maximum);
            editLineNote.Text = detail.Note ?? "";
            editLineQuantity.Enabled = editLineNote.Enabled = updateLineButton.Enabled = removeLineButton.Enabled = !busy && canEdit;
        }
        else
        {
            selectedLineLabel.Text = "Línea seleccionada: ninguna";
            editLineQuantity.Value = 1;
            editLineNote.Clear();
            editLineQuantity.Enabled = editLineNote.Enabled = updateLineButton.Enabled = removeLineButton.Enabled = false;
        }
    }

    private void UpdateSelectedOrderView()
    {
        var busy = pendingOperation is not null;
        if (selectedOrder is not { } order)
        {
            selectedOrderTitle.Text = "Selecciona una orden o crea una nueva";
            selectedOrderMeta.Text = "Ninguna orden seleccionada.";
            selectedOrderNotes.Text = "";
            selectedOrderGuidance.Text = "Usa el listado superior para elegir una orden y consultar sus líneas, modificar productos, cambiar estado o solicitar cuenta.";
            reloadOrderButton.Enabled = false;
            markServedButton.Enabled = false;
            requestAccountButton.Enabled = false;
            cancelOrderButton.Enabled = false;
            addLineProductCombo.Enabled = addLineQuantity.Enabled = addLineNote.Enabled = addLineButton.Enabled = false;
            SyncSelectedDetailEditor();
            return;
        }

        var tableText = order.TableNumber.HasValue
            ? $"Mesa #{order.TableNumber.Value:00} (ID #{order.TableId})"
            : order.TableId.HasValue ? $"Mesa #{order.TableId.Value}" : "Para llevar";
        selectedOrderTitle.Text = $"Orden #{order.Id} · {tableText}";
        var reservationMeta = order.ReservationId.HasValue ? $"   |   Reserva: #{order.ReservationId.Value}" : "";
        selectedOrderMeta.Text =
            $"Estado: {OrderApiService.FormatStatus(order.Status)}{reservationMeta}   |   " +
            $"Cuenta: {(order.AccountRequested ? "Solicitada" : "No solicitada")}   |   " +
            $"Total servidor: {order.Total:C2}   |   " +
            $"Versión: {order.Version}";
        selectedOrderNotes.Text = string.IsNullOrWhiteSpace(order.Notes)
            ? "Notas de la orden: sin notas."
            : $"Notas de la orden: {order.Notes}";

        selectedOrderGuidance.Text = order.Status switch
        {
            OrderStatus.Pending or OrderStatus.InPreparation =>
                "Orden activa: puedes agregar, modificar o quitar líneas, solicitar cuenta o cancelar. Cocina controla el paso a «Lista para servir».",
            OrderStatus.Ready =>
                "La orden está lista en cocina: puedes marcarla como «Servida», ajustar líneas, solicitar cuenta o cancelar.",
            OrderStatus.Served =>
                "Orden servida en mesa: puedes agregar o ajustar líneas, solicitar la cuenta para caja o cancelar.",
            OrderStatus.Paid =>
                "Orden pagada y cerrada (solo lectura).",
            OrderStatus.Cancelled =>
                "Orden cancelada (solo lectura).",
            OrderStatus.PreOrdered =>
                "Preorden vinculada a reservación Web (no operativa desde mesero en esta fase).",
            _ => ""
        };

        var canEdit = OrderApiService.CanEditItems(order.Status);
        var hasProduct = addLineProductCombo.SelectedItem is ProductComboItem;

        reloadOrderButton.Enabled = !busy;
        markServedButton.Enabled = !busy && OrderApiService.CanMarkServed(order.Status);
        requestAccountButton.Enabled = !busy && OrderApiService.CanRequestAccount(order);
        cancelOrderButton.Enabled = !busy && OrderApiService.CanCancel(order.Status);

        addLineProductCombo.Enabled = addLineQuantity.Enabled = addLineNote.Enabled = !busy && canEdit;
        addLineButton.Enabled = !busy && canEdit && hasProduct;

        SyncSelectedDetailEditor();
    }

    private void SetBusy(bool busy)
    {
        reloadCatalogButton.Enabled = newOrderProductCombo.Enabled =
            newOrderQuantity.Enabled = newOrderItemNote.Enabled = newOrderNotes.Enabled =
            draftGrid.Enabled = tableFilterCombo.Enabled = statusFilterCombo.Enabled =
            orderIdFilter.Enabled = dateRangeToggle.Enabled = searchOrdersButton.Enabled =
            clearFiltersButton.Enabled = ordersGrid.Enabled = detailsGrid.Enabled = !busy;

        startDatePicker.Enabled = endDatePicker.Enabled = !busy && dateRangeToggle.Checked;
        prevPageButton.Enabled = !busy && currentPage > 1;
        nextPageButton.Enabled = !busy && currentPage < totalPages;
        progress.Visible = busy;
        UseWaitCursor = busy;

        UpdateDraftActionStates();
        UpdateSelectedOrderView();
    }

    private void ShowFeedback(string message, bool isError) =>
        DesktopTheme.ShowFeedback(feedback, message, isError);

    private sealed class DraftLine
    {
        public required long ProductId { get; init; }
        public required string ProductName { get; init; }
        public required decimal UnitPrice { get; init; }
        public int Quantity { get; set; }
        public string? Note { get; init; }
    }

    private sealed record TableComboItem(TableDto Table, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record TableFilterItem(long? TableId, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record StatusFilterItem(OrderStatus? Status, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ProductComboItem(ProductDto Product, string Label)
    {
        public override string ToString() => Label;
    }
}
