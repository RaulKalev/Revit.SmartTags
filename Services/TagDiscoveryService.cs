using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace SmartTags.Services
{
    public static class TagDiscoveryService
    {
        public static List<IndependentTag> FindTagsReferencingElements(
            Document doc,
            View view,
            ICollection<ElementId> elementIds)
        {
            var result = new List<IndependentTag>();

            if (doc == null || view == null || elementIds == null || elementIds.Count == 0)
            {
                return result;
            }

            var elementIdSet = new HashSet<ElementId>(elementIds);

            try
            {
                var allTagsInView = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>()
                    .ToList();

                foreach (var tag in allTagsInView)
                {
                    try
                    {
                        if (!SmartTagMarkerStorage.IsManagedTag(tag))
                        {
                            continue;
                        }

#if NET8_0_OR_GREATER
                        var taggedIds = tag.GetTaggedElementIds();
                        if (taggedIds != null && taggedIds.Count > 0)
                        {
                            foreach (var linkElementId in taggedIds)
                            {
                                var elementId = linkElementId.HostElementId;
                                if (elementIdSet.Contains(elementId))
                                {
                                    result.Add(tag);
                                    break;
                                }
                            }
                        }
#else
                        var references = tag.GetTaggedReferences();
                        if (references != null && references.Count > 0)
                        {
                            foreach (Reference reference in references)
                            {
                                if (reference != null && elementIdSet.Contains(reference.ElementId))
                                {
                                    result.Add(tag);
                                    break;
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
                return result;
            }

            return result;
        }

        public static List<IndependentTag> FindAllManagedTagsInView(Document doc, View view)
        {
            var result = new List<IndependentTag>();

            if (doc == null || view == null)
            {
                return result;
            }

            try
            {
                var allTagsInView = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>()
                    .ToList();

                foreach (var tag in allTagsInView)
                {
                    try
                    {
                        if (SmartTagMarkerStorage.IsManagedTag(tag))
                        {
                            result.Add(tag);
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
            catch
            {
                return result;
            }

            return result;
        }
    }
}
