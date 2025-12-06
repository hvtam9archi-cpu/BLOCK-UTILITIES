using System;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace AutoCADBlockTools
{
    public class RenameBlockForm : WinForms.Form
    {
        public string ResultName { get; private set; }

        private readonly WinForms.TextBox _txtNewName;
        private readonly WinForms.CheckBox _chkRandom;
        private readonly string _currentName;

        public RenameBlockForm(string currentName)
        {
            _currentName = currentName;

            // --- Form Settings ---
            this.Text = "Rename Block (RB)";
            this.Size = new Drawing.Size(350, 220);
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            this.Font = new Drawing.Font("Microsoft Sans Serif", 8.25f);

            int x = 15, y = 15, w = 300;

            // 1. Label Current Name
            WinForms.Label lblCurrent = new WinForms.Label()
            {
                Text = $"Current Name: {_currentName}",
                Location = new Drawing.Point(x, y),
                AutoSize = true,
                Font = new Drawing.Font(this.Font, Drawing.FontStyle.Bold)
            };
            this.Controls.Add(lblCurrent);
            y += 30;

            // 2. Label New Name
            WinForms.Label lblNew = new WinForms.Label() { Text = "New Name:", Location = new Drawing.Point(x, y), AutoSize = true };
            this.Controls.Add(lblNew);
            y += 20;

            // 3. TextBox New Name
            _txtNewName = new WinForms.TextBox()
            {
                Location = new Drawing.Point(x, y),
                Width = w,
                Text = _currentName
            };
            this.Controls.Add(_txtNewName);
            y += 30;

            // 4. CheckBox Random
            _chkRandom = new WinForms.CheckBox()
            {
                Text = "Random Name",
                Location = new Drawing.Point(x, y),
                AutoSize = true
            };
            _chkRandom.CheckedChanged += ChkRandom_CheckedChanged;
            this.Controls.Add(_chkRandom);
            y += 40;

            // 5. Buttons
            WinForms.Button btnOk = new WinForms.Button() { Text = "OK", DialogResult = WinForms.DialogResult.OK, Location = new Drawing.Point(80, y), Width = 80 };
            WinForms.Button btnCancel = new WinForms.Button() { Text = "Cancel", DialogResult = WinForms.DialogResult.Cancel, Location = new Drawing.Point(170, y), Width = 80 };

            btnOk.Click += BtnOk_Click;

            this.Controls.Add(btnOk);
            this.Controls.Add(btnCancel);
            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
        }

        private void ChkRandom_CheckedChanged(object sender, EventArgs e)
        {
            if (_chkRandom.Checked)
            {
                // CẬP NHẬT: Logic giống hệt lệnh AB
                // Sử dụng ký tự '@' ở đầu và lấy 16 chữ số đầu của Ticks
                string ticks = DateTime.Now.Ticks.ToString();
                if (ticks.Length > 16)
                {
                    _txtNewName.Text = $"@{ticks.Substring(0, 16)}";
                }
                else
                {
                    _txtNewName.Text = $"@{ticks}";
                }

                _txtNewName.Enabled = false;
            }
            else
            {
                _txtNewName.Text = _currentName;
                _txtNewName.Enabled = true;
                _txtNewName.Focus();
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_txtNewName.Text))
            {
                WinForms.MessageBox.Show("Please enter a valid name.", "Error", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                this.DialogResult = WinForms.DialogResult.None; // Giữ form mở
                return;
            }
            ResultName = _txtNewName.Text.Trim();
        }
    }
}