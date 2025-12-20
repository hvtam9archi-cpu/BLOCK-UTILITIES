using System.Windows.Forms;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace AutoCADBlockTools
{
    // --- GUI FORM CLASS CHO LỆNH JBP ---
    public class JbpForm : WinForms.Form
    {
        public Justification SelectedJustification { get; private set; }
        public bool RetainVisualPosition { get; private set; }

        private readonly JustificationControl[] _controls = new JustificationControl[9];
        private readonly WinForms.RadioButton _rbVisual;
        private readonly WinForms.RadioButton _rbInsert;

        public JbpForm(Justification defaultJus, bool defaultRetain)
        {
            this.Text = "Justify Base Point";
            this.Size = new Drawing.Size(260, 340);
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            // --- Group Box: Justification ---
            WinForms.GroupBox gbJus = new WinForms.GroupBox
            {
                Text = "Justification",
                Location = new Drawing.Point(12, 12),
                Size = new Drawing.Size(220, 180)
            };
            this.Controls.Add(gbJus);

            Justification[] values = {
                Justification.TopLeft, Justification.TopCenter, Justification.TopRight,
                Justification.MiddleLeft, Justification.MiddleCenter, Justification.MiddleRight,
                Justification.BottomLeft, Justification.BottomCenter, Justification.BottomRight
            };

            // Layout Grid 3x3
            int itemW = 55;
            int itemH = 40;
            int gapX = 10;
            int gapY = 10;
            int startX = (gbJus.Width - (3 * itemW + 2 * gapX)) / 2;
            int startY = 25;
            for (int i = 0; i < 9; i++)
            {
                int row = i / 3;
                int col = i % 3;
                JustificationControl jc = new JustificationControl(values[i])
                {
                    Location = new Drawing.Point(startX + col * (itemW + gapX), startY + row * (itemH + gapY)),
                    Size = new Drawing.Size(itemW, itemH),
                    Tag = values[i]
                };
                jc.Click += (s, e) => SetSelection(((JustificationControl)s).Justification);

                // Thêm Tooltip
                WinForms.ToolTip tt = new WinForms.ToolTip();
                tt.SetToolTip(jc, values[i].ToString());

                gbJus.Controls.Add(jc);
                _controls[i] = jc;
            }

            // --- Group Box: Method ---
            WinForms.GroupBox gbMethod = new WinForms.GroupBox
            {
                Text = "Modification Method",
                Location = new Drawing.Point(12, 200),
                Size = new Drawing.Size(220, 65)
            };
            this.Controls.Add(gbMethod);

            _rbVisual = new WinForms.RadioButton
            {
                Text = "Retain visual position",
                Location = new Drawing.Point(15, 18),
                AutoSize = true,
                Checked = defaultRetain
            };
            gbMethod.Controls.Add(_rbVisual);
            _rbInsert = new WinForms.RadioButton
            {
                Text = "Retain insertion point",
                Location = new Drawing.Point(15, 40),
                AutoSize = true,
                Checked = !defaultRetain
            };
            gbMethod.Controls.Add(_rbInsert);
            // --- Buttons ---
            WinForms.Button btnOk = new WinForms.Button
            {
                Text = "OK",
                DialogResult = WinForms.DialogResult.OK,
                Location = new Drawing.Point(47, 275),
                Size = new Drawing.Size(75, 25)
            };
            this.Controls.Add(btnOk);

            WinForms.Button btnCancel = new WinForms.Button
            {
                Text = "Cancel",
                DialogResult = WinForms.DialogResult.Cancel,
                Location = new Drawing.Point(132, 275),
                Size = new Drawing.Size(75, 25)
            };
            this.Controls.Add(btnCancel);
            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            // Kích hoạt lựa chọn ban đầu
            SetSelection(defaultJus);
        }

        private void SetSelection(Justification jus)
        {
            SelectedJustification = jus;
            foreach (var ctrl in _controls)
            {
                ctrl.IsSelected = (ctrl.Justification == jus);
                ctrl.Invalidate(); // Vẽ lại control
            }
        }

        protected override void OnFormClosing(WinForms.FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (this.DialogResult == WinForms.DialogResult.OK)
            {
                RetainVisualPosition = _rbVisual.Checked;
            }
        }
    }
}