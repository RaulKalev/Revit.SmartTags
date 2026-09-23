using Autodesk.Revit.DB;
using System.Collections.Generic;
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

            return GetTaggedElementIdsInView(doc, view, tagCategoryId).Contains(elementId);
        }

        /// <summary>
        /// Ids of the host-model elements tagged in the view by tags of the given tag category (any category when
        /// invalid). Collect once and test many elements, e.g. for Tag All with "Skip tagged".
        /// </summary>
        public static HashSet<ElementId> GetTaggedElementIdsInView(Document doc, View view, ElementId tagCategoryId)
        {
            var taggedElementIds = new HashSet<ElementId>();
            if (doc == null || view == null)
            {
                return taggedElementIds;
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
                                taggedElementIds.Add(linkElementId.HostElementId);
                            }
                        }
#else
                        var references = tag.GetTaggedReferences();
                        if (references != null && references.Count > 0)
                        {
                            foreach (Reference reference in references)
                            {
                                if (reference != null)
                                {
                                    taggedElementIds.Add(reference.ElementId);
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
            }
            catch
            {
            }

            return taggedElementIds;
        }
    }
}
