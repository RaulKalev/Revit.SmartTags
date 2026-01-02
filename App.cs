using Autodesk.Revit.UI;
using ricaun.Revit.UI;
using SmartTags.Commands;

namespace SmartTags
{
    [AppLoader]
    public class App : IExternalApplication
    {
        private RibbonPanel ribbonPanel;

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
            var smartTagsButton = ribbonPanel.CreatePushButton<SmartTagsCommand>()
                .SetLargeImage("Assets/SmartTags.tiff")
                .SetText("Smart\r\nTags")
                .SetToolTip("Place and configure tags faster.")
                .SetLongDescription("Pick a category and tag type, then set leader options before placing tags.")
                .SetContextualHelp("https://github.com/RaulKalev/SmartTags");

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            // Trigger the update check
            ribbonPanel?.Remove();
            return Result.Succeeded;
        }

    }
}

