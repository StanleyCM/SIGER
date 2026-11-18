using System.Globalization;
using System.Net;
using System.Windows.Forms.Automation;
using SIGER.Application.DTOs.Reservations;
using SIGER.Application.DTOs.Tables;
using SIGER.Desktop.Controls;
using SIGER.Desktop.Services;
using SIGER.Domain.Enums;

namespace SIGER.Desktop.Forms.Admin;

internal sealed class ReservationsForm : UserControl
{
    private const int DefaultPageSize = 20;

    private readonly ReservationApiService reservations;
    private readonly TableApiService tables;
    private readonly long currentActorUserId;

    // Filter & lookup controls
    private readonly ComboBox tableFilterCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly ComboBox statusFilterCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175 };
    private readonly NumericUpDown reservationIdLookup = new() { Minimum = 0, Maximum = 9999999, Value = 0, Width = 95 };
    private readonly Button searchButton = DesktopTheme.Button("&Buscar / Actualizar", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);
    private readonly Button clearFiltersButton = DesktopTheme.Button("Limpiar filtros", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button newModeButton = DesktopTheme.Button("&Nueva reservación interna", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button prevPageButton = DesktopTheme.Button("◀ Anterior", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button nextPageButton = DesktopTheme.Button("Siguiente ▶", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Label pageStatusLabel = DesktopTheme.Label("Página 1 de 1", DesktopTheme.Small, DesktopTheme.Muted);

    private readonly Label feedback = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Error);
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Height = 5, Dock = DockStyle.Fill };

    private readonly DataGridView grid = new();

    // Editor controls
    private readonly Label editorTitleLabel = DesktopTheme.Label("Crear nueva reservación interna", DesktopTheme.Brand, DesktopTheme.Ink);
    private readonly Label contactSummaryLabel = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Muted);
    private readonly NumericUpDown userIdInput = new() { Minimum = 1, Maximum = 9999999, Value = 1, Width = 100 };
    private readonly ComboBox tableCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly DateTimePicker dateTimePicker = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy HH:mm", ShowUpDown = false, Width = 165 };
    private readonly NumericUpDown peopleInput = new() { Minimum = 1, Maximum = 50, Value = 2, Width = 80 };
    private readonly TextBox notesInput = new() { MaxLength = 300, Width = 260 };
    private readonly Button saveButton = DesktopTheme.Button("&Guardar reservación", DesktopTheme.Olive, DesktopTheme.Ink, DesktopTheme.SoftOlive);

    private readonly ComboBox statusActionCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175 };
    private readonly Button changeStatusButton = DesktopTheme.Button("Cambiar &estado", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);

    private IReadOnlyList<TableDto> loadedTables = [];
    private IReadOnlyList<ReservationDto> currentReservations = [];
    private ReservationDto? selectedReservation;
    private bool createMode = true;
    private string? uncertainCreateKey;
    private int currentPage = 1;
    private int totalPages = 1;
    private int totalCount;
    private bool suppressSelectionChanged;
    private CancellationTokenSource? pendingOperation;

    internal ReservationsForm(ReservationApiService reservations, TableApiService tables, long currentActorUserId)
    {
        this.reservations = reservations;
        this.tables = tables;
        this.currentActorUserId = Math.Max(1L, currentActorUserId);

        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = DesktopTheme.Cream;
        Font = DesktopTheme.Body;
        Margin = Padding.Empty;

        statusFilterCombo.Items.Add(new StatusFilterItem(null, "Todos los estados"));
        foreach (var status in Enum.GetValues<ReservationStatus>())
        {
            statusFilterCombo.Items.Add(new StatusFilterItem(status, ReservationApiService.FormatStatus(status)));
            statusActionCombo.Items.Add(new StatusItem(status, ReservationApiService.FormatStatus(status)));
        }
        statusFilterCombo.SelectedIndex = 0;
        statusActionCombo.SelectedIndex = 0;

        dateTimePicker.Value = DateTime.Now.Date.AddDays(1).AddHours(13);

        var root = Stack();
        root.Dock = DockStyle.Top;
        root.Controls.Add(BuildFilterBar());
        root.Controls.Add(DesktopTheme.FeedbackBar(feedback, progress, "Operación de reservaciones en curso"));
        root.Controls.Add(BuildGridCard());
        root.Controls.Add(BuildEditorCard());
        Controls.Add(root);

        searchButton.Click += async (_, _) =>
        {
            uncertainCreateKey = null;
            await LoadReservationsAsync(1, selectedReservation?.Id, reloadTables: true);
        };
        clearFiltersButton.Click += async (_, _) => await ClearFiltersAsync();
        newModeButton.Click += (_, _) => EnterCreateMode();
        prevPageButton.Click += async (_, _) =>
        {
            if (currentPage > 1) await LoadReservationsAsync(currentPage - 1, null);
        };
        nextPageButton.Click += async (_, _) =>
        {
            if (currentPage < totalPages) await LoadReservationsAsync(currentPage + 1, null);
        };
        saveButton.Click += async (_, _) => await SaveReservationAsync();
        changeStatusButton.Click += async (_, _) => await ChangeSelectedStatusAsync();

        grid.SelectionChanged += (_, _) =>
        {
            if (suppressSelectionChanged) return;
            if (grid.CurrentRow?.Tag is ReservationDto res)
                LoadReservationIntoEditor(res);
        };

        EnterCreateMode();
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

    internal async Task EnsureLoadedAsync() => await LoadReservationsAsync(currentPage, selectedReservation?.Id);

    private async Task ClearFiltersAsync()
    {
        if (tableFilterCombo.Items.Count > 0) tableFilterCombo.SelectedIndex = 0;
        if (statusFilterCombo.Items.Count > 0) statusFilterCombo.SelectedIndex = 0;
        reservationIdLookup.Value = 0;
        uncertainCreateKey = null;
        await LoadReservationsAsync(1, null);
    }

    private async Task LoadReservationsAsync(int pageNumber, long? preferredId, bool reloadTables = false)
    {
        if (pendingOperation is not null) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            if (loadedTables.Count == 0 || reloadTables)
            {
                var tablesPage = await tables.GetPagedAsync(1, 200, cts.Token);
                loadedTables = tablesPage.Items.OrderBy(t => t.Number).ToArray();
                BindTableCombos(
                    (tableFilterCombo.SelectedItem as TableFilterItem)?.TableId,
                    selectedReservation?.TableId);
            }

            var lookupId = (long)reservationIdLookup.Value;
            if (lookupId > 0)
            {
                var single = await reservations.GetByIdAsync(lookupId, cts.Token);
                if (cts.IsCancellationRequested || IsDisposed) return;
                currentReservations = [single];
                currentPage = 1;
                totalPages = 1;
                totalCount = 1;
                BindGrid(single.Id);
                return;
            }

            var filterTableId = (tableFilterCombo.SelectedItem as TableFilterItem)?.TableId;
            var filterStatus = (statusFilterCombo.SelectedItem as StatusFilterItem)?.Status;
            var page = await reservations.GetPagedAsync(pageNumber, DefaultPageSize, null, filterTableId, filterStatus, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentReservations = page.Items.ToArray();
            currentPage = page.PageNumber;
            totalPages = Math.Max(1, page.TotalPages);
            totalCount = page.TotalCount;
            BindGrid(preferredId);
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

    private async Task SaveReservationAsync()
    {
        if (pendingOperation is not null) return;
        if (tableCombo.SelectedItem is not TableComboItem { Table: var table })
        {
            ShowFeedback("Selecciona una mesa válida para la reservación.", isError: true);
            return;
        }

        var localDateTime = DateTime.SpecifyKind(dateTimePicker.Value, DateTimeKind.Local);
        var reservationTime = new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime));
        var people = (int)peopleInput.Value;
        var candidateKey = $"{userIdInput.Value}:{table.Id}:{reservationTime.UtcTicks}";
        if (createMode && string.Equals(uncertainCreateKey, candidateKey, StringComparison.Ordinal))
        {
            ShowFeedback("El alta anterior de esta reservación quedó incierta por red. Pulsa «Buscar / Actualizar» antes de reintentar.", isError: true);
            return;
        }

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            ReservationDto saved;
            if (createMode)
            {
                saved = await reservations.CreateAsync(new CreateReservationRequestDto
                {
                    UserId = (long)userIdInput.Value,
                    TableId = table.Id,
                    ReservationDateTime = reservationTime,
                    NumberOfPeople = people,
                    Notes = notesInput.Text
                }, cts.Token);
                uncertainCreateKey = null;
            }
            else if (selectedReservation is { } current)
            {
                // Preserve whether the reservation belongs to a guest (UserId is null) or a registered user.
                long? targetUserId = current.UserId.HasValue ? (long)userIdInput.Value : null;
                saved = await reservations.UpdateAsync(current.Id, new UpdateReservationRequestDto
                {
                    UserId = targetUserId,
                    TableId = table.Id,
                    ReservationDateTime = reservationTime,
                    NumberOfPeople = people,
                    Notes = notesInput.Text
                }, cts.Token);
            }
            else return;

            if (cts.IsCancellationRequested || IsDisposed) return;

            var page = await reservations.GetPagedAsync(currentPage, DefaultPageSize, null,
                (tableFilterCombo.SelectedItem as TableFilterItem)?.TableId,
                (statusFilterCombo.SelectedItem as StatusFilterItem)?.Status, cts.Token);
            currentReservations = page.Items.ToArray();
            totalPages = Math.Max(1, page.TotalPages);
            totalCount = page.TotalCount;
            BindGrid(saved.Id);
            LoadReservationIntoEditor(saved);
            ShowFeedback($"Reservación #{saved.Id} guardada en Mesa #{saved.TableNumber ?? table.Number:00}.", isError: false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (ApiException error)
        {
            if (cts.IsCancellationRequested || IsDisposed) return;
            if (createMode && error.StatusCode is null)
            {
                uncertainCreateKey = candidateKey;
                ShowFeedback($"{error.Message} No se confirmó si la reservación fue creada. Pulsa «Buscar / Actualizar» antes de reintentar.", isError: true);
                return;
            }
            if (!createMode && selectedReservation is { } current && error.StatusCode == HttpStatusCode.Conflict)
            {
                try
                {
                    var reloaded = await reservations.GetByIdAsync(current.Id, cts.Token);
                    LoadReservationIntoEditor(reloaded);
                    ShowFeedback($"{error.Message} Se recargó la Reservación #{reloaded.Id}.", isError: true);
                    return;
                }
                catch (ApiException) { }
            }
            ShowFeedback(error.Message, isError: true);
        }
        finally
        {
            pendingOperation = null;
            if (!IsDisposed) SetBusy(false);
        }
    }

    private async Task ChangeSelectedStatusAsync()
    {
        if (pendingOperation is not null || createMode || selectedReservation is not { } current) return;
        if (statusActionCombo.SelectedItem is not StatusItem { Status: var targetStatus }) return;

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            await reservations.ChangeStatusAsync(current.Id, targetStatus, cts.Token);
            var refreshed = await reservations.GetByIdAsync(current.Id, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentReservations = currentReservations.Select(r => r.Id == refreshed.Id ? refreshed : r).ToArray();
            BindGrid(refreshed.Id);
            LoadReservationIntoEditor(refreshed);
            ShowFeedback($"Reservación #{refreshed.Id} actualizada a estado «{ReservationApiService.FormatStatus(refreshed.Status)}».", isError: false);
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

    private void EnterCreateMode()
    {
        createMode = true;
        selectedReservation = null;
        editorTitleLabel.Text = "Crear nueva reservación interna";
        contactSummaryLabel.Text = "Reservación interna asociada a un ID de usuario activo del sistema.";
        userIdInput.Value = Math.Clamp(currentActorUserId, (long)userIdInput.Minimum, (long)userIdInput.Maximum);
        userIdInput.Enabled = pendingOperation is null;
        dateTimePicker.Value = DateTime.Now.Date.AddDays(1).AddHours(13);
        peopleInput.Value = 2;
        notesInput.Clear();
        changeStatusButton.Enabled = statusActionCombo.Enabled = false;
    }

    private void LoadReservationIntoEditor(ReservationDto res)
    {
        createMode = false;
        selectedReservation = res;
        editorTitleLabel.Text = $"Editar Reservación #{res.Id} · Estado: {ReservationApiService.FormatStatus(res.Status)}";

        if (res.UserId.HasValue)
        {
            contactSummaryLabel.Text = $"Tipo: Usuario registrado (ID #{res.UserId.Value})";
            userIdInput.Value = Math.Clamp(res.UserId.Value, (long)userIdInput.Minimum, (long)userIdInput.Maximum);
            userIdInput.Enabled = pendingOperation is null;
        }
        else
        {
            var emailText = string.IsNullOrWhiteSpace(res.ContactEmail) ? "sin correo" : res.ContactEmail;
            contactSummaryLabel.Text = $"Tipo: Invitado Web — {res.ContactName ?? "Sin nombre"} · Tel: {res.ContactPhone ?? "—"} · Correo: {emailText}";
            userIdInput.Enabled = false;
        }

        BindTableCombos((tableFilterCombo.SelectedItem as TableFilterItem)?.TableId, res.TableId);
        dateTimePicker.Value = res.ReservationDateTime.ToLocalTime().DateTime;
        peopleInput.Value = Math.Clamp(res.NumberOfPeople, (int)peopleInput.Minimum, (int)peopleInput.Maximum);
        notesInput.Text = res.Notes ?? "";

        for (var i = 0; i < statusActionCombo.Items.Count; i++)
        {
            if (statusActionCombo.Items[i] is StatusItem item && item.Status == res.Status)
            {
                statusActionCombo.SelectedIndex = i;
                break;
            }
        }
        changeStatusButton.Enabled = statusActionCombo.Enabled = pendingOperation is null;
    }

    private void BindTableCombos(long? filterTableId, long? editorTableId)
    {
        tableFilterCombo.Items.Clear();
        tableFilterCombo.Items.Add(new TableFilterItem(null, "Todas las mesas"));
        int filterIdx = 0;

        tableCombo.Items.Clear();
        int editorIdx = -1;

        for (var i = 0; i < loadedTables.Count; i++)
        {
            var t = loadedTables[i];
            tableFilterCombo.Items.Add(new TableFilterItem(t.Id, $"Mesa #{t.Number:00} ({t.Capacity}p)"));
            if (filterTableId.HasValue && t.Id == filterTableId.Value) filterIdx = i + 1;

            tableCombo.Items.Add(new TableComboItem(t, $"Mesa #{t.Number:00} · Cap. {t.Capacity} ({TableApiService.FormatStatus(t.Status)})"));
            if (editorTableId.HasValue && t.Id == editorTableId.Value) editorIdx = i;
        }

        tableFilterCombo.SelectedIndex = filterIdx;
        if (editorIdx >= 0) tableCombo.SelectedIndex = editorIdx;
        else if (tableCombo.Items.Count > 0) tableCombo.SelectedIndex = 0;
    }

    private void BindGrid(long? preferredId)
    {
        suppressSelectionChanged = true;
        try
        {
            grid.Rows.Clear();
            int selectedIndex = -1;
            for (var i = 0; i < currentReservations.Count; i++)
            {
                var r = currentReservations[i];
                var tableText = r.TableNumber.HasValue ? $"Mesa #{r.TableNumber.Value:00}" : $"Mesa #{r.TableId}";
                var who = !string.IsNullOrWhiteSpace(r.ContactName)
                    ? $"{r.ContactName} ({r.ContactPhone ?? "—"})"
                    : r.UserId.HasValue ? $"Usuario #{r.UserId.Value}" : "—";

                var rowIndex = grid.Rows.Add(
                    $"#{r.Id}",
                    r.ReservationDateTime.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    tableText,
                    r.NumberOfPeople.ToString(CultureInfo.InvariantCulture),
                    ReservationApiService.FormatStatus(r.Status),
                    who,
                    string.IsNullOrWhiteSpace(r.Notes) ? "—" : r.Notes);
                grid.Rows[rowIndex].Tag = r;
                if (preferredId.HasValue && r.Id == preferredId.Value)
                    selectedIndex = rowIndex;
            }

            if (selectedIndex >= 0)
            {
                grid.ClearSelection();
                grid.Rows[selectedIndex].Selected = true;
                grid.CurrentCell = grid.Rows[selectedIndex].Cells[0];
            }
        }
        finally
        {
            suppressSelectionChanged = false;
        }

        pageStatusLabel.Text = $"Página {currentPage} de {totalPages} · {totalCount} reservaciones";
        prevPageButton.Enabled = pendingOperation is null && currentPage > 1;
        nextPageButton.Enabled = pendingOperation is null && currentPage < totalPages;
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
        tableFilterCombo.AccessibleName = "Filtrar reservaciones por mesa";
        statusFilterCombo.AccessibleName = "Filtrar reservaciones por estado";
        reservationIdLookup.AccessibleName = "Buscar reservación por ID";

        bar.Controls.Add(LabeledField("Mesa", tableFilterCombo));
        bar.Controls.Add(LabeledField("Estado", statusFilterCombo));
        bar.Controls.Add(LabeledField("ID Reserva (0=todas)", reservationIdLookup));
        searchButton.Margin = new Padding(4, 22, 8, 0);
        clearFiltersButton.Margin = new Padding(0, 22, 8, 0);
        newModeButton.Margin = new Padding(0, 22, 0, 0);
        bar.Controls.Add(searchButton);
        bar.Controls.Add(clearFiltersButton);
        bar.Controls.Add(newModeButton);
        return bar;
    }

    private Control BuildGridCard()
    {
        var card = Stack();
        card.Dock = DockStyle.Top;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(20);
        card.Margin = new Padding(0, 0, 0, 16);

        var heading = DesktopTheme.Label("1. Reservaciones registradas", DesktopTheme.Strong, DesktopTheme.Ink);
        heading.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(heading);

        DesktopTheme.ConfigureGrid(grid, "Listado de reservaciones", 195);

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", FillWeight = 10 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DateTime", HeaderText = "Fecha y hora", FillWeight = 20 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Table", HeaderText = "Mesa", FillWeight = 14 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "People", HeaderText = "Personas", FillWeight = 11 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Estado", FillWeight = 14 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Guest", HeaderText = "Cliente / Invitado", FillWeight = 24 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Notes", HeaderText = "Observaciones", FillWeight = 22 });

        card.Controls.Add(grid);

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

    private Control BuildEditorCard()
    {
        var card = Stack();
        card.Dock = DockStyle.Top;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(20);
        card.Margin = Padding.Empty;

        card.Controls.Add(new Panel { Size = new Size(34, 3), BackColor = DesktopTheme.Olive, Margin = new Padding(0, 0, 0, 10) });
        editorTitleLabel.Margin = new Padding(0, 0, 0, 4);
        contactSummaryLabel.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(editorTitleLabel);
        card.Controls.Add(contactSummaryLabel);

        var fields = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 10)
        };
        userIdInput.AccessibleName = "ID de usuario registrado";
        tableCombo.AccessibleName = "Mesa de la reservación";
        dateTimePicker.AccessibleName = "Fecha y hora de la reservación";
        peopleInput.AccessibleName = "Cantidad de personas";
        notesInput.AccessibleName = "Observaciones de la reservación";

        fields.Controls.Add(LabeledField("ID Usuario (interna)", userIdInput));
        fields.Controls.Add(LabeledField("Mesa", tableCombo));
        fields.Controls.Add(LabeledField("Fecha y hora futura", dateTimePicker));
        fields.Controls.Add(LabeledField("Personas", peopleInput));
        fields.Controls.Add(LabeledField("Observaciones", notesInput));
        card.Controls.Add(fields);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = Padding.Empty
        };
        saveButton.Margin = new Padding(0, 22, 18, 0);
        statusActionCombo.AccessibleName = "Nuevo estado para la reservación seleccionada";
        changeStatusButton.Margin = new Padding(0, 22, 0, 0);

        actions.Controls.Add(saveButton);
        actions.Controls.Add(LabeledField("Estado de reservación", statusActionCombo));
        actions.Controls.Add(changeStatusButton);
        card.Controls.Add(actions);

        return card;
    }

    private static Control LabeledField(string caption, Control input) =>
        DesktopTheme.LabeledField(caption, input);

    private void SetBusy(bool busy)
    {
        tableFilterCombo.Enabled = statusFilterCombo.Enabled = reservationIdLookup.Enabled =
            searchButton.Enabled = clearFiltersButton.Enabled = newModeButton.Enabled = grid.Enabled =
            tableCombo.Enabled = dateTimePicker.Enabled = peopleInput.Enabled =
            notesInput.Enabled = saveButton.Enabled = !busy;
        userIdInput.Enabled = !busy && (createMode || selectedReservation?.UserId.HasValue == true);
        statusActionCombo.Enabled = changeStatusButton.Enabled = !busy && !createMode && selectedReservation is not null;
        prevPageButton.Enabled = !busy && currentPage > 1;
        nextPageButton.Enabled = !busy && currentPage < totalPages;
        progress.Visible = busy;
        UseWaitCursor = busy;
    }

    private void ShowFeedback(string message, bool isError) =>
        DesktopTheme.ShowFeedback(feedback, message, isError);

    private static TableLayoutPanel Stack() => DesktopTheme.Stack();

    private sealed record TableFilterItem(long? TableId, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record TableComboItem(TableDto Table, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record StatusFilterItem(ReservationStatus? Status, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record StatusItem(ReservationStatus Status, string Label)
    {
        public override string ToString() => Label;
    }
}
