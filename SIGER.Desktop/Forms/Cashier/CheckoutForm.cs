using System.Globalization;
using System.Net;
using System.Windows.Forms.Automation;
using SIGER.Application.DTOs.Orders;
using SIGER.Application.DTOs.Payments;
using SIGER.Desktop.Controls;
using SIGER.Desktop.Services;
using SIGER.Domain.Enums;

namespace SIGER.Desktop.Forms.Cashier;

internal sealed record PaymentCompletedEventArgs(OrderDto Order, PaymentDto Payment);

internal sealed class CheckoutForm : UserControl
{
    private readonly OrderApiService orders;
    private readonly PaymentApiService payments;

    // Order lookup & selector bar
    private readonly ComboBox candidateOrdersCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
    private readonly NumericUpDown orderIdInput = new() { Minimum = 1, Maximum = 9999999, Value = 1, Width = 105 };
    private readonly Button loadByIdButton = DesktopTheme.Button("Buscar &ID", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button refreshCandidatesButton = DesktopTheme.Button("&Actualizar órdenes", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);

    // Feedback & progress
    private readonly Label feedback = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Error);
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Height = 5, Dock = DockStyle.Fill };

    // Selected order summary & lines
    private readonly Label orderTitleLabel = DesktopTheme.Label("Selecciona o busca una orden para cobrar", DesktopTheme.Brand, DesktopTheme.Ink);
    private readonly Label orderMetaLabel = DesktopTheme.Label("", DesktopTheme.Strong, DesktopTheme.Muted);
    private readonly Label serverTotalLabel = DesktopTheme.Label("Total oficial del servidor: $0.00", DesktopTheme.BrandSubtitle, DesktopTheme.Ink);
    private readonly Label operationalNoticeLabel = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Muted);
    private readonly DataGridView detailsGrid = new();

    // Payment registration & reconciliation controls
    private readonly ComboBox methodCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly TextBox referenceInput = new() { MaxLength = ProcessPaymentRequestDto.MaxReferenceLength, PlaceholderText = "Folio / comprobante no sensible (opcional)", Width = 280 };
    private readonly Label securityHintLabel = DesktopTheme.Label(
        "Registro administrativo: NO solicites ni escribas número completo de tarjeta, CVV ni PIN.",
        DesktopTheme.Small,
        DesktopTheme.Muted);
    private readonly Button confirmPaymentButton = DesktopTheme.Button("&Confirmar y registrar pago", DesktopTheme.Olive, DesktopTheme.Ink, DesktopTheme.SoftOlive);
    private readonly Button reconcileButton = DesktopTheme.Button("&Reconciliar pago con el servidor", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);
    private readonly Label paymentResultLabel = DesktopTheme.Label("Resultado del pago: Sin pago registrado para esta orden.", DesktopTheme.Strong, DesktopTheme.Ink);

    private IReadOnlyList<OrderDto> candidateOrders = [];
    private OrderDto? currentOrder;
    private PaymentDto? currentPayment;
    private long? uncertainPaymentOrderId;
    private bool initialized;
    private bool suppressComboSelection;
    private CancellationTokenSource? pendingOperation;

    internal event EventHandler<PaymentCompletedEventArgs>? PaymentCompleted;

    internal CheckoutForm(OrderApiService orders, PaymentApiService payments)
    {
        this.orders = orders;
        this.payments = payments;

        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = DesktopTheme.Cream;
        Font = DesktopTheme.Body;
        Margin = Padding.Empty;

        PopulateMethods();

        var root = Stack();
        root.Dock = DockStyle.Top;
        root.Controls.Add(BuildLookupBar());
        root.Controls.Add(DesktopTheme.FeedbackBar(feedback, progress, "Operación de cobro en curso"));
        root.Controls.Add(BuildOrderVerificationCard());
        root.Controls.Add(BuildPaymentActionCard());
        Controls.Add(root);

        candidateOrdersCombo.SelectedIndexChanged += async (_, _) =>
        {
            if (suppressComboSelection) return;
            if (candidateOrdersCombo.SelectedItem is OrderComboItem { Order: var selected })
                await LoadOrderCoreWrapperAsync(selected.Id);
        };
        loadByIdButton.Click += async (_, _) => await LoadOrderCoreWrapperAsync((long)orderIdInput.Value);
        refreshCandidatesButton.Click += async (_, _) => await RefreshCandidateOrdersAsync(currentOrder?.Id);
        confirmPaymentButton.Click += async (_, _) => await ConfirmAndProcessPaymentAsync();
        reconcileButton.Click += async (_, _) => await ReconcileUncertainPaymentAsync();

        UpdateViewState();
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
            await RefreshCandidateOrdersAsync(currentOrder?.Id);
        else if (currentOrder is { } active)
            await LoadOrderCoreWrapperAsync(active.Id, silentSuccess: true);
    }

    internal async Task LoadOrderForCheckoutAsync(long orderId)
    {
        orderIdInput.Value = Math.Clamp(orderId, orderIdInput.Minimum, orderIdInput.Maximum);
        await RefreshCandidateOrdersAsync(orderId);
    }

    private async Task RefreshCandidateOrdersAsync(long? preferredOrderId)
    {
        if (pendingOperation is not null) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            var page = await orders.GetPagedAsync(new OrderQueryFilter(1, 50), cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            candidateOrders = page.Items
                .Where(o => PaymentApiService.CanProcessPayment(o) || (preferredOrderId.HasValue && o.Id == preferredOrderId.Value))
                .OrderByDescending(o => o.AccountRequested)
                .ThenByDescending(o => o.Status == OrderStatus.Served)
                .ThenByDescending(o => o.OrderDateTime)
                .ToArray();

            var targetId = preferredOrderId ?? currentOrder?.Id ?? candidateOrders.FirstOrDefault()?.Id;
            BindCandidateCombo(targetId);

            if (targetId.HasValue)
            {
                await InspectOrderAndPaymentCoreAsync(targetId.Value, cts.Token);
            }
            else
            {
                currentOrder = null;
                currentPayment = null;
            }

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

    private async Task LoadOrderCoreWrapperAsync(long orderId, bool silentSuccess = false)
    {
        if (pendingOperation is not null) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        if (!silentSuccess) ShowFeedback("", isError: false);

        try
        {
            await InspectOrderAndPaymentCoreAsync(orderId, cts.Token);
            if (!silentSuccess && currentOrder is { } loaded)
            {
                ShowFeedback(
                    $"Orden #{loaded.Id} cargada desde el servidor. Total oficial: {loaded.Total:C2}.",
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

    private async Task InspectOrderAndPaymentCoreAsync(long orderId, CancellationToken token)
    {
        var fetchedOrder = await orders.GetByIdAsync(orderId, token);
        var fetchedPayment = await payments.TryGetByOrderIdAsync(orderId, token);
        if (token.IsCancellationRequested || IsDisposed) return;

        currentOrder = fetchedOrder;
        currentPayment = fetchedPayment;
        orderIdInput.Value = Math.Clamp(fetchedOrder.Id, orderIdInput.Minimum, orderIdInput.Maximum);

        if (uncertainPaymentOrderId == orderId && fetchedPayment is not null)
            uncertainPaymentOrderId = null;

        if (candidateOrders.All(o => o.Id != fetchedOrder.Id))
        {
            candidateOrders = [fetchedOrder, .. candidateOrders];
            BindCandidateCombo(fetchedOrder.Id);
        }
        else
        {
            candidateOrders = candidateOrders.Select(o => o.Id == fetchedOrder.Id ? fetchedOrder : o).ToArray();
            BindCandidateCombo(fetchedOrder.Id);
        }
    }

    private async Task ConfirmAndProcessPaymentAsync()
    {
        if (pendingOperation is not null || currentOrder is not { } snapshot) return;

        if (uncertainPaymentOrderId == snapshot.Id)
        {
            ShowFeedback(
                $"El cobro anterior de la Orden #{snapshot.Id} quedó en estado incierto. Pulsa «Reconciliar pago con el servidor» antes de intentar otro cobro.",
                isError: true);
            reconcileButton.Focus();
            return;
        }

        if (methodCombo.SelectedItem is not MethodComboItem { Method: var method })
        {
            ShowFeedback("Selecciona un método de pago válido.", isError: true);
            methodCombo.Focus();
            return;
        }

        var reference = referenceInput.Text.Trim();
        if (reference.Length > ProcessPaymentRequestDto.MaxReferenceLength)
        {
            ShowFeedback($"La referencia no puede exceder los {ProcessPaymentRequestDto.MaxReferenceLength} caracteres.", isError: true);
            referenceInput.Focus();
            return;
        }

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            // Step 1: Always revalidate current order state, total and existing payment on the server before charging.
            var latestOrder = await orders.GetByIdAsync(snapshot.Id, cts.Token);
            var existingPayment = await payments.TryGetByOrderIdAsync(snapshot.Id, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentOrder = latestOrder;
            currentPayment = existingPayment;

            if (existingPayment is not null)
            {
                ShowFeedback(
                    $"La Orden #{latestOrder.Id} ya tiene el Pago #{existingPayment.Id} registrado ({PaymentApiService.FormatMethod(existingPayment.Method)}, {existingPayment.Amount:C2}). No se envió un cobro duplicado.",
                    isError: true);
                return;
            }

            if (PaymentApiService.GetPaymentBlockReason(latestOrder) is { } blocked)
            {
                ShowFeedback(
                    $"Orden #{latestOrder.Id}: {blocked}",
                    isError: true);
                return;
            }

            if (latestOrder.Total != snapshot.Total || latestOrder.Version != snapshot.Version)
            {
                ShowFeedback(
                    $"La Orden #{latestOrder.Id} fue actualizada en el servidor (Total actual: {latestOrder.Total:C2}, Versión {latestOrder.Version}). Revisa los datos actualizados y confirma nuevamente el cobro.",
                    isError: true);
                return;
            }

            // Step 2: Submit the payment using the verified server total.
            var createdPayment = await payments.ProcessPaymentAsync(
                latestOrder.Id,
                latestOrder.Total,
                method,
                reference,
                latestOrder,
                cts.Token);

            if (cts.IsCancellationRequested || IsDisposed) return;

            uncertainPaymentOrderId = null;
            currentPayment = createdPayment;
            referenceInput.Clear();

            var paidOrder = await orders.GetByIdAsync(latestOrder.Id, cts.Token);
            if (!cts.IsCancellationRequested && !IsDisposed)
            {
                currentOrder = paidOrder;
                candidateOrders = candidateOrders.Select(o => o.Id == paidOrder.Id ? paidOrder : o).ToArray();
                BindCandidateCombo(paidOrder.Id);
                PaymentCompleted?.Invoke(this, new PaymentCompletedEventArgs(paidOrder, createdPayment));
            }

            ShowFeedback(
                $"Pago #{createdPayment.Id} registrado correctamente para la Orden #{createdPayment.OrderId} ({PaymentApiService.FormatMethod(createdPayment.Method)}, {createdPayment.Amount:C2}).",
                isError: false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (ApiException error)
        {
            if (cts.IsCancellationRequested || IsDisposed) return;

            if (currentPayment is { } confirmed && confirmed.OrderId == snapshot.Id)
            {
                ShowFeedback($"Pago #{confirmed.Id} registrado. No se pudo actualizar la vista de la orden: {error.Message}", isError: true);
                return;
            }
            if (error.StatusCode is null || (int)error.StatusCode.Value >= 500)
            {
                // Timeout or network interruption: mark uncertain and block blind retries until reconciled.
                uncertainPaymentOrderId = snapshot.Id;
                ShowFeedback(
                    $"{error.Message} El resultado del pago para la Orden #{snapshot.Id} es incierto. NO vuelvas a cobrar a ciegas; pulsa «Reconciliar pago con el servidor» para comprobar si se registró.",
                    isError: true);
                return;
            }

            if (error.StatusCode == HttpStatusCode.Conflict)
            {
                try
                {
                    await InspectOrderAndPaymentCoreAsync(snapshot.Id, cts.Token);
                    if (!cts.IsCancellationRequested && !IsDisposed)
                    {
                        ShowFeedback(
                            $"{error.Message} Se actualizaron los datos de la Orden #{snapshot.Id} desde el servidor.",
                            isError: true);
                        return;
                    }
                }
                catch (ApiException)
                {
                    // Keep original conflict error if refresh fails.
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

    private async Task ReconcileUncertainPaymentAsync()
    {
        if (pendingOperation is not null) return;
        var targetOrderId = uncertainPaymentOrderId ?? currentOrder?.Id;
        if (!targetOrderId.HasValue) return;

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            var reconciledPayment = await payments.TryGetByOrderIdAsync(targetOrderId.Value, cts.Token);
            var reconciledOrder = await orders.GetByIdAsync(targetOrderId.Value, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentOrder = reconciledOrder;
            currentPayment = reconciledPayment;
            uncertainPaymentOrderId = null;

            candidateOrders = candidateOrders.Select(o => o.Id == reconciledOrder.Id ? reconciledOrder : o).ToArray();
            BindCandidateCombo(reconciledOrder.Id);

            if (reconciledPayment is not null)
            {
                referenceInput.Clear();
                PaymentCompleted?.Invoke(this, new PaymentCompletedEventArgs(reconciledOrder, reconciledPayment));
                ShowFeedback(
                    $"Reconciliación completada: el Pago #{reconciledPayment.Id} SÍ fue registrado ({PaymentApiService.FormatMethod(reconciledPayment.Method)}, {reconciledPayment.Amount:C2}). La Orden #{reconciledOrder.Id} está Pagada.",
                    isError: false);
            }
            else
            {
                ShowFeedback(
                    $"Reconciliación completada: el servidor confirmó que NO se registró pago para la Orden #{reconciledOrder.Id} (Estado: {OrderApiService.FormatStatus(reconciledOrder.Status)}). Ya puedes registrar el cobro si corresponde.",
                    isError: false);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (ApiException error)
        {
            if (!cts.IsCancellationRequested && !IsDisposed)
            {
                ShowFeedback(
                    $"{error.Message} No se pudo reconciliar el estado del pago; se mantiene el bloqueo preventivo para evitar un cobro duplicado.",
                    isError: true);
            }
        }
        finally
        {
            pendingOperation = null;
            if (!IsDisposed) SetBusy(false);
        }
    }

    private Control BuildLookupBar()
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

        candidateOrdersCombo.AccessibleName = "Seleccionar orden pendiente de cobro";
        orderIdInput.AccessibleName = "Identificador de orden a buscar";

        bar.Controls.Add(LabeledField("Órdenes disponibles para cobro", candidateOrdersCombo));
        bar.Controls.Add(LabeledField("ID de orden", orderIdInput));
        loadByIdButton.Margin = new Padding(0, 22, 10, 0);
        refreshCandidatesButton.Margin = new Padding(0, 22, 0, 0);
        bar.Controls.Add(loadByIdButton);
        bar.Controls.Add(refreshCandidatesButton);
        return bar;
    }

    private Control BuildOrderVerificationCard()
    {
        var card = Stack();
        card.Dock = DockStyle.Top;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(20);
        card.Margin = new Padding(0, 0, 0, 16);

        var heading = DesktopTheme.Label("1. Verificación de la orden e importe del servidor", DesktopTheme.Strong, DesktopTheme.Ink);
        heading.Margin = new Padding(0, 0, 0, 8);
        card.Controls.Add(heading);

        orderTitleLabel.Margin = new Padding(0, 0, 0, 4);
        orderMetaLabel.Margin = new Padding(0, 0, 0, 6);
        serverTotalLabel.Margin = new Padding(0, 0, 0, 6);
        operationalNoticeLabel.MaximumSize = new Size(880, 0);
        operationalNoticeLabel.Margin = new Padding(0, 0, 0, 10);

        card.Controls.Add(orderTitleLabel);
        card.Controls.Add(orderMetaLabel);
        card.Controls.Add(serverTotalLabel);
        card.Controls.Add(operationalNoticeLabel);

        DesktopTheme.ConfigureGrid(detailsGrid, "Detalle de productos de la orden a cobrar", 160);

        detailsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Product", HeaderText = "Producto", FillWeight = 36 });
        detailsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "Cant.", FillWeight = 12 });
        detailsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "UnitPrice", HeaderText = "Precio Unit.", FillWeight = 18 });
        detailsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Subtotal", HeaderText = "Subtotal", FillWeight = 18 });
        detailsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Note", HeaderText = "Nota", FillWeight = 24 });

        card.Controls.Add(detailsGrid);
        return card;
    }

    private Control BuildPaymentActionCard()
    {
        var card = Stack();
        card.Dock = DockStyle.Top;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(20);
        card.Margin = Padding.Empty;

        card.Controls.Add(new Panel { Size = new Size(34, 3), BackColor = DesktopTheme.Olive, Margin = new Padding(0, 0, 0, 10) });
        var heading = DesktopTheme.Label("2. Registro de pago y comprobación de resultado", DesktopTheme.Strong, DesktopTheme.Ink);
        heading.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(heading);

        var inputBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        methodCombo.AccessibleName = "Método de pago";
        referenceInput.AccessibleName = "Referencia del pago";

        inputBar.Controls.Add(LabeledField("Método de pago", methodCombo));
        inputBar.Controls.Add(LabeledField("Referencia (comprobante/folio opcional)", referenceInput));
        confirmPaymentButton.Margin = new Padding(4, 22, 10, 0);
        reconcileButton.Margin = new Padding(0, 22, 0, 0);
        reconcileButton.Visible = false;
        inputBar.Controls.Add(confirmPaymentButton);
        inputBar.Controls.Add(reconcileButton);
        card.Controls.Add(inputBar);

        securityHintLabel.Margin = new Padding(0, 4, 0, 12);
        paymentResultLabel.MaximumSize = new Size(880, 0);
        paymentResultLabel.Margin = Padding.Empty;

        card.Controls.Add(securityHintLabel);
        card.Controls.Add(paymentResultLabel);
        return card;
    }

    private static Control LabeledField(string caption, Control input) =>
        DesktopTheme.LabeledField(caption, input);

    private void PopulateMethods()
    {
        methodCombo.Items.Clear();
        foreach (var method in Enum.GetValues<PaymentMethod>())
            methodCombo.Items.Add(new MethodComboItem(method, PaymentApiService.FormatMethod(method)));
        methodCombo.SelectedIndex = 0;
    }

    private void BindCandidateCombo(long? preferredOrderId)
    {
        suppressComboSelection = true;
        try
        {
            candidateOrdersCombo.Items.Clear();
            int selectedIndex = -1;
            for (var i = 0; i < candidateOrders.Count; i++)
            {
                var o = candidateOrders[i];
                var tableText = o.TableNumber.HasValue
                    ? $"Mesa #{o.TableNumber.Value:00}"
                    : o.TableId.HasValue ? $"Mesa #{o.TableId.Value}" : "Para llevar";
                var accountTag = o.AccountRequested ? " · Cuenta solicitada" : "";
                var label = $"Orden #{o.Id} · {tableText} · {OrderApiService.FormatStatus(o.Status)} · {o.Total:C2}{accountTag}";
                candidateOrdersCombo.Items.Add(new OrderComboItem(o, label));
                if (preferredOrderId.HasValue && o.Id == preferredOrderId.Value)
                    selectedIndex = i;
            }

            if (selectedIndex >= 0) candidateOrdersCombo.SelectedIndex = selectedIndex;
            else if (candidateOrdersCombo.Items.Count > 0) candidateOrdersCombo.SelectedIndex = 0;
        }
        finally
        {
            suppressComboSelection = false;
        }
    }

    private void UpdateViewState()
    {
        var busy = pendingOperation is not null;
        var hasUncertain = uncertainPaymentOrderId.HasValue;
        reconcileButton.Visible = hasUncertain;
        reconcileButton.Enabled = !busy && hasUncertain;

        detailsGrid.Rows.Clear();
        if (currentOrder is not { } order)
        {
            orderTitleLabel.Text = "Selecciona o busca una orden para cobrar";
            orderMetaLabel.Text = "Sin orden cargada.";
            serverTotalLabel.Text = "Total oficial del servidor: $0.00";
            operationalNoticeLabel.Text = "Selecciona una orden de la lista o ingresa su ID para consultar el total del servidor y registrar el pago.";
            paymentResultLabel.Text = "Resultado del pago: —";
            paymentResultLabel.ForeColor = DesktopTheme.Muted;
            confirmPaymentButton.Enabled = false;
            return;
        }

        var tableText = order.TableNumber.HasValue
            ? $"Mesa #{order.TableNumber.Value:00}"
            : order.TableId.HasValue ? $"Mesa #{order.TableId.Value}" : "Para llevar";
        orderTitleLabel.Text = $"Orden #{order.Id} · {tableText}";
        orderMetaLabel.Text =
            $"Estado: {OrderApiService.FormatStatus(order.Status)}   |   " +
            $"Cuenta: {(order.AccountRequested ? "Solicitada" : "No solicitada")}   |   " +
            $"Fecha: {order.OrderDateTime.ToLocalTime():g}   |   " +
            $"Versión: {order.Version}";
        serverTotalLabel.Text = $"Total oficial del servidor: {order.Total:C2}";

        foreach (var d in order.Details)
        {
            detailsGrid.Rows.Add(
                d.ProductName,
                d.Quantity.ToString(CultureInfo.InvariantCulture),
                d.UnitPrice.ToString("C2", CultureInfo.CurrentCulture),
                d.Subtotal.ToString("C2", CultureInfo.CurrentCulture),
                d.Note ?? "");
        }

        if (currentPayment is { } payment)
        {
            var refText = string.IsNullOrWhiteSpace(payment.Reference) ? "Sin referencia" : payment.Reference;
            paymentResultLabel.Text =
                $"Pago confirmado: Pago #{payment.Id} · Método: {PaymentApiService.FormatMethod(payment.Method)} · " +
                $"Estado: {PaymentApiService.FormatStatus(payment.Status)} · Monto: {payment.Amount:C2} · " +
                $"Referencia: {refText} · Fecha: {payment.PaymentDate.ToLocalTime():g}";
            paymentResultLabel.ForeColor = DesktopTheme.Ink;
            operationalNoticeLabel.Text = "Esta orden ya cuenta con un pago registrado y se encuentra cerrada.";
        }
        else if (uncertainPaymentOrderId == order.Id)
        {
            paymentResultLabel.Text =
                $"Resultado incierto: no se recibió confirmación al cobrar la Orden #{order.Id}. Pulsa «Reconciliar pago con el servidor» antes de intentar otro cobro.";
            paymentResultLabel.ForeColor = DesktopTheme.Error;
            operationalNoticeLabel.Text = "Reintento directo bloqueado por seguridad hasta reconciliar con GET /payments/order.";
        }
        else
        {
            paymentResultLabel.Text = "Resultado del pago: Sin pago registrado todavía para esta orden.";
            paymentResultLabel.ForeColor = DesktopTheme.Muted;

            operationalNoticeLabel.Text = PaymentApiService.GetPaymentBlockReason(order)
                ?? "Orden lista para cobrar. Verifica el método de pago y confirma.";
        }

        var isThisOrderUncertain = uncertainPaymentOrderId == order.Id;
        var canPay = PaymentApiService.CanProcessPayment(order, currentPayment) && !isThisOrderUncertain;
        confirmPaymentButton.Enabled = !busy && canPay;
        methodCombo.Enabled = referenceInput.Enabled = !busy && canPay;
    }

    private void SetBusy(bool busy)
    {
        candidateOrdersCombo.Enabled = orderIdInput.Enabled = loadByIdButton.Enabled =
            refreshCandidatesButton.Enabled = detailsGrid.Enabled = !busy;
        progress.Visible = busy;
        UseWaitCursor = busy;
        UpdateViewState();
    }

    private void ShowFeedback(string message, bool isError) =>
        DesktopTheme.ShowFeedback(feedback, message, isError);

    private static TableLayoutPanel Stack() => DesktopTheme.Stack();

    private sealed record OrderComboItem(OrderDto Order, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record MethodComboItem(PaymentMethod Method, string Label)
    {
        public override string ToString() => Label;
    }
}
