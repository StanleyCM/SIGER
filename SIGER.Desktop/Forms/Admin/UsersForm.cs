using System.Globalization;
using System.Windows.Forms.Automation;
using SIGER.Application.DTOs.Roles;
using SIGER.Application.DTOs.Users;
using SIGER.Desktop.Controls;
using SIGER.Desktop.Services;

namespace SIGER.Desktop.Forms.Admin;

internal sealed class UsersForm : UserControl
{
    private const int DefaultPageSize = 20;

    private readonly UserApiService users;

    // Filter & pagination controls
    private readonly TextBox searchInput = new() { MaxLength = 100, PlaceholderText = "Buscar por nombre o correo", Width = 220 };
    private readonly ComboBox activeFilterCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly Button searchButton = DesktopTheme.Button("&Buscar / Actualizar", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);
    private readonly Button prevPageButton = DesktopTheme.Button("◀ Anterior", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button nextPageButton = DesktopTheme.Button("Siguiente ▶", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Label pageStatusLabel = DesktopTheme.Label("Página 1 de 1", DesktopTheme.Small, DesktopTheme.Muted);

    private readonly Label feedback = DesktopTheme.Label("", DesktopTheme.Small, DesktopTheme.Error);
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Height = 5, Dock = DockStyle.Fill };

    private readonly DataGridView grid = new();

    // Editor controls
    private readonly Label editorTitleLabel = DesktopTheme.Label("Crear nuevo usuario", DesktopTheme.Brand, DesktopTheme.Ink);
    private readonly ComboBox roleCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly TextBox firstNameInput = new() { MaxLength = 100, Width = 200 };
    private readonly TextBox lastNameInput = new() { MaxLength = 100, Width = 200 };
    private readonly TextBox emailInput = new() { MaxLength = 150, Width = 240 };
    private readonly TextBox phoneInput = new() { MaxLength = 30, Width = 170 };
    private readonly TextBox passwordInput = new() { MaxLength = 128, UseSystemPasswordChar = true, Width = 220 };
    private readonly Label passwordHintLabel = DesktopTheme.Label("Contraseña inicial (solo al crear):", DesktopTheme.Small, DesktopTheme.Muted);
    private readonly Button newModeButton = DesktopTheme.Button("&Nuevo usuario", DesktopTheme.SoftOlive, DesktopTheme.Ink, DesktopTheme.Olive);
    private readonly Button saveButton = DesktopTheme.Button("&Guardar usuario", DesktopTheme.Olive, DesktopTheme.Ink, DesktopTheme.SoftOlive);
    private readonly Button toggleStatusButton = DesktopTheme.Button("Activar / Desactivar", DesktopTheme.Sidebar, DesktopTheme.Cream, DesktopTheme.SidebarHover);

    private IReadOnlyList<RoleDto> availableRoles = [];
    private IReadOnlyList<UserDto> currentUsers = [];
    private UserDto? selectedUser;
    private bool createMode = true;
    private string? uncertainCreateEmail;
    private int currentPage = 1;
    private int totalPages = 1;
    private int totalCount;
    private bool suppressSelectionChanged;
    private CancellationTokenSource? pendingOperation;

    internal UsersForm(UserApiService users)
    {
        this.users = users;
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = DesktopTheme.Cream;
        Font = DesktopTheme.Body;
        Margin = Padding.Empty;

        activeFilterCombo.Items.Add(new ActiveFilterItem(null, "Todos los estados"));
        activeFilterCombo.Items.Add(new ActiveFilterItem(true, "Solo activos"));
        activeFilterCombo.Items.Add(new ActiveFilterItem(false, "Solo inactivos"));
        activeFilterCombo.SelectedIndex = 0;

        var root = Stack();
        root.Dock = DockStyle.Top;
        root.Controls.Add(BuildFilterBar());
        root.Controls.Add(DesktopTheme.FeedbackBar(feedback, progress, "Operación de usuarios en curso"));
        root.Controls.Add(BuildGridCard());
        root.Controls.Add(BuildEditorCard());
        Controls.Add(root);

        searchButton.Click += async (_, _) =>
        {
            uncertainCreateEmail = null;
            await LoadUsersAsync(1, selectedUser?.Id);
        };
        prevPageButton.Click += async (_, _) =>
        {
            if (currentPage > 1) await LoadUsersAsync(currentPage - 1, null);
        };
        nextPageButton.Click += async (_, _) =>
        {
            if (currentPage < totalPages) await LoadUsersAsync(currentPage + 1, null);
        };
        newModeButton.Click += (_, _) => EnterCreateMode();
        saveButton.Click += async (_, _) => await SaveUserAsync();
        toggleStatusButton.Click += async (_, _) => await ToggleSelectedUserStatusAsync();

        grid.SelectionChanged += (_, _) =>
        {
            if (suppressSelectionChanged) return;
            if (grid.CurrentRow?.Tag is UserDto user)
                LoadUserIntoEditor(user);
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

    internal async Task EnsureLoadedAsync() => await LoadUsersAsync(currentPage, selectedUser?.Id);

    private async Task LoadUsersAsync(int pageNumber, long? preferredUserId)
    {
        if (pendingOperation is not null) return;
        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            if (availableRoles.Count == 0)
            {
                var roles = await users.GetRolesAsync(cts.Token);
                if (cts.IsCancellationRequested || IsDisposed) return;
                availableRoles = roles.OrderBy(r => r.Name).ToArray();
                BindRolesCombo(null);
            }

            var isActive = (activeFilterCombo.SelectedItem as ActiveFilterItem)?.IsActive;
            var page = await users.GetPagedAsync(pageNumber, DefaultPageSize, searchInput.Text, isActive, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentUsers = page.Items.ToArray();
            currentPage = page.PageNumber;
            totalPages = Math.Max(1, page.TotalPages);
            totalCount = page.TotalCount;

            BindUsersGrid(preferredUserId);
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

    private async Task SaveUserAsync()
    {
        if (pendingOperation is not null) return;
        if (roleCombo.SelectedItem is not RoleComboItem { Role: var role })
        {
            ShowFeedback("Selecciona un rol válido para el usuario.", isError: true);
            return;
        }

        var emailTrimmed = emailInput.Text.Trim();
        if (createMode && string.Equals(uncertainCreateEmail, emailTrimmed, StringComparison.OrdinalIgnoreCase))
        {
            ShowFeedback("El alta anterior para este correo quedó incierta por red. Pulsa «Buscar / Actualizar» para verificar si se creó antes de reintentar.", isError: true);
            return;
        }

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            UserDto saved;
            if (createMode)
            {
                saved = await users.CreateAsync(new CreateUserRequestDto
                {
                    RoleId = role.Id,
                    FirstName = firstNameInput.Text,
                    LastName = lastNameInput.Text,
                    Email = emailTrimmed,
                    Phone = phoneInput.Text,
                    Password = passwordInput.Text
                }, cts.Token);
                passwordInput.Clear();
                uncertainCreateEmail = null;
            }
            else if (selectedUser is { } existing)
            {
                saved = await users.UpdateAsync(existing.Id, new UpdateUserRequestDto
                {
                    RoleId = role.Id,
                    FirstName = firstNameInput.Text,
                    LastName = lastNameInput.Text,
                    Email = emailTrimmed,
                    Phone = phoneInput.Text
                }, cts.Token);
            }
            else return;

            if (cts.IsCancellationRequested || IsDisposed) return;

            var page = await users.GetPagedAsync(currentPage, DefaultPageSize, searchInput.Text,
                (activeFilterCombo.SelectedItem as ActiveFilterItem)?.IsActive, cts.Token);
            currentUsers = page.Items.ToArray();
            totalPages = Math.Max(1, page.TotalPages);
            totalCount = page.TotalCount;
            BindUsersGrid(saved.Id);
            LoadUserIntoEditor(saved);

            ShowFeedback($"Usuario #{saved.Id} ({saved.Email}) guardado correctamente con rol {saved.RoleName}.", isError: false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (ApiException error)
        {
            if (cts.IsCancellationRequested || IsDisposed) return;
            if (createMode && error.StatusCode is null)
            {
                uncertainCreateEmail = emailTrimmed;
                ShowFeedback($"{error.Message} No se confirmó si el usuario fue creado. Usa «Buscar / Actualizar» antes de volver a enviar.", isError: true);
                return;
            }
            ShowFeedback(error.Message, isError: true);
        }
        finally
        {
            passwordInput.Clear();
            pendingOperation = null;
            if (!IsDisposed) SetBusy(false);
        }
    }

    private async Task ToggleSelectedUserStatusAsync()
    {
        if (pendingOperation is not null || createMode || selectedUser is not { } current) return;
        var nextStatus = !current.IsActive;

        using var cts = new CancellationTokenSource();
        pendingOperation = cts;
        SetBusy(true);
        ShowFeedback("", isError: false);

        try
        {
            await users.UpdateStatusAsync(current.Id, nextStatus, cts.Token);
            var refreshed = await users.GetByIdAsync(current.Id, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;

            currentUsers = currentUsers.Select(u => u.Id == refreshed.Id ? refreshed : u).ToArray();
            BindUsersGrid(refreshed.Id);
            LoadUserIntoEditor(refreshed);
            ShowFeedback($"Usuario #{refreshed.Id} {(refreshed.IsActive ? "activado" : "desactivado")}.", isError: false);
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
        selectedUser = null;
        editorTitleLabel.Text = "Crear nuevo usuario";
        firstNameInput.Clear();
        lastNameInput.Clear();
        emailInput.Clear();
        phoneInput.Clear();
        passwordInput.Clear();
        passwordInput.Enabled = pendingOperation is null;
        passwordHintLabel.Text = "Contraseña inicial (obligatoria al crear):";
        toggleStatusButton.Enabled = false;
        toggleStatusButton.Text = "Activar / Desactivar";
        if (roleCombo.Items.Count > 0 && roleCombo.SelectedIndex < 0)
            roleCombo.SelectedIndex = 0;
    }

    private void LoadUserIntoEditor(UserDto user)
    {
        createMode = false;
        selectedUser = user;
        editorTitleLabel.Text = $"Editar usuario #{user.Id} ({user.Email})";
        firstNameInput.Text = user.FirstName;
        lastNameInput.Text = user.LastName;
        emailInput.Text = user.Email;
        phoneInput.Text = user.Phone ?? "";
        passwordInput.Clear();
        passwordInput.Enabled = false;
        passwordHintLabel.Text = "Contraseña (no editable por esta ruta):";
        BindRolesCombo(user.RoleId);
        toggleStatusButton.Text = user.IsActive ? "Desactivar usuario" : "Activar usuario";
        toggleStatusButton.Enabled = pendingOperation is null;
    }

    private void BindRolesCombo(long? selectedRoleId)
    {
        var targetId = selectedRoleId ?? (roleCombo.SelectedItem as RoleComboItem)?.Role.Id;
        roleCombo.Items.Clear();
        int idx = -1;
        for (var i = 0; i < availableRoles.Count; i++)
        {
            var r = availableRoles[i];
            roleCombo.Items.Add(new RoleComboItem(r, r.Name));
            if (targetId.HasValue && r.Id == targetId.Value) idx = i;
        }
        if (idx >= 0) roleCombo.SelectedIndex = idx;
        else if (roleCombo.Items.Count > 0) roleCombo.SelectedIndex = 0;
    }

    private void BindUsersGrid(long? preferredUserId)
    {
        suppressSelectionChanged = true;
        try
        {
            grid.Rows.Clear();
            int selectedIndex = -1;
            for (var i = 0; i < currentUsers.Count; i++)
            {
                var u = currentUsers[i];
                var rowIndex = grid.Rows.Add(
                    $"#{u.Id}",
                    $"{u.FirstName} {u.LastName}".Trim(),
                    u.Email,
                    u.RoleName,
                    u.Phone ?? "—",
                    u.IsActive ? "Activo" : "Inactivo");
                grid.Rows[rowIndex].Tag = u;
                if (preferredUserId.HasValue && u.Id == preferredUserId.Value)
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

        pageStatusLabel.Text = $"Página {currentPage} de {totalPages} · {totalCount} usuarios";
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
        searchInput.AccessibleName = "Buscar usuario por nombre o correo";
        activeFilterCombo.AccessibleName = "Filtrar usuarios por estado";
        bar.Controls.Add(LabeledField("Búsqueda", searchInput));
        bar.Controls.Add(LabeledField("Estado", activeFilterCombo));
        searchButton.Margin = new Padding(4, 22, 8, 0);
        newModeButton.Margin = new Padding(0, 22, 0, 0);
        bar.Controls.Add(searchButton);
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

        var heading = DesktopTheme.Label("1. Usuarios registrados", DesktopTheme.Strong, DesktopTheme.Ink);
        heading.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(heading);

        DesktopTheme.ConfigureGrid(grid, "Listado de usuarios", 195);

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", FillWeight = 10 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Nombre completo", FillWeight = 24 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Email", HeaderText = "Correo electrónico", FillWeight = 28 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Role", HeaderText = "Rol", FillWeight = 16 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Phone", HeaderText = "Teléfono", FillWeight = 14 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Estado", FillWeight = 12 });

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
        roleCombo.AccessibleName = "Rol del usuario";
        firstNameInput.AccessibleName = "Nombre del usuario";
        lastNameInput.AccessibleName = "Apellido del usuario";
        emailInput.AccessibleName = "Correo electrónico del usuario";
        phoneInput.AccessibleName = "Teléfono del usuario";
        passwordInput.AccessibleName = "Contraseña inicial del usuario";

        fields.Controls.Add(LabeledField("Rol", roleCombo));
        fields.Controls.Add(LabeledField("Nombre", firstNameInput));
        fields.Controls.Add(LabeledField("Apellido", lastNameInput));
        fields.Controls.Add(LabeledField("Correo electrónico", emailInput));
        fields.Controls.Add(LabeledField("Teléfono (opcional)", phoneInput));

        var pwdPanel = Stack();
        pwdPanel.Margin = new Padding(0, 0, 12, 4);
        passwordHintLabel.Margin = new Padding(0, 0, 0, 4);
        pwdPanel.Controls.Add(passwordHintLabel);
        pwdPanel.Controls.Add(passwordInput);
        fields.Controls.Add(pwdPanel);

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
        actions.Controls.Add(toggleStatusButton);
        card.Controls.Add(actions);

        return card;
    }

    private static Control LabeledField(string caption, Control input) =>
        DesktopTheme.LabeledField(caption, input);

    private void SetBusy(bool busy)
    {
        searchInput.Enabled = activeFilterCombo.Enabled = searchButton.Enabled =
            newModeButton.Enabled = grid.Enabled = roleCombo.Enabled =
            firstNameInput.Enabled = lastNameInput.Enabled = emailInput.Enabled =
            phoneInput.Enabled = saveButton.Enabled = !busy;
        passwordInput.Enabled = !busy && createMode;
        toggleStatusButton.Enabled = !busy && !createMode && selectedUser is not null;
        prevPageButton.Enabled = !busy && currentPage > 1;
        nextPageButton.Enabled = !busy && currentPage < totalPages;
        progress.Visible = busy;
        UseWaitCursor = busy;
    }

    private void ShowFeedback(string message, bool isError) =>
        DesktopTheme.ShowFeedback(feedback, message, isError);

    private static TableLayoutPanel Stack() => DesktopTheme.Stack();

    private sealed record ActiveFilterItem(bool? IsActive, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record RoleComboItem(RoleDto Role, string Label)
    {
        public override string ToString() => Label;
    }
}
