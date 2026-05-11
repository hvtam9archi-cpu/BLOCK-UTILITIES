using System;
using System.Windows;
using System.Windows.Input;

namespace AutoCADBlockTools.UI
{
    public partial class RenameBlockWindow : Window
    {
        public string ResultName { get; private set; }
        private readonly string _currentName;

        public RenameBlockWindow(string currentName)
        {
            InitializeComponent();
            _currentName = currentName;
            txtCurrentName.Text = $"Current Name: {_currentName}";
            txtNewName.Text = _currentName;
            txtNewName.Focus();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void ChkRandom_Checked(object sender, RoutedEventArgs e)
        {
            string ticks = DateTime.Now.Ticks.ToString();
            txtNewName.Text = ticks.Length > 16 ? $"@{ticks.Substring(0, 16)}" : $"@{ticks}";
            txtNewName.IsEnabled = false;
        }

        private void ChkRandom_Unchecked(object sender, RoutedEventArgs e)
        {
            txtNewName.Text = _currentName;
            txtNewName.IsEnabled = true;
            txtNewName.Focus();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtNewName.Text))
            {
                MessageBox.Show("Please enter a valid name.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ResultName = txtNewName.Text.Trim();
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
