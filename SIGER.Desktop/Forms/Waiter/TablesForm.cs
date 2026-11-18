using System.Windows.Forms.Automation;
using SIGER.Application.DTOs.Tables;
using SIGER.Desktop.Controls;
using SIGER.Desktop.Services;
using SIGER.Domain.Enums;

namespace SIGER.Desktop.Forms.Waiter;

internal sealed class TablesForm : UserControl
{
    private readonly TableApiService tables;
    private readonly CheckBox onlyAvailable = new() { Text = "&Solo disponibles", AutoSize = true };
    private readonly Button refreshButton = DesktopTheme.Button("&Actualizar mesas", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);
    private readonly Button inspectButton = DesktopTheme.Button("&Consultar mesa", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Label summaryLabel = DesktopTheme.Label("Cargando mesas…", DesktopTheme.Small, DesktopTheme.Muted);
    private readonly Label feedback = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Error);
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Height = 5, Dock = DockStyle.Fill };
    private readonly DataGridView grid = new();
    private readonly Label selectedTitle = DesktopTheme.Label("Selecciona una mesa", DesktopTheme.Brand, DesktopTheme.Ink);
    private readonly Label selectedStatus = DesktopTheme.Label("", DesktopTheme.Strong, DesktopTheme.Muted);
    private readonly Label selectedDetails = DesktopTheme.Label("", DesktopTheme.Body, DesktopTheme.Ink);
    private readonly Label selectedHint = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Muted);
    private readonly Button createOrderButton = DesktopTheme.Button("Crear &orden en esta mesa", DesktopTheme.Olive, DesktopTheme.Ink, DesktopTheme.SoftOlive);
    private readonly Button viewOrdersButton = DesktopTheme.Button("&Ver órdenes de esta mesa", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);

    private IReadOnlyList<TableDto> currentTables = [];
    private TableDto? selectedTable;
    private CancellationTokenSource? pendingOperation;
    private bool suppressSelectionChanged;

    internal event EventHandler<TableDto>? CreateOrderRequested;
    internal event EventHandler<TableDto>? ViewTableOrdersRequested;

    internal TablesForm(TableApiService tables)
    {
        this.tables = tables;
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = DesktopTheme.Cream;
        Font = DesktopTheme.Body;
        Margin = Padding.Empty;

        var root = Stack();
        root.Dock = DockStyle.Top;
        root.Controls.Add(BuildToolbar());
        root.Controls.Add(DesktopTheme.FeedbackBar(feedback, progress, "Consulta de mesas en curso", maxWidth: 880));
        root.Controls.Add(BuildMainSplit());
        Controls.Add(root);

        onlyAvailable.CheckedChanged += async (_, _) => await RefreshTablesAsync(selectedTable?.Id);
        refreshButton.Click += async (_, _) => await RefreshTablesAsync(selectedTable?.Id);
        inspectButton.Click += async (_, _) => await InspectSelectedTableAsync();
        createOrderButton.Click += (_, _) =>
        {
            if (selectedTable is { Status: TableStatus.Available } table)
                CreateOrderRequested?.Invoke(this, table);
        };
        viewOrdersButton.Click += (_, _) =>
        {
            if (selectedTable is { } table)
                ViewTableOrdersRequested?.Invoke(this, table);
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

    internal async Task RefreshTablesAsync(long? preferredTableId = null)
    {
        if (pendingOperation is not null) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            IReadOnlyCollection<TableDto> loaded = onlyAvailable.Checked
                ? await tables.GetAvailableAsync(cts.Token)
                : (await tables.GetPagedAsync(1, 200, cts.Token)).Items;

            if (cts.IsCancellationRequested || IsDisposed) return;
            currentTables = loaded.OrderBy(t => t.Number).ToArray();
            BindGrid(preferredTableId ?? selectedTable?.Id);
            var availableCount = currentTables.Count(t => t.Status == TableStatus.Available);
            var occupiedCount = currentTables.Count(t => t.Status == TableStatus.Occupied);
            summaryLabel.Text = onlyAvailable.Checked
                ? $"Mesas disponibles: {availableCount}"
                : $"Total: {currentTables.Count} mesas · {availableCount} disponibles · {occupiedCount} ocupadas";
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

    private async Task InspectSelectedTableAsync()
    {
        if (pendingOperation is not null || selectedTable is not { } current) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            var refreshed = await tables.GetByIdAsync(current.Id, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            var updatedList = currentTables.Select(t => t.Id == refreshed.Id ? refreshed : t).ToList();
            if (onlyAvailable.Checked && refreshed.Status != TableStatus.Available)
                updatedList.RemoveAll(t => t.Id == refreshed.Id);
            else if (updatedList.All(t => t.Id != refreshed.Id))
                updatedList.Add(refreshed);

            currentTables = updatedList.OrderBy(t => t.Number).ToArray();
            selectedTable = refreshed;
            BindGrid(refreshed.Id);
            ShowFeedback($"Mesa #{refreshed.Number:00} actualizada ({TableApiService.FormatStatus(refreshed.Status)}).", isError: false);
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

        onlyAvailable.ForeColor = DesktopTheme.Ink;
        onlyAvailable.Margin = new Padding(0, 10, 18, 6);
        refreshButton.Margin = new Padding(0, 0, 10, 0);
        inspectButton.Margin = new Padding(0, 0, 18, 0);
        summaryLabel.Margin = new Padding(0, 11, 0, 0);

        bar.Controls.Add(onlyAvailable);
        bar.Controls.Add(refreshButton);
        bar.Controls.Add(inspectButton);
        bar.Controls.Add(summaryLabel);
        return bar;
    }

    private Control BuildMainSplit()
    {
        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        split.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        split.Controls.Add(BuildGridCard(), 0, 0);
        split.Controls.Add(BuildDetailCard(), 1, 0);
        return split;
    }

    private Control BuildGridCard()
    {
        var card = Stack();
        card.Dock = DockStyle.Fill;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(20);
        card.Margin = new Padding(0, 0, 16, 0);

        var heading = DesktopTheme.Label("Listado de mesas", DesktopTheme.Strong, DesktopTheme.Ink);
        heading.Margin = new Padding(0, 0, 0, 12);
        card.Controls.Add(heading);

        DesktopTheme.ConfigureGrid(grid, "Mesas del restaurante", 360);

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Number", HeaderText = "Mesa", FillWeight = 20 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Capacity", HeaderText = "Capacidad", FillWeight = 22 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Location", HeaderText = "Ubicación", FillWeight = 33 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Estado", FillWeight = 25 });

        grid.SelectionChanged += (_, _) =>
        {
            if (suppressSelectionChanged) return;
            selectedTable = grid.CurrentRow?.Tag as TableDto;
            UpdateDetailPanel();
        };

        card.Controls.Add(grid);
        return card;
    }

    private Control BuildDetailCard()
    {
        var card = Stack();
        card.Dock = DockStyle.Top;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(24);
        card.Margin = Padding.Empty;

        card.Controls.Add(new Panel { Size = new Size(34, 3), BackColor = DesktopTheme.Olive, Margin = new Padding(0, 0, 0, 12) });
        selectedTitle.Margin = new Padding(0, 0, 0, 6);
        selectedStatus.Margin = new Padding(0, 0, 0, 16);
        selectedDetails.MaximumSize = new Size(320, 0);
        selectedDetails.Margin = new Padding(0, 0, 0, 18);
        selectedHint.MaximumSize = new Size(320, 0);
        selectedHint.Margin = new Padding(0, 0, 0, 20);

        createOrderButton.Dock = DockStyle.Fill;
        createOrderButton.Margin = new Padding(0, 0, 0, 10);
        viewOrdersButton.Dock = DockStyle.Fill;
        viewOrdersButton.Margin = Padding.Empty;

        card.Controls.Add(selectedTitle);
        card.Controls.Add(selectedStatus);
        card.Controls.Add(selectedDetails);
        card.Controls.Add(selectedHint);
        card.Controls.Add(createOrderButton);
        card.Controls.Add(viewOrdersButton);
        return card;
    }

    private void BindGrid(long? preferredTableId)
    {
        suppressSelectionChanged = true;
        try
        {
            grid.Rows.Clear();
            int selectedIndex = -1;
            for (var i = 0; i < currentTables.Count; i++)
            {
                var table = currentTables[i];
                var rowIndex = grid.Rows.Add(
                    $"Mesa #{table.Number:00}",
                    $"{table.Capacity} {(table.Capacity == 1 ? "persona" : "personas")}",
                    string.IsNullOrWhiteSpace(table.Location) ? "Salón general" : table.Location,
                    TableApiService.FormatStatus(table.Status));
                grid.Rows[rowIndex].Tag = table;
                if (preferredTableId.HasValue && table.Id == preferredTableId.Value)
                    selectedIndex = rowIndex;
            }

            if (selectedIndex < 0 && currentTables.Count > 0)
                selectedIndex = 0;

            if (selectedIndex >= 0)
            {
                grid.ClearSelection();
                grid.Rows[selectedIndex].Selected = true;
                grid.CurrentCell = grid.Rows[selectedIndex].Cells[0];
                selectedTable = currentTables[selectedIndex];
            }
            else if (selectedTable is not null && currentTables.All(t => t.Id != selectedTable.Id))
            {
                selectedTable = null;
            }
        }
        finally
        {
            suppressSelectionChanged = false;
        }

        UpdateDetailPanel();
    }

    private void UpdateDetailPanel()
    {
        var busy = pendingOperation is not null;
        if (selectedTable is not { } table)
        {
            selectedTitle.Text = "Selecciona una mesa";
            selectedStatus.Text = "Sin mesa seleccionada";
            selectedStatus.ForeColor = DesktopTheme.Muted;
            selectedDetails.Text = "Elige una mesa del listado para consultar su capacidad, ubicación y disponibilidad actual.";
            selectedHint.Text = "Las órdenes nuevas requieren una mesa disponible.";
            inspectButton.Enabled = false;
            createOrderButton.Enabled = false;
            viewOrdersButton.Enabled = false;
            return;
        }

        selectedTitle.Text = $"Mesa #{table.Number:00}";
        selectedStatus.Text = $"Estado actual: {TableApiService.FormatStatus(table.Status)}";
        selectedStatus.ForeColor = table.Status == TableStatus.Available ? DesktopTheme.Ink : DesktopTheme.Muted;
        var location = string.IsNullOrWhiteSpace(table.Location) ? "Salón general" : table.Location;
        selectedDetails.Text =
            $"ID interno: #{table.Id}\n" +
            $"Capacidad: {table.Capacity} {(table.Capacity == 1 ? "persona" : "personas")}\n" +
            $"Ubicación: {location}\n" +
            $"Versión: {table.Version}";

        selectedHint.Text = table.Status switch
        {
            TableStatus.Available => "Mesa disponible para abrir una orden. Al guardar la orden, el servidor verificará la disponibilidad y ocupará la mesa.",
            TableStatus.Occupied => "Mesa ocupada con atención en curso. Abre sus órdenes para consultar o editar líneas, marcar como servida o pedir la cuenta.",
            TableStatus.Reserved => "Mesa reservada. Puedes consultar las órdenes asociadas a esta mesa.",
            TableStatus.OutOfService => "Mesa fuera de servicio. No admite nuevas órdenes operativas.",
            _ => ""
        };

        inspectButton.Enabled = !busy;
        createOrderButton.Enabled = !busy && table.Status == TableStatus.Available;
        viewOrdersButton.Enabled = !busy;
    }

    private void SetBusy(bool busy)
    {
        onlyAvailable.Enabled = refreshButton.Enabled = grid.Enabled = !busy;
        progress.Visible = busy;
        UseWaitCursor = busy;
        UpdateDetailPanel();
    }

    private void ShowFeedback(string message, bool isError) =>
        DesktopTheme.ShowFeedback(feedback, message, isError);

    private static TableLayoutPanel Stack() => DesktopTheme.Stack();
}
