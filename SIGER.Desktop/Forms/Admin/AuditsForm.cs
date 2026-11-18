using System.Globalization;
using System.Windows.Forms.Automation;
using SIGER.Application.DTOs.Audits;
using SIGER.Desktop.Controls;
using SIGER.Desktop.Services;

namespace SIGER.Desktop.Forms.Admin;

internal sealed class AuditsForm : UserControl
{
    private const int DefaultPageSize = 20;

    private readonly AuditApiService audits;

    // Filter & lookup controls
    private readonly NumericUpDown auditIdLookup = new() { Minimum = 0, Maximum = 9999999, Value = 0, Width = 95 };
    private readonly NumericUpDown userIdFilter = new() { Minimum = 0, Maximum = 9999999, Value = 0, Width = 95 };
    private readonly TextBox entityFilter = new() { MaxLength = 80, PlaceholderText = "p. ej. Order, Table, User", Width = 165 };
    private readonly CheckBox dateRangeToggle = new() { Text = "Por fecha", AutoSize = true };
    private readonly DateTimePicker startDatePicker = new() { Format = DateTimePickerFormat.Short, Width = 115, Enabled = false };
    private readonly DateTimePicker endDatePicker = new() { Format = DateTimePickerFormat.Short, Width = 115, Enabled = false };
    private readonly Button searchButton = DesktopTheme.Button("&Consultar auditoría", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);
    private readonly Button clearFiltersButton = DesktopTheme.Button("Limpiar filtros", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button prevPageButton = DesktopTheme.Button("◀ Anterior", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button nextPageButton = DesktopTheme.Button("Siguiente ▶", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Label pageStatusLabel = DesktopTheme.Label("Página 1 de 1", DesktopTheme.Small, DesktopTheme.Muted);

    private readonly Label feedback = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Error);
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Height = 5, Dock = DockStyle.Fill };

    private readonly DataGridView grid = new();

    private readonly Label detailTitleLabel = DesktopTheme.Label("Selecciona un registro de auditoría (solo lectura)", DesktopTheme.Brand, DesktopTheme.Ink);
    private readonly Label detailMetaLabel = DesktopTheme.Label("", DesktopTheme.Strong, DesktopTheme.Muted);
    private readonly TextBox previousDataBox = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 90, Width = 410, BackColor = Color.White };
    private readonly TextBox newDataBox = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 90, Width = 410, BackColor = Color.White };

    private IReadOnlyList<AuditDto> currentAudits = [];
    private AuditDto? selectedAudit;
    private int currentPage = 1;
    private int totalPages = 1;
    private int totalCount;
    private bool suppressSelectionChanged;
    private CancellationTokenSource? pendingOperation;

    internal AuditsForm(AuditApiService audits)
    {
        this.audits = audits;
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = DesktopTheme.Cream;
        Font = DesktopTheme.Body;
        Margin = Padding.Empty;

        var root = Stack();
        root.Dock = DockStyle.Top;
        root.Controls.Add(BuildFilterBar());
        root.Controls.Add(DesktopTheme.FeedbackBar(feedback, progress, "Consulta de auditoría en curso"));
        root.Controls.Add(BuildGridCard());
        root.Controls.Add(BuildDetailCard());
        Controls.Add(root);

        dateRangeToggle.CheckedChanged += (_, _) =>
        {
            startDatePicker.Enabled = endDatePicker.Enabled = dateRangeToggle.Checked && pendingOperation is null;
        };
        searchButton.Click += async (_, _) => await LoadAuditsAsync(1, selectedAudit?.Id);
        clearFiltersButton.Click += async (_, _) => await ClearFiltersAsync();
        prevPageButton.Click += async (_, _) =>
        {
            if (currentPage > 1) await LoadAuditsAsync(currentPage - 1, null);
        };
        nextPageButton.Click += async (_, _) =>
        {
            if (currentPage < totalPages) await LoadAuditsAsync(currentPage + 1, null);
        };

        grid.SelectionChanged += (_, _) =>
        {
            if (suppressSelectionChanged) return;
            selectedAudit = grid.CurrentRow?.Tag as AuditDto;
            UpdateDetailView();
        };

        UpdateDetailView();
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

    internal async Task EnsureLoadedAsync() => await LoadAuditsAsync(currentPage, selectedAudit?.Id);

    private async Task LoadAuditsAsync(int pageNumber, long? preferredId)
    {
        if (pendingOperation is not null) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            var lookupId = (long)auditIdLookup.Value;
            if (lookupId > 0)
            {
                var record = await audits.GetByIdAsync(lookupId, cts.Token);
                if (cts.IsCancellationRequested || IsDisposed) return;
                currentAudits = [record];
                currentPage = 1;
                totalPages = 1;
                totalCount = 1;
                BindGrid(record.Id);
                return;
            }

            long? userId = userIdFilter.Value > 0 ? (long)userIdFilter.Value : null;
            DateTimeOffset? startDate = null;
            DateTimeOffset? endDate = null;
            if (dateRangeToggle.Checked)
            {
                var localStart = startDatePicker.Value.Date;
                var localEnd = endDatePicker.Value.Date.AddDays(1).AddTicks(-1);
                startDate = new DateTimeOffset(localStart, TimeZoneInfo.Local.GetUtcOffset(localStart));
                endDate = new DateTimeOffset(localEnd, TimeZoneInfo.Local.GetUtcOffset(localEnd));
            }

            var page = await audits.GetPagedAsync(pageNumber, DefaultPageSize, userId, entityFilter.Text, startDate, endDate, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentAudits = page.Items.ToArray();
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

    private async Task ClearFiltersAsync()
    {
        auditIdLookup.Value = 0;
        userIdFilter.Value = 0;
        entityFilter.Clear();
        dateRangeToggle.Checked = false;
        await LoadAuditsAsync(1, null);
    }

    private void BindGrid(long? preferredId)
    {
        suppressSelectionChanged = true;
        try
        {
            grid.Rows.Clear();
            int selectedIndex = -1;
            for (var i = 0; i < currentAudits.Count; i++)
            {
                var a = currentAudits[i];
                var actor = !string.IsNullOrWhiteSpace(a.UserName)
                    ? $"{a.UserName} (#{a.UserId})"
                    : a.UserId.HasValue ? $"Usuario #{a.UserId.Value}" : "Sistema / Anónimo";

                var rowIndex = grid.Rows.Add(
                    $"#{a.Id}",
                    a.Timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    actor,
                    a.Action,
                    a.Entity,
                    a.EntityId?.ToString(CultureInfo.InvariantCulture) ?? "—",
                    a.IpAddress ?? "—");
                grid.Rows[rowIndex].Tag = a;
                if (preferredId.HasValue && a.Id == preferredId.Value)
                    selectedIndex = rowIndex;
            }

            if (selectedIndex < 0 && currentAudits.Count > 0)
                selectedIndex = 0;

            if (selectedIndex >= 0)
            {
                grid.ClearSelection();
                grid.Rows[selectedIndex].Selected = true;
                grid.CurrentCell = grid.Rows[selectedIndex].Cells[0];
                selectedAudit = currentAudits[selectedIndex];
            }
            else
            {
                selectedAudit = null;
            }
        }
        finally
        {
            suppressSelectionChanged = false;
        }

        pageStatusLabel.Text = $"Página {currentPage} de {totalPages} · {totalCount} registros";
        prevPageButton.Enabled = pendingOperation is null && currentPage > 1;
        nextPageButton.Enabled = pendingOperation is null && currentPage < totalPages;
        UpdateDetailView();
    }

    private void UpdateDetailView()
    {
        if (selectedAudit is not { } a)
        {
            detailTitleLabel.Text = "Selecciona un registro de auditoría (solo lectura)";
            detailMetaLabel.Text = "Sin registro seleccionado.";
            previousDataBox.Text = "";
            newDataBox.Text = "";
            return;
        }

        var actor = !string.IsNullOrWhiteSpace(a.UserName)
            ? $"{a.UserName} (ID #{a.UserId})"
            : a.UserId.HasValue ? $"Usuario #{a.UserId.Value}" : "Sistema / Anónimo";
        detailTitleLabel.Text = $"Auditoría #{a.Id} · {a.Action} en {a.Entity} (ID Entidad: {a.EntityId?.ToString(CultureInfo.InvariantCulture) ?? "—"})";
        detailMetaLabel.Text = $"Fecha: {a.Timestamp.ToLocalTime():G}   |   Responsable: {actor}   |   IP: {a.IpAddress ?? "—"}";
        previousDataBox.Text = string.IsNullOrWhiteSpace(a.PreviousData) ? "(sin valores previos)" : a.PreviousData;
        newDataBox.Text = string.IsNullOrWhiteSpace(a.NewData) ? "(sin valores nuevos)" : a.NewData;
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
        auditIdLookup.AccessibleName = "Buscar registro de auditoría por ID";
        userIdFilter.AccessibleName = "Filtrar auditoría por ID de usuario";
        entityFilter.AccessibleName = "Filtrar auditoría por nombre de entidad";

        bar.Controls.Add(LabeledField("ID Auditoría (0=todas)", auditIdLookup));
        bar.Controls.Add(LabeledField("ID Usuario (0=todos)", userIdFilter));
        bar.Controls.Add(LabeledField("Entidad", entityFilter));
        dateRangeToggle.ForeColor = DesktopTheme.Ink;
        dateRangeToggle.Margin = new Padding(6, 28, 8, 0);
        bar.Controls.Add(dateRangeToggle);
        bar.Controls.Add(LabeledField("Desde", startDatePicker));
        bar.Controls.Add(LabeledField("Hasta", endDatePicker));
        searchButton.Margin = new Padding(4, 22, 8, 0);
        clearFiltersButton.Margin = new Padding(0, 22, 0, 0);
        bar.Controls.Add(searchButton);
        bar.Controls.Add(clearFiltersButton);
        return bar;
    }

    private Control BuildGridCard()
    {
        var card = Stack();
        card.Dock = DockStyle.Top;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(20);
        card.Margin = new Padding(0, 0, 0, 16);

        var heading = DesktopTheme.Label("1. Historial de auditoría (solo lectura)", DesktopTheme.Strong, DesktopTheme.Ink);
        heading.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(heading);

        DesktopTheme.ConfigureGrid(grid, "Tabla de registros de auditoría", 205);

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", FillWeight = 10 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Timestamp", HeaderText = "Fecha y hora", FillWeight = 20 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "User", HeaderText = "Usuario responsable", FillWeight = 22 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "Acción", FillWeight = 14 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Entity", HeaderText = "Entidad", FillWeight = 16 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "EntityId", HeaderText = "ID Entidad", FillWeight = 12 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ip", HeaderText = "IP", FillWeight = 14 });

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

    private Control BuildDetailCard()
    {
        var card = Stack();
        card.Dock = DockStyle.Top;
        card.BackColor = DesktopTheme.Paper;
        card.Padding = new Padding(20);
        card.Margin = Padding.Empty;

        card.Controls.Add(new Panel { Size = new Size(34, 3), BackColor = DesktopTheme.Olive, Margin = new Padding(0, 0, 0, 10) });
        detailTitleLabel.Margin = new Padding(0, 0, 0, 4);
        detailMetaLabel.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(detailTitleLabel);
        card.Controls.Add(detailMetaLabel);

        var snapshots = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = Padding.Empty
        };
        previousDataBox.AccessibleName = "Valores anteriores registrados en auditoría";
        newDataBox.AccessibleName = "Valores nuevos registrados en auditoría";

        snapshots.Controls.Add(LabeledField("Valores anteriores (JSON sanitizado)", previousDataBox));
        snapshots.Controls.Add(LabeledField("Valores nuevos (JSON sanitizado)", newDataBox));
        card.Controls.Add(snapshots);

        return card;
    }

    private static Control LabeledField(string caption, Control input) =>
        DesktopTheme.LabeledField(caption, input, rightMargin: 14);

    private void SetBusy(bool busy)
    {
        auditIdLookup.Enabled = userIdFilter.Enabled = entityFilter.Enabled =
            dateRangeToggle.Enabled = searchButton.Enabled = clearFiltersButton.Enabled =
            grid.Enabled = !busy;
        startDatePicker.Enabled = endDatePicker.Enabled = !busy && dateRangeToggle.Checked;
        prevPageButton.Enabled = !busy && currentPage > 1;
        nextPageButton.Enabled = !busy && currentPage < totalPages;
        progress.Visible = busy;
        UseWaitCursor = busy;
    }

    private void ShowFeedback(string message, bool isError) =>
        DesktopTheme.ShowFeedback(feedback, message, isError);

    private static TableLayoutPanel Stack() => DesktopTheme.Stack();
}
