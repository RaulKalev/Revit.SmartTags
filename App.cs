using Autodesk.Revit.UI;
using ricaun.Revit.UI;
using ricaun.Revit.UI.Utils;
using SmartTags.Commands;

namespace SmartTags
{
    [AppLoader]
    public class App : IExternalApplication
    {
        private const string LightIconPath = "Assets/SmartTags-light.tiff";
        private const string DarkIconPath = "Assets/SmartTags-dark.tiff";

        private RibbonPanel ribbonPanel;
        private PushButton smartTagsButton;

        public Result OnStartup(UIControlledApplication application)
        {
            // Define the custom tab name
            string tabName = "RK Tools";

            // Try to create the custom tab (avoid exception if it already exists)
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch
            {
                // Tab already exists; continue without throwing an error
            }

            // Create Ribbon Panel on the custom tab
            ribbonPanel = application.CreateOrSelectPanel(tabName, "Tools");

            // Create PushButton with embedded resource
            smartTagsButton = ribbonPanel.CreatePushButton<SmartTagsCommand>()
                .SetText("Smart\r\nTags")
                .SetToolTip("Place and configure tags faster.")
                .SetLongDescription("Pick a category and tag type, then set leader options before placing tags.")
                .SetContextualHelp("https://github.com/RaulKalev/SmartTags");

            // Icon follows the Revit UI theme: dark glyph on the light theme, light glyph on the dark theme
            UpdateButtonIcon(RibbonThemeUtils.IsDark);
            RibbonThemeUtils.ThemeChanged += OnThemeChanged;

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            // Unsubscribe so an App Loader reload does not keep a handler to the removed button
            RibbonThemeUtils.ThemeChanged -= OnThemeChanged;
            smartTagsButton = null;

            // Trigger the update check
            ribbonPanel?.Remove();
            return Result.Succeeded;
        }

        private void OnThemeChanged(object sender, ThemeChangedEventArgs e)
        {
            UpdateButtonIcon(e.IsDark);
        }

        private void UpdateButtonIcon(bool isDarkTheme)
        {
            smartTagsButton?.SetLargeImage(isDarkTheme ? DarkIconPath : LightIconPath);
        }
    }
}
