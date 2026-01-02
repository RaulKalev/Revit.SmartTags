using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace SmartTags.Services
{
    public class CategorySelectionFilter : ISelectionFilter
    {
        private readonly ElementId _categoryId;

        public CategorySelectionFilter(ElementId categoryId)
        {
            _categoryId = categoryId;
        }

        public bool AllowElement(Element elem)
        {
            if (elem == null || elem.Category == null)
            {
                return false;
            }

            return elem.Category.Id == _categoryId;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
