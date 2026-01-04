using System.Windows;
using System.Windows.Input;

namespace SmartTags.UI
{
    public partial class PresetNameDialog : Window
    {
        public string PresetName { get; private set; }

        public PresetNameDialog()
        {
            InitializeComponent();
            PresetNameTextBox.Focus();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var name = PresetNameTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Please enter a preset name.", "SmartTags", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PresetName = name;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
