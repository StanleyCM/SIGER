using System.Globalization;
using System.Net;
using System.Windows.Forms.Automation;
using SIGER.Application.DTOs.Tables;
using SIGER.Desktop.Controls;
using SIGER.Desktop.Services;
using SIGER.Domain.Enums;

namespace SIGER.Desktop.Forms.Admin;

internal sealed class TablesForm : UserControl
{
    private readonly TableApiService tables;

    private readonly Button refreshButton = DesktopTheme.Button("&Actualizar mesas", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);
    private readonly Button newModeButton = DesktopTheme.Button("&Nueva mesa", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Label feedback = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Error);
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Height = 5, Dock = DockStyle.Fill };

    private readonly DataGridView grid = new();

    private readonly Label editorTitleLabel = DesktopTheme.Label("Crear nueva mesa", DesktopTheme.Brand, DesktopTheme.Ink);
    private readonly NumericUpDown numberInput = new() { Minimum = 1, Maximum = 9999, Value = 1, Width = 100 };
    private readonly NumericUpDown capacityInput = new() { Minimum = 1, Maximum = 100, Value = 4, Width = 100 };
    private readonly TextBox locationInput = new() { MaxLength = 100, Width = 240 };
    private readonly ComboBox statusCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly Button saveButton = DesktopTheme.Button("&Guardar datos de mesa", DesktopTheme.Olive, DesktopTheme.Ink, DesktopTheme.SoftOlive);
    private readonly Button changeStatusButton = DesktopTheme.Button("Cambiar &estado operativo", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);
    private readonly Label protectionNoteLabel = DesktopTheme.Label(
        "Nota: una mesa ocupada con órdenes activas no puede liberarse manualmente hasta que sus órdenes se paguen o cancelen.",
        DesktopTheme.Small,
        DesktopTheme.Muted);

    private IReadOnlyList<TableDto> currentTables = [];
    private TableDto? selectedTable;
    private bool createMode = true;
    private int? uncertainCreateNumber;
    private bool suppressSelectionChanged;
    private CancellationTokenSource? pendingOperation;

    internal TablesForm(TableApiService tables)
    {
        this.tables = tables;
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = DesktopTheme.Cream;
        Font = DesktopTheme.Body;
        Margin = Padding.Empty;

        foreach (var status in Enum.GetValues<TableStatus>())
            statusCombo.Items.Add(new StatusItem(status, TableApiService.FormatStatus(status)));
        statusCombo.SelectedIndex = 0;

        var root = Stack();
        root.Dock = DockStyle.Top;
        root.Controls.Add(BuildToolbar());
        root.Controls.Add(DesktopTheme.FeedbackBar(feedback, progress, "Operación de administración de mesas en curso"));
        root.Controls.Add(BuildGridCard());
        root.Controls.Add(BuildEditorCard());
        Controls.Add(root);

        refreshButton.Click += async (_, _) =>
        {
            uncertainCreateNumber = null;
            await RefreshTablesAsync(selectedTable?.Id);
        };
        newModeButton.Click += (_, _) => EnterCreateMode();
        saveButton.Click += async (_, _) => await SaveTableAsync();
        changeStatusButton.Click += async (_, _) => await ChangeSelectedTableStatusAsync();

        grid.SelectionChanged += (_, _) =>
        {
            if (suppressSelectionChanged) return;
            if (grid.CurrentRow?.Tag is TableDto table)
                LoadTableIntoEditor(table);
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

    internal async Task RefreshTablesAsync(long? preferredId = null)
    {
        if (pendingOperation is not null) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            var page = await tables.GetPagedAsync(1, 200, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentTables = page.Items.OrderBy(t => t.Number).ToArray();
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

    private async Task SaveTableAsync()
    {
        if (pendingOperation is not null) return;
        var number = (int)numberInput.Value;
        var capacity = (int)capacityInput.Value;

        if (createMode && uncertainCreateNumber == number)
        {
            ShowFeedback("El alta anterior de esta mesa quedó incierta por red. Pulsa «Actualizar mesas» antes de volver a enviar.", isError: true);
            return;
        }

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            var selectedStatus = (statusCombo.SelectedItem as StatusItem)?.Status ?? TableStatus.Available;
            TableDto saved = createMode
                ? await tables.CreateAsync(new CreateTableRequestDto
                {
                    Number = number,
                    Capacity = capacity,
                    Status = selectedStatus,
                    Location = locationInput.Text
                }, cts.Token)
                : await tables.UpdateAsync(selectedTable!.Id, new UpdateTableRequestDto
                {
                    Number = number,
                    Capacity = capacity,
                    Location = locationInput.Text,
                    Version = selectedTable.Version
                }, cts.Token);

            uncertainCreateNumber = null;
            var page = await tables.GetPagedAsync(1, 200, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentTables = page.Items.OrderBy(t => t.Number).ToArray();
            BindGrid(saved.Id);
            LoadTableIntoEditor(saved);
            ShowFeedback($"Mesa #{saved.Number:00} guardada (Capacidad {saved.Capacity}, Versión {saved.Version}).", isError: false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (ApiException error)
        {
            if (cts.IsCancellationRequested || IsDisposed) return;
            if (createMode && error.StatusCode is null)
            {
                uncertainCreateNumber = number;
                ShowFeedback($"{error.Message} No se confirmó si la mesa fue creada. Pulsa «Actualizar mesas» antes de reintentar.", isError: true);
                return;
            }
            if (!createMode && selectedTable is { } current && error.StatusCode == HttpStatusCode.Conflict)
            {
                try
                {
                    var reloaded = await tables.GetByIdAsync(current.Id, cts.Token);
                    LoadTableIntoEditor(reloaded);
                    ShowFeedback($"{error.Message} Se recargó la Mesa #{reloaded.Number:00} (Versión {reloaded.Version}).", isError: true);
                    return;
                }
                catch (ApiException) { }
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

    private async Task ChangeSelectedTableStatusAsync()
    {
        if (pendingOperation is not null || createMode || selectedTable is not { } current) return;
        if (statusCombo.SelectedItem is not StatusItem { Status: var targetStatus }) return;

        if (current.Status == TableStatus.Occupied && targetStatus != TableStatus.Occupied)
        {
            var confirm = MessageBox.Show(
                this,
                $"La Mesa #{current.Number:00} figura como Ocupada.\n\nEl servidor rechazará el cambio si la mesa mantiene órdenes activas sin pagar o cancelar.\n¿Deseas solicitar el cambio a «{TableApiService.FormatStatus(targetStatus)}»?",
                "Confirmar cambio de estado de mesa",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;
        }

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            await tables.ChangeStatusAsync(current.Id, targetStatus, current.Version, cts.Token);
            var refreshed = await tables.GetByIdAsync(current.Id, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentTables = currentTables.Select(t => t.Id == refreshed.Id ? refreshed : t).ToArray();
            BindGrid(refreshed.Id);
            LoadTableIntoEditor(refreshed);
            ShowFeedback($"Mesa #{refreshed.Number:00} actualizada a «{TableApiService.FormatStatus(refreshed.Status)}» (Versión {refreshed.Version}).", isError: false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (ApiException error)
        {
            if (cts.IsCancellationRequested || IsDisposed) return;
            if (error.StatusCode == HttpStatusCode.Conflict)
            {
                try
                {
                    var reloaded = await tables.GetByIdAsync(current.Id, cts.Token);
                    currentTables = currentTables.Select(t => t.Id == reloaded.Id ? reloaded : t).ToArray();
                    BindGrid(reloaded.Id);
                    LoadTableIntoEditor(reloaded);
                    ShowFeedback(
                        $"No se pudo cambiar el estado de la Mesa #{reloaded.Number:00} (puede tener órdenes activas o su versión cambió). Estado actual: {TableApiService.FormatStatus(reloaded.Status)}.",
                        isError: true);
                    return;
                }
                catch (ApiException) { }
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

    private void EnterCreateMode()
    {
        createMode = true;
        selectedTable = null;
        editorTitleLabel.Text = "Crear nueva mesa";
        numberInput.Value = currentTables.Count > 0 ? Math.Min(9999, currentTables.Max(t => t.Number) + 1) : 1;
        capacityInput.Value = 4;
        locationInput.Clear();
        statusCombo.SelectedIndex = 0;
        changeStatusButton.Enabled = false;
    }

    private void LoadTableIntoEditor(TableDto table)
    {
        createMode = false;
        selectedTable = table;
        editorTitleLabel.Text = $"Editar Mesa #{table.Number:00} (ID #{table.Id}) · Versión {table.Version}";
        numberInput.Value = Math.Clamp(table.Number, (int)numberInput.Minimum, (int)numberInput.Maximum);
        capacityInput.Value = Math.Clamp(table.Capacity, (int)capacityInput.Minimum, (int)capacityInput.Maximum);
        locationInput.Text = table.Location ?? "";
        for (var i = 0; i < statusCombo.Items.Count; i++)
        {
            if (statusCombo.Items[i] is StatusItem item && item.Status == table.Status)
            {
                statusCombo.SelectedIndex = i;
                break;
            }
        }
        changeStatusButton.Enabled = pendingOperation is null;
    }

    private void BindGrid(long? preferredId)
    {
        suppressSelectionChanged = true;
        try
        {
            grid.Rows.Clear();
            int selectedIndex = -1;
            for (var i = 0; i < currentTables.Count; i++)
            {
                var t = currentTables[i];
                var rowIndex = grid.Rows.Add(
                    $"#{t.Id}",
                    $"Mesa #{t.Number:00}",
                    $"{t.Capacity} personas",
                    string.IsNullOrWhiteSpace(t.Location) ? "Salón general" : t.Location,
                    TableApiService.FormatStatus(t.Status),
                    t.Version.ToString(CultureInfo.InvariantCulture));
                grid.Rows[rowIndex].Tag = t;
                if (preferredId.HasValue && t.Id == preferredId.Value)
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
        refreshButton.Margin = new Padding(0, 0, 10, 0);
        bar.Controls.Add(refreshButton);
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

        var heading = DesktopTheme.Label("1. Mesas configuradas", DesktopTheme.Strong, DesktopTheme.Ink);
        heading.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(heading);

        DesktopTheme.ConfigureGrid(grid, "Listado de mesas", 205);

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", FillWeight = 10 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Number", HeaderText = "Número", FillWeight = 18 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Capacity", HeaderText = "Capacidad", FillWeight = 18 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Location", HeaderText = "Ubicación", FillWeight = 26 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Estado", FillWeight = 18 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Version", HeaderText = "Versión", FillWeight = 10 });

        card.Controls.Add(grid);
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
        editorTitleLabel.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(editorTitleLabel);

        var fields = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        numberInput.AccessibleName = "Número de mesa";
        capacityInput.AccessibleName = "Capacidad de personas";
        locationInput.AccessibleName = "Ubicación de la mesa";
        statusCombo.AccessibleName = "Estado operativo de la mesa";

        fields.Controls.Add(LabeledField("Número de mesa", numberInput));
        fields.Controls.Add(LabeledField("Capacidad", capacityInput));
        fields.Controls.Add(LabeledField("Ubicación (opcional)", locationInput));
        fields.Controls.Add(LabeledField("Estado operativo", statusCombo));
        card.Controls.Add(fields);

        protectionNoteLabel.MaximumSize = new Size(880, 0);
        protectionNoteLabel.Margin = new Padding(0, 0, 0, 12);
        card.Controls.Add(protectionNoteLabel);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = Padding.Empty
        };
        saveButton.Margin = new Padding(0, 0, 10, 0);
        actions.Controls.Add(saveButton);
        actions.Controls.Add(changeStatusButton);
        card.Controls.Add(actions);

        return card;
    }

    private static Control LabeledField(string caption, Control input) =>
        DesktopTheme.LabeledField(caption, input);

    private void SetBusy(bool busy)
    {
        refreshButton.Enabled = newModeButton.Enabled = grid.Enabled =
            numberInput.Enabled = capacityInput.Enabled = locationInput.Enabled =
            statusCombo.Enabled = saveButton.Enabled = !busy;
        changeStatusButton.Enabled = !busy && !createMode && selectedTable is not null;
        progress.Visible = busy;
        UseWaitCursor = busy;
    }

    private void ShowFeedback(string message, bool isError) =>
        DesktopTheme.ShowFeedback(feedback, message, isError);

    private static TableLayoutPanel Stack() => DesktopTheme.Stack();

    private sealed record StatusItem(TableStatus Status, string Label)
    {
        public override string ToString() => Label;
    }
}
