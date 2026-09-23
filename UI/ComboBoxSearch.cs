using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace SmartTags.UI
{
    /// <summary>
    /// Type-to-filter search field at the top of a combo box drop-down (<c>ui:ComboBoxSearch.IsEnabled="True"</c> on an
    /// Input.Combo). Non-matching rows are collapsed instead of filtering the items source, so the selected item never
    /// drops out of the collection and saved selections / presets keep working.
    /// Matching: every space-separated term must appear in the item's display text (case-insensitive).
    /// Keys in the field: Enter picks the first match, Down moves into the list, Esc closes.
    /// Handlers are attached per combo box instance; nothing static survives an App Loader reload.
    /// </summary>
    public static class ComboBoxSearch
    {
        public const string SearchBoxPartName = "PART_SearchBox";

        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(ComboBoxSearch), new PropertyMetadata(false, OnIsEnabledChanged));

        public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(ComboBoxSearch), new PropertyMetadata(string.Empty, OnTextChanged));

        public static readonly DependencyProperty HasNoMatchesProperty = DependencyProperty.RegisterAttached(
            "HasNoMatches", typeof(bool), typeof(ComboBoxSearch), new PropertyMetadata(false));

        public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);
        public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);
        public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);
        public static bool GetHasNoMatches(DependencyObject d) => (bool)d.GetValue(HasNoMatchesProperty);
        public static void SetHasNoMatches(DependencyObject d, bool value) => d.SetValue(HasNoMatchesProperty, value);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var combo = d as ComboBox;
            if (combo == null) return;

            combo.DropDownOpened -= OnDropDownOpened;
            combo.DropDownClosed -= OnDropDownClosed;
            if ((bool)e.NewValue)
            {
                combo.DropDownOpened += OnDropDownOpened;
                combo.DropDownClosed += OnDropDownClosed;
            }
        }

        private static void OnDropDownOpened(object sender, EventArgs e)
        {
            var combo = (ComboBox)sender;
            var box = FindSearchBox(combo);
            if (box == null) return;

            box.PreviewKeyDown -= OnSearchKeyDown;
            box.PreviewKeyDown += OnSearchKeyDown;
            SetText(combo, string.Empty);
            // Focus after the popup has opened so typing goes straight into the field.
            combo.Dispatcher.BeginInvoke(new Action(() => box.Focus()), DispatcherPriority.Input);
        }

        private static void OnDropDownClosed(object sender, EventArgs e)
        {
            SetText((ComboBox)sender, string.Empty);
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var combo = d as ComboBox;
            if (combo == null) return;

            var text = (string)e.NewValue;
            SetHasNoMatches(combo, !string.IsNullOrWhiteSpace(text) && !combo.Items.Cast<object>().Any(item => Matches(combo, item, text)));
        }

        private static void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            var box = (TextBox)sender;
            var combo = box.TemplatedParent as ComboBox;
            if (combo == null) return;

            var text = GetText(combo);
            var first = combo.Items.Cast<object>().FirstOrDefault(item => Matches(combo, item, text));

            switch (e.Key)
            {
                case Key.Enter:
                    if (first != null)
                    {
                        combo.SelectedItem = first;
                        combo.IsDropDownOpen = false;
                    }
                    e.Handled = true;
                    break;
                case Key.Down:
                    if (first != null && combo.ItemContainerGenerator.ContainerFromItem(first) is ComboBoxItem container)
                    {
                        container.Focus();
                    }
                    e.Handled = true;
                    break;
                case Key.Escape:
                    combo.IsDropDownOpen = false;
                    combo.Focus();
                    e.Handled = true;
                    break;
            }
        }

        private static TextBox FindSearchBox(ComboBox combo)
        {
            combo.ApplyTemplate();
            return combo.Template?.FindName(SearchBoxPartName, combo) as TextBox;
        }

        /// <summary>True when every space-separated term of <paramref name="text"/> occurs in the item's display text.</summary>
        public static bool Matches(ComboBox combo, object item, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;

            var display = GetDisplayText(combo, item);
            return text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .All(term => display.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        private static string GetDisplayText(ComboBox combo, object item)
        {
            if (item == null) return string.Empty;

            var path = combo?.DisplayMemberPath;
            if (!string.IsNullOrEmpty(path) && path != ".")
            {
                var property = item.GetType().GetProperty(path);
                if (property != null) return property.GetValue(item, null)?.ToString() ?? string.Empty;
            }

            return item.ToString() ?? string.Empty;
        }
    }

    /// <summary>
    /// Collapses a combo box row that does not match the search text.
    /// Values: [0] the item, [1] the search text, [2] the owning ComboBox.
    /// </summary>
    public class ComboBoxSearchVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 3) return Visibility.Visible;

            var text = values[1] as string;
            var combo = values[2] as ComboBox;
            return ComboBoxSearch.Matches(combo, values[0], text) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
