using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SmartTags.Models;
using SmartTags.Services;
using System.Collections.Generic;
using System.Linq;

namespace SmartTags.ExternalEvents
{
    public class RetagApplyHandler : IExternalEventHandler
    {
        public bool UseSelection { get; set; }
        public IList<ElementId> TargetElementIds { get; set; }
        public TagAdjustmentService AdjustmentService { get; set; }

        public RetagResult LastResult { get; private set; }

        public void Execute(UIApplication app)
        {
            LastResult = new RetagResult();

            var uiDoc = app.ActiveUIDocument;
            if (uiDoc == null)
            {
                LastResult.ErrorMessage = "No active document.";
                return;
            }

            var doc = uiDoc.Document;
            var view = doc.ActiveView;
            if (view == null)
            {
                LastResult.ErrorMessage = "No active view.";
                return;
            }

            if (AdjustmentService == null)
            {
                LastResult.ErrorMessage = "Adjustment service not configured.";
                return;
            }

            List<IndependentTag> tags;

            if (UseSelection && TargetElementIds != null && TargetElementIds.Count > 0)
            {
                tags = TagDiscoveryService.FindTagsReferencingElements(doc, view, TargetElementIds);
                LastResult.OperationMode = "Retag Selected";
            }
            else
            {
                tags = TagDiscoveryService.FindAllManagedTagsInView(doc, view);
                LastResult.OperationMode = "Normalize View";
            }

            if (tags == null || tags.Count == 0)
            {
                LastResult.ErrorMessage = "No SmartTags-managed tags found.";
                return;
            }

            LastResult.TotalTagsFound = tags.Count;

            var proposals = AdjustmentService.ComputeAdjustments(doc, view, tags);
            if (proposals == null || proposals.Count == 0)
            {
                LastResult.UnchangedCount = tags.Count;
                return;
            }

            using (var transaction = new Transaction(doc, "SmartTags: " + LastResult.OperationMode))
            {
                transaction.Start();

                foreach (var proposal in proposals)
                {
                    try
                    {
                        var tag = doc.GetElement(proposal.TagId) as IndependentTag;
                        if (tag == null)
                        {
                            LastResult.FailedCount++;
                            continue;
                        }

                        proposal.NewState.ApplyToTag(doc, tag);
                        LastResult.AdjustedCount++;
                    }
                    catch
                    {
                        LastResult.FailedCount++;
                    }
                }

                transaction.Commit();
            }

            LastResult.UnchangedCount = LastResult.TotalTagsFound - LastResult.AdjustedCount - LastResult.FailedCount;
        }

        public string GetName()
        {
            return "SmartTags Retag Apply";
        }
    }

    public class RetagResult
    {
        public string OperationMode { get; set; }
        public int TotalTagsFound { get; set; }
        public int AdjustedCount { get; set; }
        public int UnchangedCount { get; set; }
        public int FailedCount { get; set; }
        public string ErrorMessage { get; set; }

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        public string GetSummaryMessage()
        {
            if (HasError)
            {
                return ErrorMessage;
            }

            if (TotalTagsFound == 0)
            {
                return "No SmartTags-managed tags found.";
            }

            var parts = new List<string>();

            if (AdjustedCount > 0)
            {
                parts.Add($"{AdjustedCount} tag(s) adjusted");
            }

            if (UnchangedCount > 0)
            {
                parts.Add($"{UnchangedCount} tag(s) unchanged");
            }

            if (FailedCount > 0)
            {
                parts.Add($"{FailedCount} tag(s) failed");
            }

            if (parts.Count == 0)
            {
                return "No changes made.";
            }

            return string.Join(", ", parts) + ".";
        }
    }
}
