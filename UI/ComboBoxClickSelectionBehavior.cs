using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartTags.UI
{
    public static class ComboBoxClickSelectionBehavior
    {
        public static readonly DependencyProperty EnableClickSelectionProperty =
            DependencyProperty.RegisterAttached(
                "EnableClickSelection",
                typeof(bool),
                typeof(ComboBoxClickSelectionBehavior),
                new PropertyMetadata(false, OnEnableClickSelectionChanged));

        public static void SetEnableClickSelection(DependencyObject element, bool value)
        {
            element.SetValue(EnableClickSelectionProperty, value);
        }

        public static bool GetEnableClickSelection(DependencyObject element)
        {
            return (bool)element.GetValue(EnableClickSelectionProperty);
        }

        private static void OnEnableClickSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ComboBoxItem item)
            {
                if (e.NewValue is bool enabled && enabled)
                {
                    item.PreviewMouseLeftButtonDown += OnItemPreviewMouseLeftButtonDown;
                }
                else
                {
                    item.PreviewMouseLeftButtonDown -= OnItemPreviewMouseLeftButtonDown;
                }
            }
        }

        private static void OnItemPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ComboBoxItem item && item.IsEnabled)
            {
                var comboBox = ItemsControl.ItemsControlFromItemContainer(item) as ComboBox;
                if (comboBox == null)
                {
                    return;
                }

                comboBox.SelectedItem = item.DataContext;
                comboBox.IsDropDownOpen = false;

                item.Focus();
                e.Handled = true;
            }
        }
    }
}
