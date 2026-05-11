using System;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;

namespace AutoCADBlockTools
{
    public class RibbonSetup : IExtensionApplication
    {
        public void Initialize()
        {
            // Được gọi khi Plugin được load (NETLOAD)
            // Tạm thời trì hoãn việc tạo Ribbon cho đến khi AutoCAD Idle
            Autodesk.AutoCAD.ApplicationServices.Application.Idle += OnIdle;
        }

        public void Terminate()
        {
            // Clean up
        }

        private void OnIdle(object sender, EventArgs e)
        {
            Autodesk.AutoCAD.ApplicationServices.Application.Idle -= OnIdle;
            CreateRibbon();
        }

        private void CreateRibbon()
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            string tabId = "TPL_BLOCK_TAB";
            RibbonTab rtb = ribbon.FindTab(tabId);
            if (rtb == null)
            {
                rtb = new RibbonTab
                {
                    Title = "TPL Block Utilities",
                    Id = tabId
                };
                ribbon.Tabs.Add(rtb);
            }

            string panelId = "TPL_BLOCK_PANEL";
            RibbonPanelSource rps = new RibbonPanelSource
            {
                Title = "Block Tools",
                Id = panelId
            };

            RibbonPanel rp = new RibbonPanel
            {
                Source = rps
            };

            // Tránh thêm Panel trùng lặp
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
                rtb.Panels.Add(rp);
                
                // Add some buttons as an example
                RibbonButton btnJbp = new RibbonButton
                {
                    Text = "Justify Base Point",
                    ShowText = true,
                    CommandParameter = "JBP ",
                    CommandHandler = new RibbonCommandHandler()
                };
                
                RibbonButton btnSet = new RibbonButton
                {
                    Text = "Random Settings",
                    ShowText = true,
                    CommandParameter = "RSET ",
                    CommandHandler = new RibbonCommandHandler()
                };

                rps.Items.Add(btnJbp);
                rps.Items.Add(new RibbonRowBreak());
                rps.Items.Add(btnSet);
            }

            rtb.IsActive = true;
        }
    }

    public class RibbonCommandHandler : System.Windows.Input.ICommand
    {
        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return true;
        }

        public void Execute(object parameter)
        {
            Autodesk.AutoCAD.ApplicationServices.Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc != null && parameter is string cmd)
            {
                doc.SendStringToExecute(cmd, true, false, true);
            }
        }
    }
}
