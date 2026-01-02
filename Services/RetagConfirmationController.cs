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
        private readonly ApplySingleProposalHandler _applyHandler;
        private readonly RevertSingleProposalHandler _revertHandler;
        private readonly ExternalEvent _applyEvent;
        private readonly ExternalEvent _revertEvent;

        private List<TagAdjustmentProposal> _proposals;
        private int _currentIndex;
        private int _acceptedCount;
        private int _rejectedCount;
        private bool _cancelled;

        public RetagConfirmationController(UIApplication uiApp)
        {
            _uiApp = uiApp;
            _applyHandler = new ApplySingleProposalHandler();
            _revertHandler = new RevertSingleProposalHandler();
            _applyEvent = ExternalEvent.Create(_applyHandler);
            _revertEvent = ExternalEvent.Create(_revertHandler);
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

            _proposals = proposals;
            _currentIndex = 0;
            _acceptedCount = 0;
            _rejectedCount = 0;
            _cancelled = false;

            while (_currentIndex < _proposals.Count && !_cancelled)
            {
                var proposal = _proposals[_currentIndex];

                _applyHandler.Proposal = proposal;
                _applyEvent.Raise();

                System.Threading.Thread.Sleep(200);

                while (_applyEvent.IsPending)
                {
                    System.Threading.Thread.Sleep(50);
                }

                if (!_applyHandler.Success)
                {
                    result.FailedCount++;
                    _currentIndex++;
                    continue;
                }

                FocusOnTag(proposal);

                var userChoice = ShowConfirmationDialog(
                    $"Adjustment {_currentIndex + 1} of {_proposals.Count}",
                    $"Apply this adjustment to tag?\n\nReason: {proposal.Reason}");

                if (userChoice == ConfirmationChoice.Accept)
                {
                    _acceptedCount++;
                    _currentIndex++;
                }
                else if (userChoice == ConfirmationChoice.Reject)
                {
                    _revertHandler.Proposal = proposal;
                    _revertEvent.Raise();

                    System.Threading.Thread.Sleep(200);

                    while (_revertEvent.IsPending)
                    {
                        System.Threading.Thread.Sleep(50);
                    }

                    _rejectedCount++;
                    _currentIndex++;
                }
                else
                {
                    _revertHandler.Proposal = proposal;
                    _revertEvent.Raise();

                    System.Threading.Thread.Sleep(200);

                    while (_revertEvent.IsPending)
                    {
                        System.Threading.Thread.Sleep(50);
                    }

                    _cancelled = true;
                }
            }

            result.AdjustedCount = _acceptedCount;
            result.FailedCount += _rejectedCount;
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
