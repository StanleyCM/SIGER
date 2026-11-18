using System.Windows.Forms.Automation;
using SIGER.Application.DTOs.Categories;
using SIGER.Desktop.Controls;
using SIGER.Desktop.Services;

namespace SIGER.Desktop.Forms.Admin;

internal sealed class CategoriesForm : UserControl
{
    private readonly CategoryApiService categories;

    private readonly Button refreshButton = DesktopTheme.Button("&Actualizar categorías", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);
    private readonly Button newModeButton = DesktopTheme.Button("&Nueva categoría", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Label feedback = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Error);
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Height = 5, Dock = DockStyle.Fill };

    private readonly DataGridView grid = new();
    private readonly Label editorTitleLabel = DesktopTheme.Label("Crear nueva categoría", DesktopTheme.Brand, DesktopTheme.Ink);
    private readonly TextBox nameInput = new() { MaxLength = 100, Width = 250 };
    private readonly TextBox descriptionInput = new() { MaxLength = 250, Width = 360 };
    private readonly Button saveButton = DesktopTheme.Button("&Guardar categoría", DesktopTheme.Olive, DesktopTheme.Ink, DesktopTheme.SoftOlive);
    private readonly Button toggleActiveButton = DesktopTheme.Button("Activar / Desactivar", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);

    private IReadOnlyList<CategoryDto> currentCategories = [];
    private CategoryDto? selectedCategory;
    private bool createMode = true;
    private string? uncertainCreateName;
    private bool suppressSelectionChanged;
    private CancellationTokenSource? pendingOperation;

    internal CategoriesForm(CategoryApiService categories)
    {
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
        root.Controls.Add(DesktopTheme.FeedbackBar(feedback, progress, "Operación de categorías en curso"));
        root.Controls.Add(BuildGridCard());
        root.Controls.Add(BuildEditorCard());
        Controls.Add(root);

        refreshButton.Click += async (_, _) =>
        {
            uncertainCreateName = null;
            await RefreshCategoriesAsync(selectedCategory?.Id);
        };
        newModeButton.Click += (_, _) => EnterCreateMode();
        saveButton.Click += async (_, _) => await SaveCategoryAsync();
        toggleActiveButton.Click += async (_, _) => await ToggleSelectedCategoryAsync();

        grid.SelectionChanged += (_, _) =>
        {
            if (suppressSelectionChanged) return;
            if (grid.CurrentRow?.Tag is CategoryDto category)
                LoadCategoryIntoEditor(category);
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

    internal async Task RefreshCategoriesAsync(long? preferredId = null)
    {
        if (pendingOperation is not null) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            var page = await categories.GetPagedAsync(1, 200, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentCategories = page.Items.OrderBy(c => c.Name).ToArray();
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

    private async Task SaveCategoryAsync()
    {
        if (pendingOperation is not null) return;
        var trimmedName = nameInput.Text.Trim();
        if (createMode && string.Equals(uncertainCreateName, trimmedName, StringComparison.OrdinalIgnoreCase))
        {
            ShowFeedback("El guardado anterior de esta categoría quedó incierto por red. Pulsa «Actualizar categorías» antes de reintentar.", isError: true);
            return;
        }

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            CategoryDto saved = createMode
                ? await categories.CreateAsync(trimmedName, descriptionInput.Text, cts.Token)
                : await categories.UpdateAsync(selectedCategory!.Id, trimmedName, descriptionInput.Text, cts.Token);

            uncertainCreateName = null;
            var page = await categories.GetPagedAsync(1, 200, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentCategories = page.Items.OrderBy(c => c.Name).ToArray();
            BindGrid(saved.Id);
            LoadCategoryIntoEditor(saved);
            ShowFeedback($"Categoría #{saved.Id} ({saved.Name}) guardada correctamente.", isError: false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (ApiException error)
        {
            if (cts.IsCancellationRequested || IsDisposed) return;
            if (createMode && error.StatusCode is null)
            {
                uncertainCreateName = trimmedName;
                ShowFeedback($"{error.Message} No se confirmó si la categoría fue creada. Pulsa «Actualizar categorías» antes de reintentar.", isError: true);
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

    private async Task ToggleSelectedCategoryAsync()
    {
        if (pendingOperation is not null || createMode || selectedCategory is not { } current) return;
        var nextActive = !current.IsActive;

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            await categories.SetActiveAsync(current.Id, nextActive, cts.Token);
            var refreshed = await categories.GetByIdAsync(current.Id, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentCategories = currentCategories.Select(c => c.Id == refreshed.Id ? refreshed : c).ToArray();
            BindGrid(refreshed.Id);
            LoadCategoryIntoEditor(refreshed);
            ShowFeedback($"Categoría #{refreshed.Id} {(refreshed.IsActive ? "activada" : "desactivada")}.", isError: false);
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
        selectedCategory = null;
        editorTitleLabel.Text = "Crear nueva categoría";
        nameInput.Clear();
        descriptionInput.Clear();
        toggleActiveButton.Enabled = false;
        toggleActiveButton.Text = "Activar / Desactivar";
    }

    private void LoadCategoryIntoEditor(CategoryDto category)
    {
        createMode = false;
        selectedCategory = category;
        editorTitleLabel.Text = $"Editar categoría #{category.Id} ({category.Name})";
        nameInput.Text = category.Name;
        descriptionInput.Text = category.Description ?? "";
        toggleActiveButton.Text = category.IsActive ? "Desactivar categoría" : "Activar categoría";
        toggleActiveButton.Enabled = pendingOperation is null;
    }

    private void BindGrid(long? preferredId)
    {
        suppressSelectionChanged = true;
        try
        {
            grid.Rows.Clear();
            int selectedIndex = -1;
            for (var i = 0; i < currentCategories.Count; i++)
            {
                var c = currentCategories[i];
                var rowIndex = grid.Rows.Add(
                    $"#{c.Id}",
                    c.Name,
                    string.IsNullOrWhiteSpace(c.Description) ? "—" : c.Description,
                    c.IsActive ? "Activa" : "Inactiva");
                grid.Rows[rowIndex].Tag = c;
                if (preferredId.HasValue && c.Id == preferredId.Value)
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

        var heading = DesktopTheme.Label("1. Categorías del catálogo", DesktopTheme.Strong, DesktopTheme.Ink);
        heading.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(heading);

        DesktopTheme.ConfigureGrid(grid, "Listado de categorías", 200);

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", FillWeight = 12 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Nombre", FillWeight = 30 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Description", HeaderText = "Descripción", FillWeight = 42 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Estado", FillWeight = 16 });

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
            Margin = new Padding(0, 0, 0, 10)
        };
        nameInput.AccessibleName = "Nombre de la categoría";
        descriptionInput.AccessibleName = "Descripción de la categoría";

        var namePanel = Stack();
        namePanel.Margin = new Padding(0, 0, 14, 4);
        namePanel.Controls.Add(DesktopTheme.Label("Nombre", DesktopTheme.Small, DesktopTheme.Muted));
        namePanel.Controls.Add(nameInput);

        var descPanel = Stack();
        descPanel.Margin = new Padding(0, 0, 14, 4);
        descPanel.Controls.Add(DesktopTheme.Label("Descripción (opcional)", DesktopTheme.Small, DesktopTheme.Muted));
        descPanel.Controls.Add(descriptionInput);

        fields.Controls.Add(namePanel);
        fields.Controls.Add(descPanel);
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
        actions.Controls.Add(toggleActiveButton);
        card.Controls.Add(actions);

        return card;
    }

    private void SetBusy(bool busy)
    {
        refreshButton.Enabled = newModeButton.Enabled = grid.Enabled =
            nameInput.Enabled = descriptionInput.Enabled = saveButton.Enabled = !busy;
        toggleActiveButton.Enabled = !busy && !createMode && selectedCategory is not null;
        progress.Visible = busy;
        UseWaitCursor = busy;
    }

    private void ShowFeedback(string message, bool isError) =>
        DesktopTheme.ShowFeedback(feedback, message, isError);

    private static TableLayoutPanel Stack() => DesktopTheme.Stack();
}
