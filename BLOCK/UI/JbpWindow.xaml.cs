using System.Windows;
using System.Windows.Input;

namespace AutoCADBlockTools.UI
{
	public partial class JbpWindow : Window
	{
		public Justification SelectedJustification { get; private set; }
		public bool RetainVisualPosition { get; private set; }

		public JbpWindow(Justification defaultJus, bool defaultRetain)
		{
			InitializeComponent();

			rbVisual.IsChecked = defaultRetain;
			rbInsert.IsChecked = !defaultRetain;

			// Set default justification
			switch (defaultJus)
			{
				case Justification.TopLeft: rbTL.IsChecked = true; break;
				case Justification.TopCenter: rbTC.IsChecked = true; break;
				case Justification.TopRight: rbTR.IsChecked = true; break;
				case Justification.MiddleLeft: rbML.IsChecked = true; break;
				case Justification.MiddleCenter: rbMC.IsChecked = true; break;
				case Justification.MiddleRight: rbMR.IsChecked = true; break;
				case Justification.BottomLeft: rbBL.IsChecked = true; break;
				case Justification.BottomCenter: rbBC.IsChecked = true; break;
				case Justification.BottomRight: rbBR.IsChecked = true; break;
				default: rbBL.IsChecked = true; break;
			}
		}

		private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			if (e.ButtonState == MouseButtonState.Pressed)
				DragMove();
		}

		private void BtnOk_Click(object sender, RoutedEventArgs e)
		{
			if (rbTL.IsChecked == true) SelectedJustification = Justification.TopLeft;
			else if (rbTC.IsChecked == true) SelectedJustification = Justification.TopCenter;
			else if (rbTR.IsChecked == true) SelectedJustification = Justification.TopRight;
			else if (rbML.IsChecked == true) SelectedJustification = Justification.MiddleLeft;
			else if (rbMC.IsChecked == true) SelectedJustification = Justification.MiddleCenter;
			else if (rbMR.IsChecked == true) SelectedJustification = Justification.MiddleRight;
			else if (rbBL.IsChecked == true) SelectedJustification = Justification.BottomLeft;
			else if (rbBC.IsChecked == true) SelectedJustification = Justification.BottomCenter;
			else if (rbBR.IsChecked == true) SelectedJustification = Justification.BottomRight;
			else SelectedJustification = Justification.BottomLeft;

			RetainVisualPosition = rbVisual.IsChecked == true;

			DialogResult = true;
			Close();
		}

		private void BtnCancel_Click(object sender, RoutedEventArgs e)
		{
			DialogResult = false;
			Close();
		}
	}
}
