using System.Windows;
using System.Windows.Input;

namespace AutoCADBlockTools.UI
{
	public partial class RandomSettingsWindow : Window
	{
		public double MinScale { get; private set; }
		public double MaxScale { get; private set; }
		public double MinAngle { get; private set; }
		public double MaxAngle { get; private set; }

		public RandomSettingsWindow(double minS, double maxS, double minA, double maxA)
		{
			InitializeComponent();
			txtMinScale.Text = minS.ToString("0.00");
			txtMaxScale.Text = maxS.ToString("0.00");
			txtMinAngle.Text = minA.ToString("0");
			txtMaxAngle.Text = maxA.ToString("0");
		}

		private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			if (e.ButtonState == MouseButtonState.Pressed)
				DragMove();
		}

		private void BtnOk_Click(object sender, RoutedEventArgs e)
		{
			if (double.TryParse(txtMinScale.Text, out double minS) &&
				double.TryParse(txtMaxScale.Text, out double maxS) &&
				double.TryParse(txtMinAngle.Text, out double minA) &&
				double.TryParse(txtMaxAngle.Text, out double maxA))
			{
				MinScale = minS;
				MaxScale = maxS;
				MinAngle = minA;
				MaxAngle = maxA;
				DialogResult = true;
				Close();
			}
			else
			{
				MessageBox.Show("Please enter valid numeric values.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		}

		private void BtnCancel_Click(object sender, RoutedEventArgs e)
		{
			DialogResult = false;
			Close();
		}
	}
}
