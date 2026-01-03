using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MaterialDesignThemes.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SmartTags.ExternalEvents;

namespace SmartTags.UI
{
    public partial class TagPlacementWindow : Window
    {
        private const string ConfigFilePath = @"C:\ProgramData\RK Tools\SmartTags\config.json";
        private const string WindowLeftKey = "TagPlacementWindow.Left";
        private const string WindowTopKey = "TagPlacementWindow.Top";
        private const string WindowWidthKey = "TagPlacementWindow.Width";
        private const string WindowHeightKey = "TagPlacementWindow.Height";
        private const string SelectedCategoryKey = "TagPlacementWindow.SelectedCategoryId";
        private const string LeaderLengthKey = "TagPlacementWindow.LeaderLength";
        private const string AngleKey = "TagPlacementWindow.Angle";
        private const string PlacementDirectionKey = "TagPlacementWindow.PlacementDirection";
        private const string CollisionDetectionEnabledKey = "TagPlacementWindow.CollisionDetectionEnabled";
        private const string CollisionGapKey = "TagPlacementWindow.CollisionGap";
        private const string MinimumOffsetKey = "TagPlacementWindow.MinimumOffset";
        private const string RetagExecutionModeKey = "TagPlacementWindow.RetagExecutionMode";
        private const string DirectionKeywordKey = "TagPlacementWindow.DirectionKeyword";

        private readonly UIApplication _uiApplication;
        private readonly WindowResizer _windowResizer;
        private readonly Dictionary<ElementId, List<TagTypeOption>> _tagTypesByCategory = new Dictionary<ElementId, List<TagTypeOption>>();
        private bool _isDarkMode = true;
        private ResourceDictionary _currentThemeDictionary;
        private readonly TagPlacementHandler _tagPlacementHandler;
        private readonly ExternalEvent _tagPlacementExternalEvent;
        private readonly RetagApplyHandler _retagApplyHandler;
        private readonly ExternalEvent _retagApplyExternalEvent;
        private readonly RetagConfirmationHandler _retagConfirmationHandler;
        private readonly ExternalEvent _retagConfirmationExternalEvent;
        private readonly ActiveSelectionTagHandler _activeSelectionHandler;
        private readonly ExternalEvent _activeSelectionExternalEvent;
        private bool _isUpdatingPlacementDirection;
        private bool _isUpdatingRetagMode;
        private bool _isActiveSelectionModeActive;
        private ElementId _activeSelectionViewId;

        public ObservableCollection<TagCategoryOption> TagCategories { get; } = new ObservableCollection<TagCategoryOption>();
        public ObservableCollection<TagTypeOption> TagTypes { get; } = new ObservableCollection<TagTypeOption>();
        public ObservableCollection<TagTypeOption> LeftTagTypes { get; } = new ObservableCollection<TagTypeOption>();
        public ObservableCollection<TagTypeOption> RightTagTypes { get; } = new ObservableCollection<TagTypeOption>();
        public ObservableCollection<TagTypeOption> UpTagTypes { get; } = new ObservableCollection<TagTypeOption>();
        public ObservableCollection<TagTypeOption> DownTagTypes { get; } = new ObservableCollection<TagTypeOption>();
        public ObservableCollection<LeaderTypeOption> LeaderTypes { get; } = new ObservableCollection<LeaderTypeOption>();
        public ObservableCollection<OrientationOption> OrientationOptions { get; } = new ObservableCollection<OrientationOption>();

        public TagPlacementWindow(UIApplication app)
        {
            _uiApplication = app;
            InitializeComponent();

            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _windowResizer = new WindowResizer(this);
            Closed += TagPlacementWindow_Closed;

            MouseMove += Window_MouseMove;
            MouseLeftButtonUp += Window_MouseLeftButtonUp;

            DataContext = this;

            _tagPlacementHandler = new TagPlacementHandler();
            _tagPlacementExternalEvent = ExternalEvent.Create(_tagPlacementHandler);

            _retagApplyHandler = new RetagApplyHandler();
            _retagApplyExternalEvent = ExternalEvent.Create(_retagApplyHandler);

            _retagConfirmationHandler = new RetagConfirmationHandler();
            _retagConfirmationExternalEvent = ExternalEvent.Create(_retagConfirmationHandler);

            _activeSelectionHandler = new ActiveSelectionTagHandler();
            _activeSelectionExternalEvent = ExternalEvent.Create(_activeSelectionHandler);

            LoadThemeState();
            LoadWindowState();
            LoadLeaderSettings();
            LoadPlacementDirection();
            LoadCollisionSettings();
            LoadRetagExecutionMode();
            LoadDirectionKeyword();

            InitializeLeaderOptions();
            InitializeOrientationOptions();
            LoadTagOptions(app.ActiveUIDocument?.Document);
            UpdateLeaderInputs();
        }

        private void InitializeLeaderOptions()
        {
            LeaderTypes.Clear();
            LeaderTypes.Add(new LeaderTypeOption("Attached end", true));
            LeaderTypes.Add(new LeaderTypeOption("Free end", false));
            LeaderTypeComboBox.SelectedIndex = 0;
        }

        private void InitializeOrientationOptions()
        {
            OrientationOptions.Clear();
            OrientationOptions.Add(new OrientationOption("Horizontal", TagOrientation.Horizontal));
            OrientationOptions.Add(new OrientationOption("Vertical", TagOrientation.Vertical));
            OrientationOptions.Add(new OrientationOption("Model", TagOrientation.Horizontal));
            OrientationComboBox.SelectedIndex = 0;
        }

        private void LoadTagOptions(Document doc)
        {
            TagCategories.Clear();
            TagTypes.Clear();
            _tagTypesByCategory.Clear();

            if (doc == null)
            {
                return;
            }

            var tagSymbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(symbol => symbol.Category != null && IsTagCategory(symbol.Category))
                .ToList();

            var grouped = tagSymbols
                .GroupBy(symbol => symbol.Category.Id)
                .OrderBy(group => group.First().Category.Name);

            foreach (var group in grouped)
            {
                var category = group.First().Category;
                var elementCategory = FindTaggedCategory(doc, category.Name);
                var displayName = elementCategory != null ? elementCategory.Name : ToElementCategoryName(category.Name);
                var elementCategoryId = elementCategory != null ? elementCategory.Id : ElementId.InvalidElementId;
                TagCategories.Add(new TagCategoryOption(category.Id, elementCategoryId, displayName, category.Name));

                var types = group
                    .OrderBy(symbol => symbol.Family?.Name)
                    .ThenBy(symbol => symbol.Name)
                    .Select(symbol => new TagTypeOption(symbol.Id, symbol.Family?.Name ?? string.Empty, symbol.Name))
                    .ToList();

                _tagTypesByCategory[category.Id] = types;
            }

            ApplySavedCategorySelection();
        }

        private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var option = e.AddedItems.Count > 0 ? e.AddedItems[0] as TagCategoryOption : null;
            TagTypes.Clear();

            if (option != null && _tagTypesByCategory.TryGetValue(option.TagCategoryId, out var types))
            {
                foreach (var type in types)
                {
                    TagTypes.Add(type);
                }
            }

            TagTypeComboBox.SelectedIndex = TagTypes.Count > 0 ? 0 : -1;
        }

        private void LeaderLineCheckBox_Toggled(object sender, RoutedEventArgs e)
        {
            if (LeaderLineCheckBox == null || LeaderLengthTextBox == null || LeaderTypeComboBox == null)
            {
                return;
            }

            UpdateLeaderInputs();
        }

        private void UpdateLeaderInputs()
        {
            if (LeaderLineCheckBox == null || LeaderLengthTextBox == null || LeaderTypeComboBox == null)
            {
                return;
            }

            var enabled = LeaderLineCheckBox.IsChecked == true;
            LeaderLengthTextBox.IsEnabled = enabled;
            LeaderTypeComboBox.IsEnabled = enabled;
            var opacity = enabled ? 1.0 : 0.5;
            LeaderLengthTextBox.Opacity = opacity;
        }

        private void Background_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null)
            {
                if (dep is GroupBox)
                {
                    return;
                }

                dep = VisualTreeHelper.GetParent(dep);
            }

            Keyboard.ClearFocus();
        }

        private void PlacementDirection_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingPlacementDirection)
            {
                return;
            }

            _isUpdatingPlacementDirection = true;

            var current = sender as CheckBox;
            var checkBoxes = new[]
            {
                PlacementUpCheckBox,
                PlacementRightCheckBox,
                PlacementDownCheckBox,
                PlacementLeftCheckBox
            };

            foreach (var checkBox in checkBoxes)
            {
                if (checkBox == null || checkBox == current)
                {
                    continue;
                }

                checkBox.IsChecked = false;
            }

            _isUpdatingPlacementDirection = false;
        }

        private void SetPlacementDirection(PlacementDirection direction)
        {
            if (PlacementUpCheckBox == null || PlacementDownCheckBox == null || PlacementLeftCheckBox == null || PlacementRightCheckBox == null)
            {
                return;
            }

            _isUpdatingPlacementDirection = true;
            PlacementUpCheckBox.IsChecked = direction == PlacementDirection.Up;
            PlacementDownCheckBox.IsChecked = direction == PlacementDirection.Down;
            PlacementLeftCheckBox.IsChecked = direction == PlacementDirection.Left;
            PlacementRightCheckBox.IsChecked = direction == PlacementDirection.Right;
            _isUpdatingPlacementDirection = false;
        }

        private void TagAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryConfigureTagPlacement(false))
            {
                return;
            }

            _tagPlacementExternalEvent.Raise();
        }

        private void TagSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryConfigureTagPlacement(true))
            {
                return;
            }

            _tagPlacementExternalEvent.Raise();
        }

        private bool TryConfigureTagPlacement(bool useSelection)
        {
            if (_uiApplication?.ActiveUIDocument == null)
            {
                MessageBox.Show("Open a document before placing tags.", "SmartTags", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var uiDoc = _uiApplication.ActiveUIDocument;
            var doc = uiDoc.Document;

            var categoryOption = CategoryComboBox?.SelectedItem as TagCategoryOption;
            if (categoryOption == null)
            {
                MessageBox.Show("Select a category to tag.", "SmartTags", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (categoryOption.ElementCategoryId == ElementId.InvalidElementId)
            {
                MessageBox.Show("No matching element category found for this tag.", "SmartTags", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var tagTypeOption = TagTypeComboBox?.SelectedItem as TagTypeOption;
            if (tagTypeOption == null)
            {
                MessageBox.Show("Select a tag type.", "SmartTags", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var hasLeader = LeaderLineCheckBox?.IsChecked == true;
            double leaderLength = 0;
            double attachedLength = 0;
            double freeLength = 0;
            if (hasLeader)
            {
                if (!TryParseLength(LeaderLengthTextBox?.Text, out leaderLength, out var lengthError))
                {
                    MessageBox.Show(lengthError, "SmartTags", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            double angleRadians;
            if (!TryParseAngle(AngleTextBox?.Text, out angleRadians, out var angleError))
            {
                MessageBox.Show(angleError, "SmartTags", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var orientationOption = OrientationComboBox?.SelectedItem as OrientationOption;
            var orientation = orientationOption != null ? orientationOption.Orientation : TagOrientation.Horizontal;

            _tagPlacementHandler.CategoryId = categoryOption.ElementCategoryId;
            _tagPlacementHandler.TagTypeId = tagTypeOption.TypeId;
            _tagPlacementHandler.HasLeader = hasLeader;
            _tagPlacementHandler.Direction = GetPlacementDirection();
            _tagPlacementHandler.DetectElementRotation = DetectRotationCheckBox?.IsChecked == true;

            var leaderType = LeaderTypeComboBox?.SelectedItem as LeaderTypeOption;
            var applyToAttached = leaderType == null || leaderType.IsAttachedEnd;
            if (hasLeader)
            {
                attachedLength = applyToAttached ? leaderLength : 0;
                freeLength = applyToAttached ? 0 : leaderLength;
            }

            _tagPlacementHandler.AttachedLength = attachedLength;
            _tagPlacementHandler.FreeLength = freeLength;
            _tagPlacementHandler.Orientation = orientation;
            _tagPlacementHandler.Angle = angleRadians;

            // Configure collision detection
            _tagPlacementHandler.EnableCollisionDetection = CollisionDetectionCheckBox?.IsChecked == true;

            if (_tagPlacementHandler.EnableCollisionDetection)
            {
                if (!TryParseCollisionGap(CollisionGapTextBox?.Text, out var gapMm, out var gapError))
                {
                    MessageBox.Show(gapError, "SmartTags", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                _tagPlacementHandler.CollisionGapMillimeters = gapMm;
            }

            // Configure minimum offset (when leader is disabled)
            if (!TryParseLength(MinimumOffsetTextBox?.Text, out var minimumOffsetMm, out var offsetError))
            {
                MessageBox.Show(offsetError, "SmartTags", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _tagPlacementHandler.MinimumOffsetMillimeters = minimumOffsetMm;

            if (useSelection)
            {
                var selectionIds = uiDoc.Selection.GetElementIds();
                if (selectionIds == null || selectionIds.Count == 0)
                {
                    MessageBox.Show("Select elements in the active view to tag.", "SmartTags", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }

                var filteredIds = new List<ElementId>();
                foreach (var id in selectionIds)
                {
                    var element = doc.GetElement(id);
                    if (element?.Category == null)
                    {
                        continue;
                    }

                    if (element.Category.Id == categoryOption.ElementCategoryId)
                    {
                        filteredIds.Add(id);
                    }
                }

                if (filteredIds.Count == 0)
                {
                    MessageBox.Show("No selected elements match the chosen category.", "SmartTags", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }

                _tagPlacementHandler.UseSelection = true;
                _tagPlacementHandler.TargetElementIds = filteredIds;
            }
            else
            {
                _tagPlacementHandler.UseSelection = false;
                _tagPlacementHandler.TargetElementIds = null;
            }

            _tagPlacementHandler.DirectionResolver = GetDirectionResolver();

            return true;
        }

        private PlacementDirection GetPlacementDirection()
        {
            if (PlacementUpCheckBox?.IsChecked == true)
            {
                return PlacementDirection.Up;
            }

            if (PlacementDownCheckBox?.IsChecked == true)
            {
                return PlacementDirection.Down;
            }

            if (PlacementLeftCheckBox?.IsChecked == true)
            {
                return PlacementDirection.Left;
            }

            if (PlacementRightCheckBox?.IsChecked == true)
            {
                return PlacementDirection.Right;
            }

            return PlacementDirection.Right;
        }

        private bool TryParseLength(string text, out double length, out string error)
        {
            length = 0;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            var units = _uiApplication?.ActiveUIDocument?.Document?.GetUnits();
            if (units == null)
            {
                error = "Unable to read document units.";
                return false;
            }

            if (!UnitFormatUtils.TryParse(units, SpecTypeId.Length, text, out length))
            {
                error = $"Invalid length value: {text}";
                return false;
            }

            return true;
        }

        private bool TryParseAngle(string text, out double angle, out string error)
        {
            angle = 0;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            var units = _uiApplication?.ActiveUIDocument?.Document?.GetUnits();
            if (units == null)
            {
                error = "Unable to read document units.";
                return false;
            }

            if (!UnitFormatUtils.TryParse(units, SpecTypeId.Angle, text, out angle))
            {
                error = $"Invalid angle value: {text}";
                return false;
            }

            return true;
        }

        private static bool IsTagCategory(Category category)
        {
            if (category == null)
            {
                return false;
            }

            if (category.CategoryType != CategoryType.Annotation)
            {
                return false;
            }

            var parent = category.Parent;
            if (parent != null)
            {
                var parentId = GetElementIdValue(parent.Id);
                if (parentId == (long)BuiltInCategory.OST_Tags)
                {
                    return true;
                }
            }

            var name = category.Name ?? string.Empty;
            return name.EndsWith(" Tags", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(" Tag", StringComparison.OrdinalIgnoreCase);
        }

        private static string ToElementCategoryName(string tagCategoryName)
        {
            if (string.IsNullOrWhiteSpace(tagCategoryName))
            {
                return tagCategoryName;
            }

            var name = tagCategoryName.Trim();
            if (name.EndsWith(" Tags", StringComparison.OrdinalIgnoreCase))
            {
                return name.Substring(0, name.Length - 5);
            }

            if (name.EndsWith(" Tag", StringComparison.OrdinalIgnoreCase))
            {
                return name.Substring(0, name.Length - 4);
            }

            return name;
        }

        private static Category FindTaggedCategory(Document doc, string tagCategoryName)
        {
            if (doc == null || string.IsNullOrWhiteSpace(tagCategoryName))
            {
                return null;
            }

            var baseName = ToElementCategoryName(tagCategoryName);
            var categories = doc.Settings?.Categories;
            if (categories == null)
            {
                return null;
            }

            var candidates = categories
                .Cast<Category>()
                .Where(cat => cat.CategoryType != CategoryType.Annotation)
                .ToList();

            var match = candidates
                .FirstOrDefault(cat => string.Equals(cat.Name, baseName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }

            if (!baseName.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            {
                match = candidates
                    .FirstOrDefault(cat => string.Equals(cat.Name, baseName + "s", StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match;
                }
            }

            if (!baseName.EndsWith("es", StringComparison.OrdinalIgnoreCase))
            {
                match = candidates
                    .FirstOrDefault(cat => string.Equals(cat.Name, baseName + "es", StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match;
                }
            }

            return candidates
                .FirstOrDefault(cat => cat.Name.IndexOf(baseName, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static long GetElementIdValue(ElementId id)
        {
#if NET8_0_OR_GREATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }

        private void TitleBar_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private void LoadTheme()
        {
            try
            {
                var themeUri = new Uri(_isDarkMode
                    ? "pack://application:,,,/SmartTags;component/UI/Themes/DarkTheme.xaml"
                    : "pack://application:,,,/SmartTags;component/UI/Themes/LightTheme.xaml", UriKind.Absolute);

                var newDict = new ResourceDictionary { Source = themeUri };

                if (_currentThemeDictionary != null)
                {
                    Resources.MergedDictionaries.Remove(_currentThemeDictionary);
                }

                Resources.MergedDictionaries.Add(newDict);
                _currentThemeDictionary = newDict;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading theme: {ex.Message}");
            }
        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            _isDarkMode = ThemeToggleButton.IsChecked == true;
            LoadTheme();
            SaveThemeState();

            var icon = ThemeToggleButton?.Template?.FindName("ThemeToggleIcon", ThemeToggleButton)
                       as PackIcon;
            if (icon != null)
            {
                icon.Kind = _isDarkMode
                    ? PackIconKind.ToggleSwitchOffOutline
                    : PackIconKind.ToggleSwitchOutline;
            }
        }

        private void LoadThemeState()
        {
            try
            {
                var config = LoadConfig();
                if (TryGetBool(config, "IsDarkMode", out var isDark))
                {
                    _isDarkMode = isDark;
                }
            }
            catch (Exception)
            {
            }

            if (ThemeToggleButton != null)
            {
                ThemeToggleButton.IsChecked = _isDarkMode;
                var icon = ThemeToggleButton.Template?.FindName("ThemeToggleIcon", ThemeToggleButton)
                           as PackIcon;
                if (icon != null)
                {
                    icon.Kind = _isDarkMode
                        ? PackIconKind.ToggleSwitchOffOutline
                        : PackIconKind.ToggleSwitchOutline;
                }
            }

            LoadTheme();
        }

        private void SaveThemeState()
        {
            try
            {
                var config = LoadConfig();
                config["IsDarkMode"] = _isDarkMode;
                SaveConfig(config);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LeftEdge_MouseEnter(object sender, MouseEventArgs e) => Cursor = Cursors.SizeWE;
        private void RightEdge_MouseEnter(object sender, MouseEventArgs e) => Cursor = Cursors.SizeWE;
        private void BottomEdge_MouseEnter(object sender, MouseEventArgs e) => Cursor = Cursors.SizeNS;
        private void Edge_MouseLeave(object sender, MouseEventArgs e) => Cursor = Cursors.Arrow;
        private void BottomLeftCorner_MouseEnter(object sender, MouseEventArgs e) => Cursor = Cursors.SizeNESW;
        private void BottomRightCorner_MouseEnter(object sender, MouseEventArgs e) => Cursor = Cursors.SizeNWSE;

        private void Window_MouseMove(object sender, MouseEventArgs e) => _windowResizer.ResizeWindow(e);
        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _windowResizer.StopResizing();
        private void LeftEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.Left);
        private void RightEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.Right);
        private void BottomEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.Bottom);
        private void BottomLeftCorner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.BottomLeft);
        private void BottomRightCorner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.BottomRight);

        private void TagPlacementWindow_Closed(object sender, EventArgs e)
        {
            SaveSelectedCategory();
            SaveLeaderSettings();
            SavePlacementDirection();
            SaveCollisionSettings();
            SaveRetagExecutionMode();
            SaveWindowState();
        }

        private void LoadWindowState()
        {
            try
            {
                var config = LoadConfig();
                bool hasLeft = TryGetDouble(config, WindowLeftKey, out var left);
                bool hasTop = TryGetDouble(config, WindowTopKey, out var top);
                bool hasWidth = TryGetDouble(config, WindowWidthKey, out var width);
                bool hasHeight = TryGetDouble(config, WindowHeightKey, out var height);

                bool hasSize = hasWidth && hasHeight && width > 0 && height > 0;
                bool hasPos = hasLeft && hasTop && !double.IsNaN(left) && !double.IsNaN(top);

                if (!hasSize && !hasPos)
                {
                    return;
                }

                WindowStartupLocation = WindowStartupLocation.Manual;

                if (hasSize)
                {
                    Width = Math.Max(MinWidth, width);
                    Height = Math.Max(MinHeight, height);
                }

                if (hasPos)
                {
                    Left = left;
                    Top = top;
                }
            }
            catch (Exception)
            {
            }
        }

        private void SaveWindowState()
        {
            try
            {
                var config = LoadConfig();
                var bounds = WindowState == WindowState.Normal
                    ? new Rect(Left, Top, Width, Height)
                    : RestoreBounds;

                config[WindowLeftKey] = bounds.Left;
                config[WindowTopKey] = bounds.Top;
                config[WindowWidthKey] = bounds.Width;
                config[WindowHeightKey] = bounds.Height;

                SaveConfig(config);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save window state: {ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Dictionary<string, object> LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var json = File.ReadAllText(ConfigFilePath);
                    var config = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    if (config != null)
                    {
                        return config;
                    }
                }
            }
            catch (Exception)
            {
            }

            return new Dictionary<string, object>();
        }

        private void SaveConfig(Dictionary<string, object> config)
        {
            var dir = Path.GetDirectoryName(ConfigFilePath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(ConfigFilePath, JsonConvert.SerializeObject(config, Formatting.Indented));
        }

        private void LoadLeaderSettings()
        {
            if (LeaderLengthTextBox == null || AngleTextBox == null)
            {
                return;
            }

            var config = LoadConfig();

            if (TryGetString(config, LeaderLengthKey, out var leaderLength))
            {
                LeaderLengthTextBox.Text = string.IsNullOrWhiteSpace(leaderLength) ? "0" : leaderLength;
            }
            else
            {
                LeaderLengthTextBox.Text = "0";
            }

            if (TryGetString(config, AngleKey, out var angle))
            {
                AngleTextBox.Text = string.IsNullOrWhiteSpace(angle) ? "0" : angle;
            }
            else
            {
                AngleTextBox.Text = "0";
            }
        }

        private void SaveLeaderSettings()
        {
            if (LeaderLengthTextBox == null || AngleTextBox == null)
            {
                return;
            }

            try
            {
                var config = LoadConfig();
                var leaderLength = string.IsNullOrWhiteSpace(LeaderLengthTextBox.Text) ? "0" : LeaderLengthTextBox.Text.Trim();
                var angle = string.IsNullOrWhiteSpace(AngleTextBox.Text) ? "0" : AngleTextBox.Text.Trim();

                config[LeaderLengthKey] = leaderLength;
                config[AngleKey] = angle;
                SaveConfig(config);
            }
            catch (Exception)
            {
            }
        }

        private void LoadPlacementDirection()
        {
            if (PlacementUpCheckBox == null || PlacementDownCheckBox == null || PlacementLeftCheckBox == null || PlacementRightCheckBox == null)
            {
                return;
            }

            var config = LoadConfig();
            PlacementDirection direction = PlacementDirection.Right;

            if (TryGetString(config, PlacementDirectionKey, out var value))
            {
                PlacementDirection parsed;
                if (Enum.TryParse(value, true, out parsed))
                {
                    direction = parsed;
                }
            }

            SetPlacementDirection(direction);
        }

        private void SavePlacementDirection()
        {
            if (PlacementUpCheckBox == null || PlacementDownCheckBox == null || PlacementLeftCheckBox == null || PlacementRightCheckBox == null)
            {
                return;
            }

            try
            {
                var config = LoadConfig();
                config[PlacementDirectionKey] = GetPlacementDirection().ToString();
                SaveConfig(config);
            }
            catch (Exception)
            {
            }
        }

        private void ApplySavedCategorySelection()
        {
            if (TagCategories.Count == 0)
            {
                return;
            }

            var savedCategoryId = GetSavedCategoryId();
            if (savedCategoryId.HasValue)
            {
                var matched = TagCategories.FirstOrDefault(option =>
                    GetElementIdValue(option.TagCategoryId) == savedCategoryId.Value);
                if (matched != null)
                {
                    CategoryComboBox.SelectedItem = matched;
                    return;
                }
            }

            CategoryComboBox.SelectedIndex = 0;
        }

        private long? GetSavedCategoryId()
        {
            var config = LoadConfig();
            if (TryGetLong(config, SelectedCategoryKey, out var saved))
            {
                return saved;
            }

            return null;
        }

        private void SaveSelectedCategory()
        {
            TagCategoryOption option = null;
            if (CategoryComboBox != null)
            {
                option = CategoryComboBox.SelectedItem as TagCategoryOption;
            }

            if (option == null)
            {
                return;
            }

            try
            {
                var config = LoadConfig();
                config[SelectedCategoryKey] = GetElementIdValue(option.TagCategoryId);
                SaveConfig(config);
            }
            catch (Exception)
            {
            }
        }

        private void CollisionDetectionCheckBox_Toggled(object sender, RoutedEventArgs e)
        {
            if (CollisionGapTextBox != null)
            {
                var enabled = CollisionDetectionCheckBox?.IsChecked == true;
                CollisionGapTextBox.IsEnabled = enabled;
                CollisionGapTextBox.Opacity = enabled ? 1.0 : 0.5;
            }
        }

        private bool TryParseCollisionGap(string text, out double gapMm, out string error)
        {
            gapMm = 1.0;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out gapMm))
            {
                error = $"Invalid gap value: {text}";
                return false;
            }

            if (gapMm < 0 || gapMm > 1000)
            {
                error = "Gap must be between 0 and 1000mm.";
                return false;
            }

            return true;
        }

        private void LoadCollisionSettings()
        {
            if (CollisionDetectionCheckBox == null || CollisionGapTextBox == null || MinimumOffsetTextBox == null)
            {
                return;
            }

            var config = LoadConfig();

            if (TryGetBool(config, CollisionDetectionEnabledKey, out var enabled))
            {
                CollisionDetectionCheckBox.IsChecked = enabled;
            }

            if (TryGetString(config, CollisionGapKey, out var gap))
            {
                CollisionGapTextBox.Text = string.IsNullOrWhiteSpace(gap) ? "1" : gap;
            }
            else
            {
                CollisionGapTextBox.Text = "1";
            }

            if (TryGetString(config, MinimumOffsetKey, out var offset))
            {
                MinimumOffsetTextBox.Text = string.IsNullOrWhiteSpace(offset) ? "300" : offset;
            }
            else
            {
                MinimumOffsetTextBox.Text = "300";
            }

            CollisionDetectionCheckBox_Toggled(null, null);
        }

        private void SaveCollisionSettings()
        {
            if (CollisionDetectionCheckBox == null || CollisionGapTextBox == null || MinimumOffsetTextBox == null)
            {
                return;
            }

            try
            {
                var config = LoadConfig();
                config[CollisionDetectionEnabledKey] = CollisionDetectionCheckBox.IsChecked == true;
                config[CollisionGapKey] = string.IsNullOrWhiteSpace(CollisionGapTextBox.Text) ? "1" : CollisionGapTextBox.Text.Trim();
                config[MinimumOffsetKey] = string.IsNullOrWhiteSpace(MinimumOffsetTextBox.Text) ? "300" : MinimumOffsetTextBox.Text.Trim();
                SaveConfig(config);
            }
            catch (Exception)
            {
            }
        }

        private static bool TryGetBool(Dictionary<string, object> config, string key, out bool value)
        {
            value = false;
            if (!config.TryGetValue(key, out var raw) || raw == null)
            {
                return false;
            }

            if (raw is bool boolValue)
            {
                value = boolValue;
                return true;
            }

            if (raw is JToken token && token.Type == JTokenType.Boolean)
            {
                value = token.Value<bool>();
                return true;
            }

            if (raw is string text && bool.TryParse(text, out var parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        }

        private static bool TryGetLong(Dictionary<string, object> config, string key, out long value)
        {
            value = 0;
            if (!config.TryGetValue(key, out var raw) || raw == null)
            {
                return false;
            }

            switch (raw)
            {
                case long longValue:
                    value = longValue;
                    return true;
                case int intValue:
                    value = intValue;
                    return true;
                case double doubleValue:
                    value = (long)doubleValue;
                    return true;
                case float floatValue:
                    value = (long)floatValue;
                    return true;
                case decimal decimalValue:
                    value = (long)decimalValue;
                    return true;
                case JToken token when token.Type == JTokenType.Integer:
                    value = token.Value<long>();
                    return true;
                case string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                    value = parsed;
                    return true;
            }

            return false;
        }

        private static bool TryGetString(Dictionary<string, object> config, string key, out string value)
        {
            value = null;
            if (!config.TryGetValue(key, out var raw) || raw == null)
            {
                return false;
            }

            if (raw is string text)
            {
                value = text;
                return true;
            }

            if (raw is JToken token && token.Type == JTokenType.String)
            {
                value = token.Value<string>();
                return true;
            }

            value = raw.ToString();
            return true;
        }

        private static bool TryGetDouble(Dictionary<string, object> config, string key, out double value)
        {
            value = 0;
            if (!config.TryGetValue(key, out var raw) || raw == null)
            {
                return false;
            }

            switch (raw)
            {
                case double doubleValue:
                    value = doubleValue;
                    return true;
                case float floatValue:
                    value = floatValue;
                    return true;
                case decimal decimalValue:
                    value = (double)decimalValue;
                    return true;
                case long longValue:
                    value = longValue;
                    return true;
                case int intValue:
                    value = intValue;
                    return true;
                case JToken token when token.Type == JTokenType.Float || token.Type == JTokenType.Integer:
                    value = token.Value<double>();
                    return true;
                case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                    value = parsed;
                    return true;
            }

            return false;
        }

        private void RetagExecutionMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingRetagMode)
            {
                return;
            }

            _isUpdatingRetagMode = true;

            var checkbox = sender as CheckBox;
            if (checkbox == null || checkbox.IsChecked != true)
            {
                _isUpdatingRetagMode = false;
                return;
            }

            if (checkbox == RetagFullyAutomaticCheckBox)
            {
                if (RetagUserConfirmationCheckBox != null)
                {
                    RetagUserConfirmationCheckBox.IsChecked = false;
                }
            }
            else if (checkbox == RetagUserConfirmationCheckBox)
            {
                if (RetagFullyAutomaticCheckBox != null)
                {
                    RetagFullyAutomaticCheckBox.IsChecked = false;
                }
            }

            _isUpdatingRetagMode = false;
        }

        private void RetagSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteRetag(true);
        }

        private void NormalizeViewButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteRetag(false);
        }

        private void ExecuteRetag(bool useSelection)
        {
            if (_uiApplication?.ActiveUIDocument == null)
            {
                MessageBox.Show("Open a document before retagging.", "SmartTags", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var uiDoc = _uiApplication.ActiveUIDocument;
            var doc = uiDoc.Document;
            var view = doc.ActiveView;

            if (view == null)
            {
                MessageBox.Show("No active view.", "SmartTags", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var adjustmentService = new Services.TagAdjustmentService
            {
                Direction = GetPlacementDirection(),
                DetectElementRotation = DetectRotationCheckBox?.IsChecked == true,
                HasLeader = LeaderLineCheckBox?.IsChecked == true,
                Orientation = (OrientationComboBox?.SelectedItem as OrientationOption)?.Orientation ?? TagOrientation.Horizontal,
                EnableCollisionDetection = CollisionDetectionCheckBox?.IsChecked == true
            };

            if (!TryParseAngle(AngleTextBox?.Text, out var angleRadians, out var angleError))
            {
                MessageBox.Show(angleError, "SmartTags", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            adjustmentService.Angle = angleRadians;

            var hasLeader = LeaderLineCheckBox?.IsChecked == true;
            if (hasLeader)
            {
                if (!TryParseLength(LeaderLengthTextBox?.Text, out var leaderLength, out var lengthError))
                {
                    MessageBox.Show(lengthError, "SmartTags", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var leaderType = LeaderTypeComboBox?.SelectedItem as LeaderTypeOption;
                var applyToAttached = leaderType == null || leaderType.IsAttachedEnd;
                adjustmentService.AttachedLength = applyToAttached ? leaderLength : 0;
                adjustmentService.FreeLength = applyToAttached ? 0 : leaderLength;
            }

            if (adjustmentService.EnableCollisionDetection)
            {
                if (!TryParseCollisionGap(CollisionGapTextBox?.Text, out var gapMm, out var gapError))
                {
                    MessageBox.Show(gapError, "SmartTags", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                adjustmentService.CollisionGapMillimeters = gapMm;
            }

            if (!TryParseLength(MinimumOffsetTextBox?.Text, out var minimumOffsetMm, out var offsetError))
            {
                MessageBox.Show(offsetError, "SmartTags", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            adjustmentService.MinimumOffsetMillimeters = minimumOffsetMm;

            if (useSelection)
            {
                var selectionIds = uiDoc.Selection.GetElementIds();
                if (selectionIds == null || selectionIds.Count == 0)
                {
                    MessageBox.Show("Select elements in the active view to retag.", "SmartTags", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _retagApplyHandler.UseSelection = true;
                _retagApplyHandler.TargetElementIds = selectionIds.ToList();
            }
            else
            {
                _retagApplyHandler.UseSelection = false;
                _retagApplyHandler.TargetElementIds = null;
            }

            _retagApplyHandler.AdjustmentService = adjustmentService;

            var isFullyAutomatic = RetagFullyAutomaticCheckBox?.IsChecked == true;

            if (isFullyAutomatic)
            {
                _retagApplyExternalEvent.Raise();

                System.Threading.Tasks.Task.Run(() =>
                {
                    System.Threading.Thread.Sleep(500);
                    Dispatcher.Invoke(() =>
                    {
                        var result = _retagApplyHandler.LastResult;
                        if (result != null)
                        {
                            TaskDialog.Show("SmartTags", result.GetSummaryMessage());
                        }
                    });
                });
            }
            else
            {
                var operationMode = useSelection ? "Retag Selected" : "Normalize View";

                _retagConfirmationHandler.UseSelection = useSelection;
                _retagConfirmationHandler.TargetElementIds = useSelection ? _retagApplyHandler.TargetElementIds : null;
                _retagConfirmationHandler.OperationMode = operationMode;
                _retagConfirmationHandler.AdjustmentService = adjustmentService;

                _retagConfirmationExternalEvent.Raise();

                System.Threading.Tasks.Task.Run(() =>
                {
                    System.Threading.Thread.Sleep(500);
                    Dispatcher.Invoke(() =>
                    {
                        var result = _retagConfirmationHandler.LastResult;
                        if (result != null)
                        {
                            TaskDialog.Show("SmartTags", result.GetSummaryMessage());
                        }
                    });
                });
            }
        }

        private void LoadRetagExecutionMode()
        {
            if (RetagFullyAutomaticCheckBox == null || RetagUserConfirmationCheckBox == null)
            {
                return;
            }

            var config = LoadConfig();
            bool isFullyAutomatic = true;

            if (TryGetBool(config, RetagExecutionModeKey, out var value))
            {
                isFullyAutomatic = value;
            }

            _isUpdatingRetagMode = true;
            RetagFullyAutomaticCheckBox.IsChecked = isFullyAutomatic;
            RetagUserConfirmationCheckBox.IsChecked = !isFullyAutomatic;
            _isUpdatingRetagMode = false;
        }

        private void SaveRetagExecutionMode()
        {
            if (RetagFullyAutomaticCheckBox == null)
            {
                return;
            }

            try
            {
                var config = LoadConfig();
                config[RetagExecutionModeKey] = RetagFullyAutomaticCheckBox.IsChecked == true;
                SaveConfig(config);
            }
            catch (Exception)
            {
            }
        }

        private void LoadDirectionKeyword()
        {
            if (DirectionKeywordTextBox == null)
            {
                return;
            }

            var config = LoadConfig();
            if (TryGetString(config, DirectionKeywordKey, out var keyword))
            {
                DirectionKeywordTextBox.Text = keyword;
            }
        }

        private void SaveDirectionKeyword()
        {
            if (DirectionKeywordTextBox == null)
            {
                return;
            }

            try
            {
                var config = LoadConfig();
                config[DirectionKeywordKey] = DirectionKeywordTextBox.Text ?? string.Empty;
                SaveConfig(config);
            }
            catch (Exception)
            {
            }
        }

        private void DirectionKeywordTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SaveDirectionKeyword();
        }

        private void ActiveSelectionToggle_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveSelectionToggleButton == null)
            {
                return;
            }

            if (_isActiveSelectionModeActive)
            {
                _isActiveSelectionModeActive = false;
                _activeSelectionViewId = null;
                ActiveSelectionToggleButton.Content = "Start Active Selection";
                return;
            }

            if (!ValidateTagSettings())
            {
                return;
            }

            _isActiveSelectionModeActive = true;
            _activeSelectionViewId = _uiApplication?.ActiveUIDocument?.Document?.ActiveView?.Id;
            ActiveSelectionToggleButton.Content = "Stop Active Selection";

            System.Threading.Tasks.Task.Run(() =>
            {
                RunActiveSelectionLoop();
            });
        }

        private bool ValidateTagSettings()
        {
            if (_uiApplication?.ActiveUIDocument == null)
            {
                MessageBox.Show("Open a document before using Active Selection.", "SmartTags", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var uiDoc = _uiApplication.ActiveUIDocument;
            var doc = uiDoc.Document;

            var categoryOption = CategoryComboBox?.SelectedItem as TagCategoryOption;
            if (categoryOption == null)
            {
                MessageBox.Show("Select a category to tag.", "SmartTags", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (categoryOption.ElementCategoryId == ElementId.InvalidElementId)
            {
                MessageBox.Show("No matching element category found for this tag.", "SmartTags", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var tagTypeOption = TagTypeComboBox?.SelectedItem as TagTypeOption;
            if (tagTypeOption == null)
            {
                MessageBox.Show("Select a tag type.", "SmartTags", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            return true;
        }

        private void RunActiveSelectionLoop()
        {
            while (_isActiveSelectionModeActive)
            {
                try
                {
                    ElementId selectedElementId = null;
                    TagCategoryOption categoryOption = null;
                    bool shouldContinue = true;

                    Dispatcher.Invoke(() =>
                    {
                        if (!_isActiveSelectionModeActive)
                        {
                            shouldContinue = false;
                            return;
                        }

                        var uiDoc = _uiApplication?.ActiveUIDocument;
                        if (uiDoc == null)
                        {
                            _isActiveSelectionModeActive = false;
                            UpdateActiveSelectionButton();
                            shouldContinue = false;
                            return;
                        }

                        var doc = uiDoc.Document;
                        var view = doc.ActiveView;
                        if (view == null)
                        {
                            _isActiveSelectionModeActive = false;
                            UpdateActiveSelectionButton();
                            shouldContinue = false;
                            return;
                        }

                        if (_activeSelectionViewId != null && view.Id != _activeSelectionViewId)
                        {
                            _isActiveSelectionModeActive = false;
                            UpdateActiveSelectionButton();
                            shouldContinue = false;
                            return;
                        }

                        categoryOption = CategoryComboBox?.SelectedItem as TagCategoryOption;
                        if (categoryOption == null || categoryOption.ElementCategoryId == ElementId.InvalidElementId)
                        {
                            _isActiveSelectionModeActive = false;
                            UpdateActiveSelectionButton();
                            shouldContinue = false;
                            return;
                        }

                        try
                        {
                            var filter = new Services.CategorySelectionFilter(categoryOption.ElementCategoryId);
                            var reference = uiDoc.Selection.PickObject(
                                Autodesk.Revit.UI.Selection.ObjectType.Element,
                                filter,
                                "Click an element to tag (ESC twice to exit)");

                            if (reference != null)
                            {
                                selectedElementId = reference.ElementId;
                            }
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            _isActiveSelectionModeActive = false;
                            UpdateActiveSelectionButton();
                            shouldContinue = false;
                            return;
                        }
                        catch
                        {
                            _isActiveSelectionModeActive = false;
                            UpdateActiveSelectionButton();
                            shouldContinue = false;
                            return;
                        }

                        if (selectedElementId == null || selectedElementId == ElementId.InvalidElementId)
                        {
                            shouldContinue = false;
                            return;
                        }

                        ConfigureActiveSelectionHandler(selectedElementId, categoryOption);
                        _activeSelectionExternalEvent.Raise();
                    });

                    if (!shouldContinue)
                    {
                        continue;
                    }

                    System.Threading.Thread.Sleep(300);

                    while (_activeSelectionExternalEvent.IsPending)
                    {
                        System.Threading.Thread.Sleep(50);
                    }
                }
                catch
                {
                    Dispatcher.Invoke(() =>
                    {
                        _isActiveSelectionModeActive = false;
                        UpdateActiveSelectionButton();
                    });
                    break;
                }
            }
        }

        private void UpdateActiveSelectionButton()
        {
            if (ActiveSelectionToggleButton != null)
            {
                ActiveSelectionToggleButton.Content = "Start Active Selection";
            }
        }

        private void ConfigureActiveSelectionHandler(ElementId elementId, TagCategoryOption categoryOption)
        {
            var tagTypeOption = TagTypeComboBox?.SelectedItem as TagTypeOption;
            if (tagTypeOption == null)
            {
                return;
            }

            _activeSelectionHandler.ElementToTag = elementId;
            _activeSelectionHandler.CategoryId = categoryOption.ElementCategoryId;
            _activeSelectionHandler.TagTypeId = tagTypeOption.TypeId;
            _activeSelectionHandler.TagCategoryId = categoryOption.TagCategoryId;
            _activeSelectionHandler.HasLeader = LeaderLineCheckBox?.IsChecked == true;
            _activeSelectionHandler.Direction = GetPlacementDirection();
            _activeSelectionHandler.DetectElementRotation = DetectRotationCheckBox?.IsChecked == true;
            _activeSelectionHandler.SkipIfAlreadyTagged = SkipIfTaggedCheckBox?.IsChecked == true;

            var orientationOption = OrientationComboBox?.SelectedItem as OrientationOption;
            _activeSelectionHandler.Orientation = orientationOption != null ? orientationOption.Orientation : TagOrientation.Horizontal;

            TryParseAngle(AngleTextBox?.Text, out var angleRadians, out var _);
            _activeSelectionHandler.Angle = angleRadians;

            var hasLeader = LeaderLineCheckBox?.IsChecked == true;
            if (hasLeader)
            {
                if (TryParseLength(LeaderLengthTextBox?.Text, out var leaderLength, out var _))
                {
                    var leaderType = LeaderTypeComboBox?.SelectedItem as LeaderTypeOption;
                    var applyToAttached = leaderType == null || leaderType.IsAttachedEnd;
                    _activeSelectionHandler.AttachedLength = applyToAttached ? leaderLength : 0;
                    _activeSelectionHandler.FreeLength = applyToAttached ? 0 : leaderLength;
                }
            }

            _activeSelectionHandler.EnableCollisionDetection = CollisionDetectionCheckBox?.IsChecked == true;

            if (_activeSelectionHandler.EnableCollisionDetection)
            {
                if (TryParseCollisionGap(CollisionGapTextBox?.Text, out var gapMm, out var _))
                {
                    _activeSelectionHandler.CollisionGapMillimeters = gapMm;
                }
            }

            if (TryParseLength(MinimumOffsetTextBox?.Text, out var minimumOffsetMm, out var _))
            {
                _activeSelectionHandler.MinimumOffsetMillimeters = minimumOffsetMm;
            }

            _activeSelectionHandler.DirectionResolver = GetDirectionResolver();
        }

        private void DirectionCheckButton_Click(object sender, RoutedEventArgs e)
        {
            if (_uiApplication?.ActiveUIDocument == null)
            {
                MessageBox.Show("Open a document before checking direction tag types.", "SmartTags", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var doc = _uiApplication.ActiveUIDocument.Document;
            var categoryOption = CategoryComboBox?.SelectedItem as TagCategoryOption;
            if (categoryOption == null || categoryOption.TagCategoryId == ElementId.InvalidElementId)
            {
                MessageBox.Show("Select a tag category first.", "SmartTags", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var keyword = DirectionKeywordTextBox?.Text ?? string.Empty;

            var checkResult = Services.DirectionTagTypeResolver.CheckDirectionTagTypes(
                doc,
                categoryOption.TagCategoryId,
                keyword);

            if (!checkResult.Success)
            {
                MessageBox.Show(checkResult.ErrorMessage, "SmartTags", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PopulateDirectionTagTypes(categoryOption.TagCategoryId, checkResult);

            MessageBox.Show(checkResult.GetSummary(), "Direction Check Results", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void PopulateDirectionTagTypes(ElementId tagCategoryId, Services.DirectionCheckResult checkResult)
        {
            LeftTagTypes.Clear();
            RightTagTypes.Clear();
            UpTagTypes.Clear();
            DownTagTypes.Clear();

            if (!_tagTypesByCategory.TryGetValue(tagCategoryId, out var allTagTypes))
            {
                return;
            }

            foreach (var tagType in allTagTypes)
            {
                LeftTagTypes.Add(tagType);
                RightTagTypes.Add(tagType);
                UpTagTypes.Add(tagType);
                DownTagTypes.Add(tagType);
            }

            if (checkResult.LeftMatch != null && checkResult.LeftMatch.Found)
            {
                var match = LeftTagTypes.FirstOrDefault(t => t.TypeId == checkResult.LeftMatch.TypeId);
                if (match != null)
                {
                    LeftTagTypeComboBox.SelectedItem = match;
                }
            }

            if (checkResult.RightMatch != null && checkResult.RightMatch.Found)
            {
                var match = RightTagTypes.FirstOrDefault(t => t.TypeId == checkResult.RightMatch.TypeId);
                if (match != null)
                {
                    RightTagTypeComboBox.SelectedItem = match;
                }
            }

            if (checkResult.UpMatch != null && checkResult.UpMatch.Found)
            {
                var match = UpTagTypes.FirstOrDefault(t => t.TypeId == checkResult.UpMatch.TypeId);
                if (match != null)
                {
                    UpTagTypeComboBox.SelectedItem = match;
                }
            }

            if (checkResult.DownMatch != null && checkResult.DownMatch.Found)
            {
                var match = DownTagTypes.FirstOrDefault(t => t.TypeId == checkResult.DownMatch.TypeId);
                if (match != null)
                {
                    DownTagTypeComboBox.SelectedItem = match;
                }
            }
        }

        private Services.DirectionTagTypeResolver GetDirectionResolver()
        {
            var resolver = new Services.DirectionTagTypeResolver();

            resolver.DirectionKeyword = DirectionKeywordTextBox?.Text ?? string.Empty;

            var leftOption = LeftTagTypeComboBox?.SelectedItem as TagTypeOption;
            resolver.LeftTagTypeId = leftOption?.TypeId ?? ElementId.InvalidElementId;

            var rightOption = RightTagTypeComboBox?.SelectedItem as TagTypeOption;
            resolver.RightTagTypeId = rightOption?.TypeId ?? ElementId.InvalidElementId;

            var upOption = UpTagTypeComboBox?.SelectedItem as TagTypeOption;
            resolver.UpTagTypeId = upOption?.TypeId ?? ElementId.InvalidElementId;

            var downOption = DownTagTypeComboBox?.SelectedItem as TagTypeOption;
            resolver.DownTagTypeId = downOption?.TypeId ?? ElementId.InvalidElementId;

            var defaultOption = TagTypeComboBox?.SelectedItem as TagTypeOption;
            resolver.DefaultTagTypeId = defaultOption?.TypeId ?? ElementId.InvalidElementId;

            return resolver;
        }

        public sealed class TagCategoryOption
        {
            public TagCategoryOption(ElementId tagCategoryId, ElementId elementCategoryId, string displayName, string tagCategoryName)
            {
                TagCategoryId = tagCategoryId;
                ElementCategoryId = elementCategoryId;
                DisplayName = displayName;
                TagCategoryName = tagCategoryName;
            }

            public ElementId TagCategoryId { get; }
            public ElementId ElementCategoryId { get; }
            public string DisplayName { get; }
            public string TagCategoryName { get; }

            public override string ToString() => DisplayName;
        }

        public sealed class TagTypeOption
        {
            public TagTypeOption(ElementId typeId, string familyName, string typeName)
            {
                TypeId = typeId;
                FamilyName = familyName;
                TypeName = typeName;
            }

            public ElementId TypeId { get; }
            public string FamilyName { get; }
            public string TypeName { get; }
            public string DisplayName => string.IsNullOrWhiteSpace(FamilyName)
                ? TypeName
                : $"{FamilyName} : {TypeName}";

            public override string ToString() => DisplayName;
        }

        public sealed class OrientationOption
        {
            public OrientationOption(string name, TagOrientation orientation)
            {
                Name = name;
                Orientation = orientation;
            }

            public string Name { get; }
            public TagOrientation Orientation { get; }

            public override string ToString() => Name;
        }

        public sealed class LeaderTypeOption
        {
            public LeaderTypeOption(string name, bool isAttachedEnd)
            {
                Name = name;
                IsAttachedEnd = isAttachedEnd;
            }

            public string Name { get; }
            public bool IsAttachedEnd { get; }

            public override string ToString() => Name;
        }
    }
}
