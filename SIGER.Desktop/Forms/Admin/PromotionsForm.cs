using System.Globalization;
using System.Windows.Forms.Automation;
using SIGER.Application.DTOs.Products;
using SIGER.Application.DTOs.Promotions;
using SIGER.Desktop.Controls;
using SIGER.Desktop.Services;

namespace SIGER.Desktop.Forms.Admin;

internal sealed class PromotionsForm : UserControl
{
    private const int DefaultPageSize = 20;

    private readonly PromotionApiService promotions;
    private readonly ProductApiService products;

    private readonly CheckBox onlyActiveCheck = new() { Text = "Solo vigentes/activas", AutoSize = true };
    private readonly Button refreshButton = DesktopTheme.Button("&Actualizar promociones", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);
    private readonly Button newModeButton = DesktopTheme.Button("&Nueva promoción", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button prevPageButton = DesktopTheme.Button("◀ Anterior", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button nextPageButton = DesktopTheme.Button("Siguiente ▶", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Label pageStatusLabel = DesktopTheme.Label("Página 1 de 1", DesktopTheme.Small, DesktopTheme.Muted);

    private readonly Label feedback = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Error);
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Height = 5, Dock = DockStyle.Fill };

    private readonly DataGridView promotionsGrid = new();

    private readonly Label editorTitleLabel = DesktopTheme.Label("Crear nueva promoción", DesktopTheme.Brand, DesktopTheme.Ink);
    private readonly TextBox nameInput = new() { MaxLength = 120, Width = 220 };
    private readonly NumericUpDown discountInput = new() { DecimalPlaces = 2, Minimum = 0.01m, Maximum = 100.00m, Value = 10.00m, Width = 100 };
    private readonly DateTimePicker startDatePicker = new() { Format = DateTimePickerFormat.Short, Width = 120 };
    private readonly DateTimePicker endDatePicker = new() { Format = DateTimePickerFormat.Short, Width = 120 };
    private readonly TextBox descriptionInput = new() { MaxLength = 300, Width = 280 };
    private readonly CheckBox activeOnCreateCheck = new() { Text = "Activa al crear", Checked = true, AutoSize = true };
    private readonly Button saveButton = DesktopTheme.Button("&Guardar promoción", DesktopTheme.Olive, DesktopTheme.Ink, DesktopTheme.SoftOlive);
    private readonly Button toggleActiveButton = DesktopTheme.Button("Activar / Desactivar", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);

    // Product associations for the selected promotion
    private readonly DataGridView associatedProductsGrid = new();
    private readonly ComboBox catalogProductsCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 290 };
    private readonly Button addProductButton = DesktopTheme.Button("Asociar producto", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button removeProductButton = DesktopTheme.Button("Quitar producto asociado", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);

    private IReadOnlyList<PromotionDto> currentPromotions = [];
    private IReadOnlyList<ProductDto> catalogProducts = [];
    private PromotionDto? selectedPromotion;
    private PromotionProductDto? selectedAssociatedProduct;
    private bool createMode = true;
    private string? uncertainCreateName;
    private int currentPage = 1;
    private int totalPages = 1;
    private int totalCount;
    private bool suppressSelectionChanged;
    private CancellationTokenSource? pendingOperation;

    internal PromotionsForm(PromotionApiService promotions, ProductApiService products)
    {
        this.promotions = promotions;
        this.products = products;

        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = DesktopTheme.Cream;
        Font = DesktopTheme.Body;
        Margin = Padding.Empty;

        startDatePicker.Value = DateTime.Now.Date;
        endDatePicker.Value = DateTime.Now.Date.AddDays(7);

        var root = Stack();
        root.Dock = DockStyle.Top;
        root.Controls.Add(BuildToolbar());
        root.Controls.Add(DesktopTheme.FeedbackBar(feedback, progress, "Operación de promociones en curso"));
        root.Controls.Add(BuildGridCard());
        root.Controls.Add(BuildEditorCard());
        Controls.Add(root);

        onlyActiveCheck.CheckedChanged += async (_, _) => await LoadPromotionsAsync(1, selectedPromotion?.Id);
        refreshButton.Click += async (_, _) =>
        {
            uncertainCreateName = null;
            await LoadPromotionsAsync(currentPage, selectedPromotion?.Id);
        };
        newModeButton.Click += (_, _) => EnterCreateMode();
        prevPageButton.Click += async (_, _) =>
        {
            if (currentPage > 1) await LoadPromotionsAsync(currentPage - 1, null);
        };
        nextPageButton.Click += async (_, _) =>
        {
            if (currentPage < totalPages) await LoadPromotionsAsync(currentPage + 1, null);
        };
        saveButton.Click += async (_, _) => await SavePromotionAsync();
        toggleActiveButton.Click += async (_, _) => await TogglePromotionActiveAsync();
        addProductButton.Click += async (_, _) => await AssociateProductAsync();
        removeProductButton.Click += async (_, _) => await DisassociateProductAsync();

        promotionsGrid.SelectionChanged += (_, _) =>
        {
            if (suppressSelectionChanged) return;
            if (promotionsGrid.CurrentRow?.Tag is PromotionDto promo)
                LoadPromotionIntoEditor(promo);
        };

        associatedProductsGrid.SelectionChanged += (_, _) =>
        {
            selectedAssociatedProduct = associatedProductsGrid.CurrentRow?.Tag as PromotionProductDto;
            removeProductButton.Enabled = pendingOperation is null && !createMode && selectedAssociatedProduct is not null;
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

    internal async Task EnsureLoadedAsync() => await LoadPromotionsAsync(currentPage, selectedPromotion?.Id);

    private async Task LoadPromotionsAsync(int pageNumber, long? preferredId)
    {
        if (pendingOperation is not null) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            if (catalogProducts.Count == 0)
            {
                var prodPage = await products.GetPagedAsync(1, 200, null, cts.Token);
                catalogProducts = prodPage.Items.OrderBy(p => p.Name).ToArray();
                BindCatalogCombo();
            }

            if (onlyActiveCheck.Checked)
            {
                var active = await promotions.GetActiveAsync(null, cts.Token);
                if (cts.IsCancellationRequested || IsDisposed) return;
                currentPromotions = active.ToArray();
                currentPage = 1;
                totalPages = 1;
                totalCount = currentPromotions.Count;
            }
            else
            {
                var page = await promotions.GetPagedAsync(pageNumber, DefaultPageSize, cts.Token);
                if (cts.IsCancellationRequested || IsDisposed) return;
                currentPromotions = page.Items.ToArray();
                currentPage = page.PageNumber;
                totalPages = Math.Max(1, page.TotalPages);
                totalCount = page.TotalCount;
            }

            BindPromotionsGrid(preferredId);
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

    private async Task SavePromotionAsync()
    {
        if (pendingOperation is not null) return;
        var trimmedName = nameInput.Text.Trim();
        if (createMode && string.Equals(uncertainCreateName, trimmedName, StringComparison.OrdinalIgnoreCase))
        {
            ShowFeedback("El alta anterior de esta promoción quedó incierta por red. Pulsa «Actualizar promociones» antes de reintentar.", isError: true);
            return;
        }

        var localStart = startDatePicker.Value.Date;
        var localEnd = endDatePicker.Value.Date.AddDays(1).AddSeconds(-1);
        var startDto = new DateTimeOffset(localStart, TimeZoneInfo.Local.GetUtcOffset(localStart));
        var endDto = new DateTimeOffset(localEnd, TimeZoneInfo.Local.GetUtcOffset(localEnd));

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            PromotionDto saved = createMode
                ? await promotions.CreateAsync(new CreatePromotionRequestDto
                {
                    Name = trimmedName,
                    Description = descriptionInput.Text,
                    DiscountPercentage = discountInput.Value,
                    StartDate = startDto,
                    EndDate = endDto,
                    IsActive = activeOnCreateCheck.Checked
                }, cts.Token)
                : await promotions.UpdateAsync(selectedPromotion!.Id, new UpdatePromotionRequestDto
                {
                    Name = trimmedName,
                    Description = descriptionInput.Text,
                    DiscountPercentage = discountInput.Value,
                    StartDate = startDto,
                    EndDate = endDto
                }, cts.Token);

            uncertainCreateName = null;
            var page = await promotions.GetPagedAsync(currentPage, DefaultPageSize, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentPromotions = page.Items.ToArray();
            totalPages = Math.Max(1, page.TotalPages);
            totalCount = page.TotalCount;
            BindPromotionsGrid(saved.Id);
            LoadPromotionIntoEditor(saved);
            ShowFeedback($"Promoción #{saved.Id} ({saved.Name}, {saved.DiscountPercentage:0.##}%) guardada.", isError: false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (ApiException error)
        {
            if (cts.IsCancellationRequested || IsDisposed) return;
            if (createMode && error.StatusCode is null)
            {
                uncertainCreateName = trimmedName;
                ShowFeedback($"{error.Message} No se confirmó si la promoción fue creada. Pulsa «Actualizar promociones» antes de reintentar.", isError: true);
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

    private async Task TogglePromotionActiveAsync()
    {
        if (pendingOperation is not null || createMode || selectedPromotion is not { } current) return;

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            await promotions.SetActiveAsync(current.Id, !current.IsActive, cts.Token);
            var refreshed = await promotions.GetByIdAsync(current.Id, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentPromotions = currentPromotions.Select(p => p.Id == refreshed.Id ? refreshed : p).ToArray();
            BindPromotionsGrid(refreshed.Id);
            LoadPromotionIntoEditor(refreshed);
            ShowFeedback($"Promoción #{refreshed.Id} {(refreshed.IsActive ? "activada" : "desactivada")}.", isError: false);
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

    private async Task AssociateProductAsync()
    {
        if (pendingOperation is not null || createMode || selectedPromotion is not { } current) return;
        if (catalogProductsCombo.SelectedItem is not ProductComboItem { Product: var prod }) return;

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            await promotions.AddProductAsync(current.Id, prod.Id, cts.Token);
            var refreshed = await promotions.GetByIdAsync(current.Id, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentPromotions = currentPromotions.Select(p => p.Id == refreshed.Id ? refreshed : p).ToArray();
            BindPromotionsGrid(refreshed.Id);
            LoadPromotionIntoEditor(refreshed);
            ShowFeedback($"Producto «{prod.Name}» asociado a la Promoción #{refreshed.Id}.", isError: false);
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

    private async Task DisassociateProductAsync()
    {
        if (pendingOperation is not null || createMode || selectedPromotion is not { } current || selectedAssociatedProduct is not { } assoc) return;

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            await promotions.RemoveProductAsync(current.Id, assoc.ProductId, cts.Token);
            var refreshed = await promotions.GetByIdAsync(current.Id, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentPromotions = currentPromotions.Select(p => p.Id == refreshed.Id ? refreshed : p).ToArray();
            BindPromotionsGrid(refreshed.Id);
            LoadPromotionIntoEditor(refreshed);
            ShowFeedback($"Producto «{assoc.ProductName}» desasociado de la Promoción #{refreshed.Id}.", isError: false);
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
        selectedPromotion = null;
        selectedAssociatedProduct = null;
        editorTitleLabel.Text = "Crear nueva promoción";
        nameInput.Clear();
        discountInput.Value = 10.00m;
        startDatePicker.Value = DateTime.Now.Date;
        endDatePicker.Value = DateTime.Now.Date.AddDays(7);
        descriptionInput.Clear();
        activeOnCreateCheck.Checked = true;
        activeOnCreateCheck.Enabled = pendingOperation is null;
        toggleActiveButton.Enabled = false;
        associatedProductsGrid.Rows.Clear();
        addProductButton.Enabled = removeProductButton.Enabled = false;
    }

    private void LoadPromotionIntoEditor(PromotionDto promo)
    {
        createMode = false;
        selectedPromotion = promo;
        editorTitleLabel.Text = $"Editar Promoción #{promo.Id} ({promo.Name})";
        nameInput.Text = promo.Name;
        discountInput.Value = Math.Clamp(promo.DiscountPercentage, discountInput.Minimum, discountInput.Maximum);
        startDatePicker.Value = promo.StartDate.ToLocalTime().Date;
        endDatePicker.Value = promo.EndDate.ToLocalTime().Date;
        descriptionInput.Text = promo.Description ?? "";
        activeOnCreateCheck.Checked = promo.IsActive;
        activeOnCreateCheck.Enabled = false;
        toggleActiveButton.Text = promo.IsActive ? "Desactivar promoción" : "Activar promoción";
        toggleActiveButton.Enabled = pendingOperation is null;

        associatedProductsGrid.Rows.Clear();
        foreach (var p in promo.Products)
        {
            var idx = associatedProductsGrid.Rows.Add($"#{p.ProductId}", p.ProductName);
            associatedProductsGrid.Rows[idx].Tag = p;
        }
        selectedAssociatedProduct = associatedProductsGrid.CurrentRow?.Tag as PromotionProductDto;
        addProductButton.Enabled = pendingOperation is null && catalogProductsCombo.Items.Count > 0;
        removeProductButton.Enabled = pendingOperation is null && selectedAssociatedProduct is not null;
    }

    private void BindCatalogCombo()
    {
        catalogProductsCombo.Items.Clear();
        foreach (var p in catalogProducts)
            catalogProductsCombo.Items.Add(new ProductComboItem(p, $"[#{p.Id}] {p.Name} ({p.Price:C2})"));
        if (catalogProductsCombo.Items.Count > 0)
            catalogProductsCombo.SelectedIndex = 0;
    }

    private void BindPromotionsGrid(long? preferredId)
    {
        suppressSelectionChanged = true;
        try
        {
            promotionsGrid.Rows.Clear();
            int selectedIndex = -1;
            for (var i = 0; i < currentPromotions.Count; i++)
            {
                var p = currentPromotions[i];
                var rowIndex = promotionsGrid.Rows.Add(
                    $"#{p.Id}",
                    p.Name,
                    $"{p.DiscountPercentage:0.##}%",
                    $"{p.StartDate.ToLocalTime():d} - {p.EndDate.ToLocalTime():d}",
                    p.IsActive ? "Activa" : "Inactiva",
                    p.Products.Count.ToString(CultureInfo.InvariantCulture));
                promotionsGrid.Rows[rowIndex].Tag = p;
                if (preferredId.HasValue && p.Id == preferredId.Value)
                    selectedIndex = rowIndex;
            }

            if (selectedIndex >= 0)
            {
                promotionsGrid.ClearSelection();
                promotionsGrid.Rows[selectedIndex].Selected = true;
                promotionsGrid.CurrentCell = promotionsGrid.Rows[selectedIndex].Cells[0];
            }
        }
        finally
        {
            suppressSelectionChanged = false;
        }

        pageStatusLabel.Text = $"Página {currentPage} de {totalPages} · {totalCount} promociones";
        prevPageButton.Enabled = pendingOperation is null && !onlyActiveCheck.Checked && currentPage > 1;
        nextPageButton.Enabled = pendingOperation is null && !onlyActiveCheck.Checked && currentPage < totalPages;
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
        onlyActiveCheck.ForeColor = DesktopTheme.Ink;
        onlyActiveCheck.Margin = new Padding(0, 10, 14, 0);
        refreshButton.Margin = new Padding(0, 0, 10, 0);
        bar.Controls.Add(onlyActiveCheck);
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

        var heading = DesktopTheme.Label("1. Promociones registradas (catálogo informativo; no descuenta órdenes automáticamente)", DesktopTheme.Strong, DesktopTheme.Ink);
        heading.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(heading);

        ConfigureGrid(promotionsGrid, "Listado de promociones", 180);
        promotionsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", FillWeight = 10 });
        promotionsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Nombre", FillWeight = 28 });
        promotionsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Discount", HeaderText = "Descuento", FillWeight = 14 });
        promotionsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Dates", HeaderText = "Vigencia", FillWeight = 24 });
        promotionsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Estado", FillWeight = 14 });
        promotionsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Products", HeaderText = "Productos", FillWeight = 12 });
        card.Controls.Add(promotionsGrid);

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
        editorTitleLabel.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(editorTitleLabel);

        var fields = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 10)
        };
        nameInput.AccessibleName = "Nombre de la promoción";
        discountInput.AccessibleName = "Porcentaje de descuento";
        startDatePicker.AccessibleName = "Fecha inicial de vigencia";
        endDatePicker.AccessibleName = "Fecha final de vigencia";
        descriptionInput.AccessibleName = "Descripción de la promoción";

        fields.Controls.Add(LabeledField("Nombre", nameInput));
        fields.Controls.Add(LabeledField("% Descuento", discountInput));
        fields.Controls.Add(LabeledField("Inicio vigencia", startDatePicker));
        fields.Controls.Add(LabeledField("Fin vigencia", endDatePicker));
        fields.Controls.Add(LabeledField("Descripción (opcional)", descriptionInput));
        activeOnCreateCheck.ForeColor = DesktopTheme.Ink;
        activeOnCreateCheck.Margin = new Padding(4, 28, 8, 0);
        fields.Controls.Add(activeOnCreateCheck);
        card.Controls.Add(fields);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 14)
        };
        saveButton.Margin = new Padding(0, 0, 10, 0);
        actions.Controls.Add(saveButton);
        actions.Controls.Add(toggleActiveButton);
        card.Controls.Add(actions);

        var assocHeading = DesktopTheme.Label("2. Productos asociados a la promoción seleccionada", DesktopTheme.Strong, DesktopTheme.Ink);
        assocHeading.Margin = new Padding(0, 0, 0, 8);
        card.Controls.Add(assocHeading);

        ConfigureGrid(associatedProductsGrid, "Productos asociados a la promoción", 125);
        associatedProductsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProductId", HeaderText = "ID Producto", FillWeight = 20 });
        associatedProductsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProductName", HeaderText = "Producto asociado", FillWeight = 80 });
        card.Controls.Add(associatedProductsGrid);

        var assocBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 10, 0, 0)
        };
        catalogProductsCombo.AccessibleName = "Seleccionar producto para asociar a la promoción";
        assocBar.Controls.Add(LabeledField("Producto del catálogo", catalogProductsCombo));
        addProductButton.Margin = new Padding(0, 22, 8, 0);
        removeProductButton.Margin = new Padding(0, 22, 0, 0);
        assocBar.Controls.Add(addProductButton);
        assocBar.Controls.Add(removeProductButton);
        card.Controls.Add(assocBar);

        return card;
    }

    private static void ConfigureGrid(DataGridView grid, string accessibleName, int height) =>
        DesktopTheme.ConfigureGrid(grid, accessibleName, height);

    private static Control LabeledField(string caption, Control input) =>
        DesktopTheme.LabeledField(caption, input);

    private void SetBusy(bool busy)
    {
        onlyActiveCheck.Enabled = refreshButton.Enabled = newModeButton.Enabled =
            promotionsGrid.Enabled = associatedProductsGrid.Enabled =
            nameInput.Enabled = discountInput.Enabled = startDatePicker.Enabled =
            endDatePicker.Enabled = descriptionInput.Enabled = catalogProductsCombo.Enabled =
            saveButton.Enabled = !busy;
        activeOnCreateCheck.Enabled = !busy && createMode;
        toggleActiveButton.Enabled = !busy && !createMode && selectedPromotion is not null;
        addProductButton.Enabled = !busy && !createMode && selectedPromotion is not null && catalogProductsCombo.Items.Count > 0;
        removeProductButton.Enabled = !busy && !createMode && selectedAssociatedProduct is not null;
        prevPageButton.Enabled = !busy && !onlyActiveCheck.Checked && currentPage > 1;
        nextPageButton.Enabled = !busy && !onlyActiveCheck.Checked && currentPage < totalPages;
        progress.Visible = busy;
        UseWaitCursor = busy;
    }

    private void ShowFeedback(string message, bool isError) =>
        DesktopTheme.ShowFeedback(feedback, message, isError);

    private static TableLayoutPanel Stack() => DesktopTheme.Stack();

    private sealed record ProductComboItem(ProductDto Product, string Label)
    {
        public override string ToString() => Label;
    }
}
