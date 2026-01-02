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
                    item.PreviewMouseLeftButtonUp += OnItemPreviewMouseLeftButtonUp;
                }
                else
                {
                    item.PreviewMouseLeftButtonUp -= OnItemPreviewMouseLeftButtonUp;
                }
            }
        }

        private static void OnItemPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is ComboBoxItem item)
            {
                var comboBox = ItemsControl.ItemsControlFromItemContainer(item) as ComboBox;
                if (comboBox == null)
                {
                    return;
                }

                if (!item.IsSelected)
                {
                    comboBox.SelectedItem = item.DataContext;
                }

                comboBox.IsDropDownOpen = false;
                e.Handled = true;
            }
        }
    }
}
