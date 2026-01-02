using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SmartTags.Models;
using System.Linq;

namespace SmartTags.ExternalEvents
{
    public class ApplySingleProposalHandler : IExternalEventHandler
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
            var view = doc.ActiveView;

            using (var transaction = new Transaction(doc, "SmartTags: Apply Adjustment"))
            {
                transaction.Start();

                try
                {
                    var tag = doc.GetElement(Proposal.TagId) as IndependentTag;
                    if (tag == null)
                    {
                        return;
                    }

                    Proposal.NewState.ApplyToTag(doc, tag);

                    if (Proposal.NewState.TagHeadPosition != null)
                    {
                        try
                        {
                            uiDoc.ShowElements(tag.Id);
                            var center = Proposal.NewState.TagHeadPosition;
                            uiDoc.GetOpenUIViews().First()?.ZoomAndCenterRectangle(
                                new XYZ(center.X - 5, center.Y - 5, center.Z),
                                new XYZ(center.X + 5, center.Y + 5, center.Z)
                            );
                        }
                        catch
                        {
                        }
                    }

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
            return "SmartTags Apply Single Proposal";
        }
    }
}
