using System.Globalization;
using System.Net;
using System.Windows.Forms.Automation;
using SIGER.Application.DTOs.Categories;
using SIGER.Application.DTOs.Products;
using SIGER.Desktop.Controls;
using SIGER.Desktop.Services;

namespace SIGER.Desktop.Forms.Admin;

internal sealed class ProductsForm : UserControl
{
    private const int DefaultPageSize = 20;

    private readonly ProductApiService products;
    private readonly CategoryApiService categories;

    private readonly ComboBox categoryFilterCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly Button refreshButton = DesktopTheme.Button("&Buscar / Actualizar", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);
    private readonly Button newModeButton = DesktopTheme.Button("&Nuevo producto", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button prevPageButton = DesktopTheme.Button("◀ Anterior", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button nextPageButton = DesktopTheme.Button("Siguiente ▶", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Label pageStatusLabel = DesktopTheme.Label("Página 1 de 1", DesktopTheme.Small, DesktopTheme.Muted);

    private readonly Label feedback = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Error);
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Height = 5, Dock = DockStyle.Fill };

    private readonly DataGridView grid = new();

    private readonly Label editorTitleLabel = DesktopTheme.Label("Crear nuevo producto", DesktopTheme.Brand, DesktopTheme.Ink);
    private readonly ComboBox categoryCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly TextBox nameInput = new() { MaxLength = 120, Width = 220 };
    private readonly NumericUpDown priceInput = new() { DecimalPlaces = 2, Minimum = 0m, Maximum = 999999.99m, Increment = 1.00m, Width = 120 };
    private readonly TextBox descriptionInput = new() { MaxLength = 300, Width = 260 };
    private readonly TextBox imageUrlInput = new() { MaxLength = 500, Width = 250 };
    private readonly CheckBox availableCheck = new() { Text = "Disponible al crear", Checked = true, AutoSize = true };
    private readonly Button saveButton = DesktopTheme.Button("&Guardar producto", DesktopTheme.Olive, DesktopTheme.Ink, DesktopTheme.SoftOlive);
    private readonly Button toggleAvailabilityButton = DesktopTheme.Button("Cambiar disponibilidad", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);

    private IReadOnlyList<CategoryDto> loadedCategories = [];
    private IReadOnlyList<ProductDto> currentProducts = [];
    private ProductDto? selectedProduct;
    private bool createMode = true;
    private string? uncertainCreateName;
    private int currentPage = 1;
    private int totalPages = 1;
    private int totalCount;
    private bool suppressSelectionChanged;
    private CancellationTokenSource? pendingOperation;

    internal ProductsForm(ProductApiService products, CategoryApiService categories)
    {
        this.products = products;
        this.categories = categories;

        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = DesktopTheme.Cream;
        Font = DesktopTheme.Body;
        Margin = Padding.Empty;

        var root = Stack();
        root.Dock = DockStyle.Top;
        root.Controls.Add(BuildToolbar());
        root.Controls.Add(DesktopTheme.FeedbackBar(feedback, progress, "Operación de productos en curso"));
        root.Controls.Add(BuildGridCard());
        root.Controls.Add(BuildEditorCard());
        Controls.Add(root);

        refreshButton.Click += async (_, _) =>
        {
            uncertainCreateName = null;
            await LoadProductsAsync(1, selectedProduct?.Id, reloadCategories: true);
        };
        newModeButton.Click += (_, _) => EnterCreateMode();
        prevPageButton.Click += async (_, _) =>
        {
            if (currentPage > 1) await LoadProductsAsync(currentPage - 1, null);
        };
        nextPageButton.Click += async (_, _) =>
        {
            if (currentPage < totalPages) await LoadProductsAsync(currentPage + 1, null);
        };
        saveButton.Click += async (_, _) => await SaveProductAsync();
        toggleAvailabilityButton.Click += async (_, _) => await ToggleAvailabilityAsync();

        grid.SelectionChanged += (_, _) =>
        {
            if (suppressSelectionChanged) return;
            if (grid.CurrentRow?.Tag is ProductDto product)
                LoadProductIntoEditor(product);
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

    internal async Task EnsureLoadedAsync() => await LoadProductsAsync(currentPage, selectedProduct?.Id, reloadCategories: true);

    private async Task LoadProductsAsync(int pageNumber, long? preferredId, bool reloadCategories = false)
    {
        if (pendingOperation is not null) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            if (loadedCategories.Count == 0 || reloadCategories)
            {
                var catPage = await categories.GetPagedAsync(1, 200, cts.Token);
                loadedCategories = catPage.Items.OrderBy(c => c.Name).ToArray();
                BindCategoryCombos(
                    (categoryFilterCombo.SelectedItem as CategoryFilterItem)?.CategoryId,
                    selectedProduct?.CategoryId);
            }

            var filterCatId = (categoryFilterCombo.SelectedItem as CategoryFilterItem)?.CategoryId;
            var page = await products.GetPagedAsync(pageNumber, DefaultPageSize, filterCatId, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentProducts = page.Items.ToArray();
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

    private async Task SaveProductAsync()
    {
        if (pendingOperation is not null) return;
        if (categoryCombo.SelectedItem is not CategoryComboItem { Category: var cat })
        {
            ShowFeedback("Selecciona una categoría válida para el producto.", isError: true);
            return;
        }

        var trimmedName = nameInput.Text.Trim();
        if (createMode && string.Equals(uncertainCreateName, trimmedName, StringComparison.OrdinalIgnoreCase))
        {
            ShowFeedback("El guardado anterior de este producto quedó incierto por red. Pulsa «Buscar / Actualizar» antes de reintentar.", isError: true);
            return;
        }

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            ProductDto saved = createMode
                ? await products.CreateAsync(new CreateProductRequestDto
                {
                    CategoryId = cat.Id,
                    Name = trimmedName,
                    Description = descriptionInput.Text,
                    Price = priceInput.Value,
                    IsAvailable = availableCheck.Checked,
                    ImageUrl = imageUrlInput.Text
                }, cts.Token)
                : await products.UpdateAsync(selectedProduct!.Id, new UpdateProductRequestDto
                {
                    CategoryId = cat.Id,
                    Name = trimmedName,
                    Description = descriptionInput.Text,
                    Price = priceInput.Value,
                    ImageUrl = imageUrlInput.Text,
                    Version = selectedProduct.Version
                }, cts.Token);

            uncertainCreateName = null;
            var filterCatId = (categoryFilterCombo.SelectedItem as CategoryFilterItem)?.CategoryId;
            var page = await products.GetPagedAsync(currentPage, DefaultPageSize, filterCatId, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentProducts = page.Items.ToArray();
            totalPages = Math.Max(1, page.TotalPages);
            totalCount = page.TotalCount;
            BindGrid(saved.Id);
            LoadProductIntoEditor(saved);
            ShowFeedback($"Producto #{saved.Id} ({saved.Name}) guardado. Precio servidor: {saved.Price:C2} · Versión {saved.Version}.", isError: false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (ApiException error)
        {
            if (cts.IsCancellationRequested || IsDisposed) return;
            if (createMode && error.StatusCode is null)
            {
                uncertainCreateName = trimmedName;
                ShowFeedback($"{error.Message} No se confirmó si el producto fue creado. Pulsa «Buscar / Actualizar» antes de reintentar.", isError: true);
                return;
            }
            if (!createMode && selectedProduct is { } current && error.StatusCode == HttpStatusCode.Conflict)
            {
                try
                {
                    var reloaded = await products.GetByIdAsync(current.Id, cts.Token);
                    LoadProductIntoEditor(reloaded);
                    ShowFeedback($"{error.Message} Se recargó el Producto #{reloaded.Id} (Versión {reloaded.Version}).", isError: true);
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

    private async Task ToggleAvailabilityAsync()
    {
        if (pendingOperation is not null || createMode || selectedProduct is not { } current) return;

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            await products.ChangeAvailabilityAsync(current.Id, !current.IsAvailable, current.Version, cts.Token);
            var refreshed = await products.GetByIdAsync(current.Id, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentProducts = currentProducts.Select(p => p.Id == refreshed.Id ? refreshed : p).ToArray();
            BindGrid(refreshed.Id);
            LoadProductIntoEditor(refreshed);
            ShowFeedback($"Producto #{refreshed.Id} ahora está {(refreshed.IsAvailable ? "Disponible" : "No disponible")} (Versión {refreshed.Version}).", isError: false);
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
        selectedProduct = null;
        editorTitleLabel.Text = "Crear nuevo producto";
        nameInput.Clear();
        priceInput.Value = 0m;
        descriptionInput.Clear();
        imageUrlInput.Clear();
        availableCheck.Checked = true;
        availableCheck.Enabled = pendingOperation is null;
        toggleAvailabilityButton.Enabled = false;
        toggleAvailabilityButton.Text = "Cambiar disponibilidad";
    }

    private void LoadProductIntoEditor(ProductDto product)
    {
        createMode = false;
        selectedProduct = product;
        editorTitleLabel.Text = $"Editar producto #{product.Id} ({product.Name}) · Versión {product.Version}";
        nameInput.Text = product.Name;
        priceInput.Value = Math.Clamp(product.Price, priceInput.Minimum, priceInput.Maximum);
        descriptionInput.Text = product.Description ?? "";
        imageUrlInput.Text = product.ImageUrl ?? "";
        availableCheck.Checked = product.IsAvailable;
        availableCheck.Enabled = false;
        BindCategoryCombos((categoryFilterCombo.SelectedItem as CategoryFilterItem)?.CategoryId, product.CategoryId);
        toggleAvailabilityButton.Text = product.IsAvailable ? "Marcar no disponible" : "Marcar disponible";
        toggleAvailabilityButton.Enabled = pendingOperation is null;
    }

    private void BindCategoryCombos(long? filterCategoryId, long? editorCategoryId)
    {
        categoryFilterCombo.Items.Clear();
        categoryFilterCombo.Items.Add(new CategoryFilterItem(null, "Todas las categorías"));
        int filterIdx = 0;

        categoryCombo.Items.Clear();
        int editorIdx = -1;

        for (var i = 0; i < loadedCategories.Count; i++)
        {
            var c = loadedCategories[i];
            categoryFilterCombo.Items.Add(new CategoryFilterItem(c.Id, c.Name));
            if (filterCategoryId.HasValue && c.Id == filterCategoryId.Value) filterIdx = i + 1;

            categoryCombo.Items.Add(new CategoryComboItem(c, c.IsActive ? c.Name : $"{c.Name} (Inactiva)"));
            if (editorCategoryId.HasValue && c.Id == editorCategoryId.Value) editorIdx = i;
        }

        categoryFilterCombo.SelectedIndex = filterIdx;
        if (editorIdx >= 0) categoryCombo.SelectedIndex = editorIdx;
        else if (categoryCombo.Items.Count > 0) categoryCombo.SelectedIndex = 0;
    }

    private void BindGrid(long? preferredId)
    {
        suppressSelectionChanged = true;
        try
        {
            grid.Rows.Clear();
            int selectedIndex = -1;
            for (var i = 0; i < currentProducts.Count; i++)
            {
                var p = currentProducts[i];
                var rowIndex = grid.Rows.Add(
                    $"#{p.Id}",
                    p.Name,
                    p.CategoryName,
                    p.Price.ToString("C2", CultureInfo.CurrentCulture),
                    p.IsAvailable ? "Disponible" : "No disponible",
                    p.Version.ToString(CultureInfo.InvariantCulture));
                grid.Rows[rowIndex].Tag = p;
                if (preferredId.HasValue && p.Id == preferredId.Value)
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

        pageStatusLabel.Text = $"Página {currentPage} de {totalPages} · {totalCount} productos";
        prevPageButton.Enabled = pendingOperation is null && currentPage > 1;
        nextPageButton.Enabled = pendingOperation is null && currentPage < totalPages;
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
        categoryFilterCombo.AccessibleName = "Filtrar productos por categoría";
        bar.Controls.Add(LabeledField("Categoría", categoryFilterCombo));
        refreshButton.Margin = new Padding(4, 22, 10, 0);
        newModeButton.Margin = new Padding(0, 22, 0, 0);
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

        var heading = DesktopTheme.Label("1. Productos del catálogo", DesktopTheme.Strong, DesktopTheme.Ink);
        heading.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(heading);

        DesktopTheme.ConfigureGrid(grid, "Listado de productos", 195);

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", FillWeight = 10 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Producto", FillWeight = 32 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = "Categoría", FillWeight = 22 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Price", HeaderText = "Precio (decimal)", FillWeight = 16 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Available", HeaderText = "Disponibilidad", FillWeight = 16 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Version", HeaderText = "Versión", FillWeight = 10 });

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
        editorTitleLabel.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(editorTitleLabel);

        var fields = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 10)
        };
        categoryCombo.AccessibleName = "Categoría del producto";
        nameInput.AccessibleName = "Nombre del producto";
        priceInput.AccessibleName = "Precio del producto";
        descriptionInput.AccessibleName = "Descripción del producto";
        imageUrlInput.AccessibleName = "URL de imagen del producto";

        fields.Controls.Add(LabeledField("Categoría", categoryCombo));
        fields.Controls.Add(LabeledField("Nombre", nameInput));
        fields.Controls.Add(LabeledField("Precio (decimal)", priceInput));
        fields.Controls.Add(LabeledField("Descripción (opcional)", descriptionInput));
        fields.Controls.Add(LabeledField("URL imagen (opcional)", imageUrlInput));
        availableCheck.ForeColor = DesktopTheme.Ink;
        availableCheck.Margin = new Padding(4, 28, 8, 0);
        fields.Controls.Add(availableCheck);

        card.Controls.Add(fields);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = Padding.Empty
        };
        saveButton.Margin = new Padding(0, 0, 10, 0);
        actions.Controls.Add(saveButton);
        actions.Controls.Add(toggleAvailabilityButton);
        card.Controls.Add(actions);

        return card;
    }

    private static Control LabeledField(string caption, Control input) =>
        DesktopTheme.LabeledField(caption, input);

    private void SetBusy(bool busy)
    {
        categoryFilterCombo.Enabled = refreshButton.Enabled = newModeButton.Enabled =
            grid.Enabled = categoryCombo.Enabled = nameInput.Enabled = priceInput.Enabled =
            descriptionInput.Enabled = imageUrlInput.Enabled = saveButton.Enabled = !busy;
        availableCheck.Enabled = !busy && createMode;
        toggleAvailabilityButton.Enabled = !busy && !createMode && selectedProduct is not null;
        prevPageButton.Enabled = !busy && currentPage > 1;
        nextPageButton.Enabled = !busy && currentPage < totalPages;
        progress.Visible = busy;
        UseWaitCursor = busy;
    }

    private void ShowFeedback(string message, bool isError) =>
        DesktopTheme.ShowFeedback(feedback, message, isError);

    private static TableLayoutPanel Stack() => DesktopTheme.Stack();

    private sealed record CategoryFilterItem(long? CategoryId, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record CategoryComboItem(CategoryDto Category, string Label)
    {
        public override string ToString() => Label;
    }
}
