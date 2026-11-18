using System.Windows.Forms.Automation;

namespace SIGER.Desktop.Controls;

internal static class DesktopTheme
{
    internal static readonly Color Cream = Color.FromArgb(240, 234, 210);
    internal static readonly Color Paper = Color.FromArgb(252, 249, 240);
    internal static readonly Color Ink = Color.FromArgb(51, 39, 32);
    internal static readonly Color Muted = Color.FromArgb(108, 88, 76);
    internal static readonly Color Sidebar = Color.FromArgb(54, 43, 35);
    internal static readonly Color SidebarHover = Color.FromArgb(83, 65, 53);
    internal static readonly Color Olive = Color.FromArgb(173, 193, 120);
    internal static readonly Color SoftOlive = Color.FromArgb(221, 229, 182);
    internal static readonly Color Line = Color.FromArgb(217, 206, 190);
    internal static readonly Color Error = Color.FromArgb(131, 45, 39);

    internal static readonly Font Body = new("Segoe UI", 11);
    internal static readonly Font Small = new("Segoe UI", 10);
    internal static readonly Font Strong = new("Segoe UI Semibold", 11);
    internal static readonly Font Display = new("Georgia", 29);
    internal static readonly Font Brand = new("Georgia", 22);
    internal static readonly Font BrandSubtitle = new("Georgia", 16);

    internal static Label Label(string text, Font font, Color color) =>
        new() { Text = text, Font = font, ForeColor = color, AutoSize = true, Margin = Padding.Empty };

    internal static Button Button(string text, Color background, Color foreground, Color hover)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, MinimumSize = new Size(0, 42),
            Padding = new Padding(15, 5, 15, 5), BackColor = background, ForeColor = foreground,
            Font = Strong, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = background;
        button.FlatAppearance.MouseOverBackColor = hover;
        button.FlatAppearance.MouseDownBackColor = hover;
        return button;
    }

    internal static TableLayoutPanel Stack() =>
        new() { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };

    internal static Control LabeledField(string caption, Control input, int rightMargin = 12)
    {
        var panel = Stack();
        panel.Margin = new Padding(0, 0, rightMargin, 4);
        var label = Label(caption, Small, Muted);
        label.Margin = new Padding(0, 0, 0, 4);
        panel.Controls.Add(label);
        panel.Controls.Add(input);
        return panel;
    }

    internal static Control FeedbackBar(Label feedback, ProgressBar progress, string progressAccessibleName, int maxWidth = 920)
    {
        var host = Stack();
        host.Dock = DockStyle.Top;
        host.Margin = new Padding(0, 0, 0, 12);
        feedback.AccessibleRole = AccessibleRole.Alert;
        feedback.MaximumSize = new Size(maxWidth, 0);
        feedback.Margin = new Padding(0, 0, 0, 4);
        progress.AccessibleName = progressAccessibleName;
        host.Controls.Add(feedback);
        host.Controls.Add(progress);
        return host;
    }

    internal static void ShowFeedback(Label feedback, string message, bool isError)
    {
        feedback.ForeColor = isError ? Error : Muted;
        feedback.Text = message;
        if (message.Length > 0 && feedback.IsHandleCreated)
            feedback.AccessibilityObject.RaiseAutomationNotification(
                AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent, message);
    }

    internal static void ConfigureGrid(DataGridView grid, string accessibleName, int height)
    {
        grid.AccessibleName = accessibleName;
        grid.Dock = DockStyle.Top;
        grid.Height = height;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.ReadOnly = true;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.RowHeadersVisible = false;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Sidebar;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Cream;
        grid.ColumnHeadersDefaultCellStyle.Font = Strong;
        grid.ColumnHeadersHeight = 34;
        grid.DefaultCellStyle.BackColor = Color.White;
        grid.DefaultCellStyle.ForeColor = Ink;
        grid.DefaultCellStyle.SelectionBackColor = SoftOlive;
        grid.DefaultCellStyle.SelectionForeColor = Ink;
        grid.DefaultCellStyle.Font = Body;
        grid.RowTemplate.Height = 32;
    }
}
