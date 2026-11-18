namespace SIGER.Desktop
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1110, 660);
            MinimumSize = new Size(810, 545);
            StartPosition = FormStartPosition.CenterScreen;
            Font = SIGER.Desktop.Controls.DesktopTheme.Body;
            Text = "SIGER Desktop | Superior Kitchen Essentials";
        }

        #endregion
    }
}
