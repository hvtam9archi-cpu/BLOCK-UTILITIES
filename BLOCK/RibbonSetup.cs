using System;
using System.Windows.Input;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutoCADBlockTools
{
    public class RibbonSetup : IExtensionApplication
    {
        private const string TabId = "TH_TOOLS_TAB";
        private const string TabTitle = "TH Tools";
        private RibbonCommandHandler _cmdHandler = new RibbonCommandHandler();

        public void Initialize()
        {
            Application.Idle += Application_Idle;
            Application.SystemVariableChanged += Application_SystemVariableChanged;
        }

        public void Terminate() 
        {
            Application.Idle -= Application_Idle;
            Application.SystemVariableChanged -= Application_SystemVariableChanged;
        }

        private void Application_Idle(object sender, EventArgs e)
        {
            if (ComponentManager.Ribbon != null)
            {
                Application.Idle -= Application_Idle;
                CreateRibbon();
            }
        }

        private void Application_SystemVariableChanged(object sender, Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs e)
        {
            if (e.Name.Equals("WSCURRENT", StringComparison.OrdinalIgnoreCase) && ComponentManager.Ribbon != null)
            {
                CreateRibbon();
            }
        }

        private void CreateRibbon()
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            // 1. Tìm hoặc Tạo Tab "TH Tools"
            RibbonTab rtb = ribbon.FindTab(TabId);
            if (rtb == null)
            {
                rtb = new RibbonTab { Title = TabTitle, Id = TabId };
                ribbon.Tabs.Add(rtb);
                rtb.IsActive = true;
            }

            // 2. Tìm hoặc Tạo Panel duy nhất "Block Utilities"
            string panelId = "TPL_BLOCK_PANEL";
            bool panelExists = false;
            foreach (RibbonPanel p in rtb.Panels)
            {
                if (p.Source.Id == panelId)
                {
                    panelExists = true;
                    break;
                }
            }

            if (!panelExists)
            {
                RibbonPanelSource rps = new RibbonPanelSource { Title = "Block Utilities", Id = panelId };
                RibbonPanel rp = new RibbonPanel { Source = rps };

                // --- Nhóm 1: Transformation ---
                AddGroupToPanel(rps, new[] {
                    CreateButton("RSET", "Random Settings", "RSET"),
                    CreateButton("RSC", "Random Scale", "RSC"),
                    CreateButton("RRT", "Random Rotate", "RRT"),
                    CreateButton("RAL", "Random Align", "RAL"),
                    CreateButton("RR", "Reset Blocks", "RR"),
                    CreateButton("RB", "Rename Block", "RB")
                });

                rps.Items.Add(new RibbonSeparator());

                // --- Nhóm 2: Management ---
                AddGroupToPanel(rps, new[] {
                    CreateButton("DELB", "Delete Blocks", "DELB"),
                    CreateButton("DLB", "To Layer 0", "DLB"),
                    CreateButton("UDLB", "Undo Layer", "UDLB"),
                    CreateButton("MU", "Make Unique", "MU")
                });

                rps.Items.Add(new RibbonSeparator());

                // --- Nhóm 3: Base Point ---
                AddGroupToPanel(rps, new[] {
                    CreateButton("CB", "Center Base", "CB"),
                    CreateButton("CBP", "Change Base", "CBP"),
                    CreateButton("CBPR", "Change Base (R)", "CBPR"),
                    CreateButton("AB", "Auto Block", "AB"),
                    CreateButton("JBP", "Justify Base", "JBP")
                });

                rtb.Panels.Add(rp);
            }
        }

        private void AddGroupToPanel(RibbonPanelSource rps, RibbonButton[] buttons)
        {
            RibbonRowPanel currentRowPanel = null;
            
            for (int i = 0; i < buttons.Length; i++)
            {
                // Cứ mỗi 3 nút, tạo một cột mới (RibbonRowPanel)
                if (i % 3 == 0)
                {
                    currentRowPanel = new RibbonRowPanel();
                    rps.Items.Add(currentRowPanel);
                }
                
                currentRowPanel.Items.Add(buttons[i]);
                
                // Thêm ngắt dòng (RibbonRowBreak) sau mỗi nút để xếp dọc (trừ nút cuối cùng của cột)
                if (i % 3 != 2 && i != buttons.Length - 1)
                {
                    currentRowPanel.Items.Add(new RibbonRowBreak());
                }
            }
        }

        private RibbonButton CreateButton(string id, string text, string command)
        {
            return new RibbonButton
            {
                Id = id,
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Standard, // Đổi thành cỡ nhỏ để xếp hàng dọc
                Image = GetTextBitmap16(id), // Dùng ảnh 16x16
                CommandParameter = "\x03\x03" + command + " ",
                CommandHandler = _cmdHandler
            };
        }

        // Tạo Icon cỡ nhỏ 16x16
        private System.Windows.Media.ImageSource GetTextBitmap16(string text)
        {
            System.Windows.Media.DrawingVisual visual = new System.Windows.Media.DrawingVisual();
            using (System.Windows.Media.DrawingContext dc = visual.RenderOpen())
            {
                // Nền Accent Color
                dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235)), null, new System.Windows.Rect(0, 0, 16, 16));
                
                // Viền trắng
                dc.DrawRectangle(null, new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 0.5), new System.Windows.Rect(0.5, 0.5, 15, 15));

                System.Windows.Media.FormattedText ft = new System.Windows.Media.FormattedText(
                    text.Length > 2 ? text.Substring(0, 2) : text, // Tối đa 2 ký tự cho 16x16
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    new System.Windows.Media.Typeface(new System.Windows.Media.FontFamily("Segoe UI"), System.Windows.FontStyles.Normal, System.Windows.FontWeights.Bold, System.Windows.FontStretches.Normal),
                    9, // Font siêu nhỏ
                    System.Windows.Media.Brushes.White,
                    1.0); // 1.0 là PixelsPerDip cho .NET 4.6.2+
                
                // Căn giữa text
                dc.DrawText(ft, new System.Windows.Point((16 - ft.Width) / 2, (16 - ft.Height) / 2));
            }
            
            System.Windows.Media.Imaging.RenderTargetBitmap rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(16, 16, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(visual);
            return rtb;
        }
    }

    public class RibbonCommandHandler : ICommand
    {
        public event EventHandler CanExecuteChanged;
        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            string cmd = null;
            if (parameter is RibbonButton btn)
                cmd = btn.CommandParameter as string;
            else if (parameter is string s)
                cmd = s;

            if (!string.IsNullOrEmpty(cmd))
            {
                Autodesk.AutoCAD.ApplicationServices.Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    doc.SendStringToExecute(cmd, true, false, true);
                }
            }
        }
    }
}
