using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace SmartTags.Services
{
    public class DetailLineSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is DetailCurve;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
