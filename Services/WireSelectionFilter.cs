using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI.Selection;

namespace SmartTags.Services
{
    public class WireSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is Wire;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
