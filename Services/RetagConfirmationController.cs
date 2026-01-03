using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SmartTags.ExternalEvents;
using SmartTags.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace SmartTags.Services
{
    public class RetagConfirmationController
    {
        private readonly UIApplication _uiApp;

        public RetagConfirmationController(UIApplication uiApp)
        {
            _uiApp = uiApp;
        }

        public RetagResult RunConfirmationWorkflow(
            List<TagAdjustmentProposal> proposals,
            string operationMode)
        {
            var result = new RetagResult
            {
                OperationMode = operationMode,
                TotalTagsFound = proposals?.Count ?? 0
            };

            if (proposals == null || proposals.Count == 0)
            {
                result.ErrorMessage = "No adjustments to confirm.";
                return result;
            }

            var uiDoc = _uiApp?.ActiveUIDocument;
            if (uiDoc == null)
            {
                result.ErrorMessage = "No active document.";
                return result;
            }

            var doc = uiDoc.Document;
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

                FocusOnTag(proposal);

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

            result.AdjustedCount = acceptedCount;
            result.FailedCount = failedCount + rejectedCount;
            result.UnchangedCount = result.TotalTagsFound - result.AdjustedCount - result.FailedCount;

            return result;
        }

        private void FocusOnTag(TagAdjustmentProposal proposal)
        {
            if (proposal == null || _uiApp?.ActiveUIDocument == null)
            {
                return;
            }

            try
            {
                var uiDoc = _uiApp.ActiveUIDocument;
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
