using System;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace AutoCADBlockTools
{
    // --- FORM SETTINGS (DÙNG CHO LỆNH RSET) ---
    public class RandomSettingsForm : WinForms.Form
    {
        public double MinScale { get; private set; }
        public double MaxScale { get; private set; }
        public double MinAngle { get; private set; }
        public double MaxAngle { get; private set; }

        private readonly WinForms.NumericUpDown txtMinScale;
        private readonly WinForms.NumericUpDown txtMaxScale;
        private readonly WinForms.NumericUpDown txtMinAngle;
        private readonly WinForms.NumericUpDown txtMaxAngle;

        public RandomSettingsForm(double minS, double maxS, double minA, double maxA)
        {
            this.Text = "Global Settings (RSET)";
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            // Dùng font an toàn
            this.Font = new Drawing.Font("Microsoft Sans Serif", 8.25f, Drawing.FontStyle.Regular);

            int currentY = 15;
            int formWidth = 320;
            int leftMargin = 15;
            int labelW = 35;
            int inputW = 80;
            int gap = 10;
            int indentX = leftMargin + 20;

            // --- SCALE SECTION ---
            WinForms.Label lblTitleS = new WinForms.Label()
            {
                Text = "Scale Parameters",
                Location = new Drawing.Point(leftMargin, currentY),
                AutoSize = true,
                Font = new Drawing.Font(this.Font, Drawing.FontStyle.Bold)
            };
            this.Controls.Add(lblTitleS);
            currentY += 25;

            WinForms.Label lblMinS = new WinForms.Label() { Text = "Min:", Location = new Drawing.Point(indentX, currentY + 3), Width = labelW, AutoSize = true };
            txtMinScale = new WinForms.NumericUpDown() { Location = new Drawing.Point(indentX + 35, currentY), Width = inputW, DecimalPlaces = 2, Increment = 0.1M, Minimum = 0.01M, Maximum = 10000M, Value = (decimal)minS };
            WinForms.Label lblMaxS = new WinForms.Label() { Text = "Max:", Location = new Drawing.Point(indentX + 35 + inputW + gap, currentY + 3), Width = labelW, AutoSize = true };
            txtMaxScale = new WinForms.NumericUpDown() { Location = new Drawing.Point(indentX + 35 + inputW + gap + 35, currentY), Width = inputW, DecimalPlaces = 2, Increment = 0.1M, Minimum = 0.01M, Maximum = 10000M, Value = (decimal)maxS };
            this.Controls.Add(lblMinS); this.Controls.Add(txtMinScale);
            this.Controls.Add(lblMaxS); this.Controls.Add(txtMaxScale);

            currentY += 35;

            // --- ROTATION SECTION ---
            WinForms.Label lblTitleR = new WinForms.Label()
            {
                Text = "Rotation Parameters",
                Location = new Drawing.Point(leftMargin, currentY),
                AutoSize = true,
                Font = new Drawing.Font(this.Font, Drawing.FontStyle.Bold)
            };
            this.Controls.Add(lblTitleR);
            currentY += 25;

            WinForms.Label lblMinR = new WinForms.Label() { Text = "Min:", Location = new Drawing.Point(indentX, currentY + 3), Width = labelW, AutoSize = true };
            txtMinAngle = new WinForms.NumericUpDown() { Location = new Drawing.Point(indentX + 35, currentY), Width = inputW, DecimalPlaces = 0, Increment = 5, Minimum = -3600, Maximum = 3600, Value = (decimal)minA };
            WinForms.Label lblMaxR = new WinForms.Label() { Text = "Max:", Location = new Drawing.Point(indentX + 35 + inputW + gap, currentY + 3), Width = labelW, AutoSize = true };
            txtMaxAngle = new WinForms.NumericUpDown() { Location = new Drawing.Point(indentX + 35 + inputW + gap + 35, currentY), Width = inputW, DecimalPlaces = 0, Increment = 5, Minimum = -3600, Maximum = 3600, Value = (decimal)maxA };
            this.Controls.Add(lblMinR); this.Controls.Add(txtMinAngle);
            this.Controls.Add(lblMaxR); this.Controls.Add(txtMaxAngle);

            currentY += 35;

            // --- BUTTONS ---
            currentY += 5;
            int btnW = 80;
            int gapBtn = 15;
            int startXBtn = (formWidth - (btnW * 2 + gapBtn)) / 2;
            WinForms.Button btnOK = new WinForms.Button() { Text = "OK", DialogResult = WinForms.DialogResult.OK, Location = new Drawing.Point(startXBtn, currentY), Width = btnW };
            WinForms.Button btnCancel = new WinForms.Button() { Text = "Cancel", DialogResult = WinForms.DialogResult.Cancel, Location = new Drawing.Point(startXBtn + btnW + gapBtn, currentY), Width = btnW };
            btnOK.Click += (s, e) =>
            {
                MinScale = (double)txtMinScale.Value;
                MaxScale = (double)txtMaxScale.Value;
                MinAngle = (double)txtMinAngle.Value;
                MaxAngle = (double)txtMaxAngle.Value;
            };

            this.Controls.Add(btnOK); this.Controls.Add(btnCancel);
            this.AcceptButton = btnOK; this.CancelButton = btnCancel;
            this.ClientSize = new Drawing.Size(formWidth, currentY + 40);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // RandomSettingsForm
            // 
            this.ClientSize = new System.Drawing.Size(284, 261);
            this.Name = "RandomSettingsForm";
            this.Load += new System.EventHandler(this.RandomSettingsForm_Load);
            this.ResumeLayout(false);

        }

        private void RandomSettingsForm_Load(object sender, EventArgs e)
        {

        }
    }
}