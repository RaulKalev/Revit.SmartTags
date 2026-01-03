using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SmartTags.Models;
using SmartTags.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace SmartTags.ExternalEvents
{
    public class RetagConfirmationHandler : IExternalEventHandler
    {
        public ICollection<ElementId> TargetElementIds { get; set; }
        public bool UseSelection { get; set; }
        public string OperationMode { get; set; }
        public TagAdjustmentService AdjustmentService { get; set; }

        public RetagResult LastResult { get; private set; }

        public void Execute(UIApplication app)
        {
            LastResult = new RetagResult
            {
                OperationMode = OperationMode
            };

            var uiDoc = app?.ActiveUIDocument;
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
                LastResult.ErrorMessage = "Adjustment service not initialized.";
                return;
            }

            List<IndependentTag> tags;
            if (UseSelection && TargetElementIds != null && TargetElementIds.Count > 0)
            {
                tags = TagDiscoveryService.FindTagsReferencingElements(doc, view, TargetElementIds);
            }
            else
            {
                tags = TagDiscoveryService.FindAllManagedTagsInView(doc, view);
            }

            LastResult.TotalTagsFound = tags?.Count ?? 0;

            if (tags == null || tags.Count == 0)
            {
                LastResult.ErrorMessage = "No managed tags found.";
                return;
            }

            var proposals = AdjustmentService.ComputeAdjustments(doc, view, tags);

            if (proposals == null || proposals.Count == 0)
            {
                LastResult.ErrorMessage = "No adjustments needed.";
                return;
            }

            var acceptedCount = 0;
            var rejectedCount = 0;
            var failedCount = 0;

            for (int i = 0; i < proposals.Count; i++)
            {
                var proposal = proposals[i];

                using (var transaction = new Transaction(doc, "SmartTags: Apply Adjustment"))
                {
                    transaction.Start();

                    try
                    {
                        var tag = doc.GetElement(proposal.TagId) as IndependentTag;
                        if (tag == null)
                        {
                            failedCount++;
                            transaction.RollBack();
                            continue;
                        }

                        proposal.NewState.ApplyToTag(doc, tag);
                        transaction.Commit();
                    }
                    catch
                    {
                        if (transaction.HasStarted())
                        {
                            transaction.RollBack();
                        }
                        failedCount++;
                        continue;
                    }
                }

                FocusOnTag(uiDoc, proposal);

                var userChoice = ShowConfirmationDialog(
                    $"Adjustment {i + 1} of {proposals.Count}",
                    $"Keep this adjustment?\n\nReason: {proposal.Reason}");

                if (userChoice == ConfirmationChoice.Accept)
                {
                    acceptedCount++;
                }
                else if (userChoice == ConfirmationChoice.Reject)
                {
                    using (var transaction = new Transaction(doc, "SmartTags: Revert Adjustment"))
                    {
                        transaction.Start();
                        try
                        {
                            var tag = doc.GetElement(proposal.TagId) as IndependentTag;
                            if (tag != null)
                            {
                                proposal.OldState.ApplyToTag(doc, tag);
                            }
                            transaction.Commit();
                        }
                        catch
                        {
                            if (transaction.HasStarted())
                            {
                                transaction.RollBack();
                            }
                        }
                    }
                    rejectedCount++;
                }
                else
                {
                    using (var transaction = new Transaction(doc, "SmartTags: Revert Adjustment"))
                    {
                        transaction.Start();
                        try
                        {
                            var tag = doc.GetElement(proposal.TagId) as IndependentTag;
                            if (tag != null)
                            {
                                proposal.OldState.ApplyToTag(doc, tag);
                            }
                            transaction.Commit();
                        }
                        catch
                        {
                            if (transaction.HasStarted())
                            {
                                transaction.RollBack();
                            }
                        }
                    }
                    break;
                }
            }

            LastResult.AdjustedCount = acceptedCount;
            LastResult.FailedCount = failedCount + rejectedCount;
            LastResult.UnchangedCount = LastResult.TotalTagsFound - LastResult.AdjustedCount - LastResult.FailedCount;
        }

        public string GetName()
        {
            return "SmartTags Retag Confirmation";
        }

        private void FocusOnTag(UIDocument uiDoc, TagAdjustmentProposal proposal)
        {
            if (proposal == null || uiDoc == null)
            {
                return;
            }

            try
            {
                var doc = uiDoc.Document;
                var tag = doc.GetElement(proposal.TagId);

                if (tag != null)
                {
                    uiDoc.Selection.SetElementIds(new List<ElementId> { proposal.TagId });
                    uiDoc.ShowElements(proposal.TagId);
                }
            }
            catch
            {
            }
        }

        private ConfirmationChoice ShowConfirmationDialog(string title, string message)
        {
            var result = MessageBox.Show(
                message,
                title,
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    return ConfirmationChoice.Accept;
                case MessageBoxResult.No:
                    return ConfirmationChoice.Reject;
                default:
                    return ConfirmationChoice.Cancel;
            }
        }

        private enum ConfirmationChoice
        {
            Accept,
            Reject,
            Cancel
        }
    }
}
