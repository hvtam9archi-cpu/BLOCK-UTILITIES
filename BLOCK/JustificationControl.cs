using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace AutoCADBlockTools
{
    public class JustificationControl : WinForms.Control
    {
        public Justification Justification { get; set; }
        public bool IsSelected { get; set; }

        private const int BoxMargin = 6;
        public JustificationControl(Justification jus)
        {
            this.Justification = jus;
            this.DoubleBuffered = true; // Chống nhấp nháy
            this.Cursor = Cursors.Hand;
            this.Size = new Drawing.Size(50, 40);
            // Kích thước chữ nhật giống icon trong Lisp
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Drawing.Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // 1. Vẽ nền (Background)
            Drawing.Color backColor = IsSelected ? Drawing.Color.FromArgb(200, 230, 255) : System.Drawing.SystemColors.Control;
            Drawing.Color borderColor = IsSelected ?
            Drawing.Color.DodgerBlue : Drawing.Color.Gray;

            using (Drawing.SolidBrush bgBrush = new Drawing.SolidBrush(backColor))
            {
                g.FillRectangle(bgBrush, this.ClientRectangle);
            }

            // Vẽ viền ngoài của nút
            using (Drawing.Pen borderPen = new Drawing.Pen(borderColor, IsSelected ? 2 : 1))
            {
                Drawing.Rectangle borderRect = this.ClientRectangle;
                borderRect.Width -= 1;
                borderRect.Height -= 1;
                g.DrawRectangle(borderPen, borderRect);
            }

            // 2. Vẽ hình chữ nhật đại diện Block (Màu trắng, viền đen)
            Drawing.Rectangle boxRect = new Drawing.Rectangle(BoxMargin, BoxMargin, this.Width - 2 * BoxMargin, this.Height - 2 * BoxMargin);
            using (Drawing.SolidBrush whiteBrush = new Drawing.SolidBrush(Drawing.Color.White))
            using (Drawing.Pen blackPen = new Drawing.Pen(Drawing.Color.Black, 1.5f))
            {
                g.FillRectangle(whiteBrush, boxRect);
                g.DrawRectangle(blackPen, boxRect);
            }

            // 3. Vẽ điểm chèn (Chấm đỏ)
            float dotX = 0, dotY = 0;
            string jusStr = Justification.ToString();
            // X position
            if (jusStr.Contains("Left")) dotX = boxRect.Left;
            else if (jusStr.Contains("Center")) dotX = boxRect.Left + boxRect.Width / 2.0f;
            else if (jusStr.Contains("Right")) dotX = boxRect.Right;

            // Y position
            if (jusStr.Contains("Top")) dotY = boxRect.Top;
            else if (jusStr.Contains("Middle")) dotY = boxRect.Top + boxRect.Height / 2.0f;
            else if (jusStr.Contains("Bottom")) dotY = boxRect.Bottom;
            // Vẽ chấm đỏ
            float r = 3.5f; // Bán kính chấm
            using (Drawing.SolidBrush redBrush = new Drawing.SolidBrush(Drawing.Color.Red))
            {
                g.FillEllipse(redBrush, dotX - r, dotY - r, r * 2, r * 2);
            }

            // Vẽ viền nhẹ cho chấm đỏ
            using (Drawing.Pen darkRedPen = new Drawing.Pen(Drawing.Color.DarkRed, 1))
            {
                g.DrawEllipse(darkRedPen, dotX - r, dotY - r, r * 2, r * 2);
            }
        }
    }
}