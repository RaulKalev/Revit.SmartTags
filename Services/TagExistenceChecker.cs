using Autodesk.Revit.DB;
using System.Linq;

namespace SmartTags.Services
{
    public static class TagExistenceChecker
    {
        public static bool IsElementTaggedInView(Document doc, View view, ElementId elementId, ElementId tagCategoryId)
        {
            if (doc == null || view == null || elementId == null || elementId == ElementId.InvalidElementId)
            {
                return false;
            }

            try
            {
                var tagsInView = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>()
                    .ToList();

                foreach (var tag in tagsInView)
                {
                    try
                    {
                        if (tagCategoryId != null && tagCategoryId != ElementId.InvalidElementId)
                        {
                            if (tag.Category == null || tag.Category.Id != tagCategoryId)
                            {
                                continue;
                            }
                        }

#if NET8_0_OR_GREATER
                        var taggedIds = tag.GetTaggedElementIds();
                        if (taggedIds != null && taggedIds.Count > 0)
                        {
                            foreach (var linkElementId in taggedIds)
                            {
                                var refElementId = linkElementId.HostElementId;
                                if (refElementId == elementId)
                                {
                                    return true;
                                }
                            }
                        }
#else
                        var references = tag.GetTaggedReferences();
                        if (references != null && references.Count > 0)
                        {
                            foreach (Reference reference in references)
                            {
                                if (reference != null && reference.ElementId == elementId)
                                {
                                    return true;
                                }
                            }
                        }
#endif
                    }
                    catch
                    {
                        continue;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
