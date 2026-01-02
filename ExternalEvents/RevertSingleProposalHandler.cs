using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SmartTags.Models;

namespace SmartTags.ExternalEvents
{
    public class RevertSingleProposalHandler : IExternalEventHandler
    {
        public TagAdjustmentProposal Proposal { get; set; }
        public bool Success { get; private set; }

        public void Execute(UIApplication app)
        {
            Success = false;

            var uiDoc = app.ActiveUIDocument;
            if (uiDoc == null || Proposal == null)
            {
                return;
            }

            var doc = uiDoc.Document;

            using (var transaction = new Transaction(doc, "SmartTags: Revert Adjustment"))
            {
                transaction.Start();

                try
                {
                    var tag = doc.GetElement(Proposal.TagId) as IndependentTag;
                    if (tag == null)
                    {
                        return;
                    }

                    Proposal.OldState.ApplyToTag(doc, tag);

                    transaction.Commit();
                    Success = true;
                }
                catch
                {
                    if (transaction.HasStarted())
                    {
                        transaction.RollBack();
                    }
                }
            }
        }

        public string GetName()
        {
            return "SmartTags Revert Single Proposal";
        }
    }
}
