using Autodesk.Revit.DB;

namespace SmartTags.Models
{
    public class TagAdjustmentProposal
    {
        public ElementId TagId { get; set; }
        public ElementId ReferencedElementId { get; set; }
        public TagStateSnapshot OldState { get; set; }
        public TagStateSnapshot NewState { get; set; }
        public string Reason { get; set; }

        public TagAdjustmentProposal(
            ElementId tagId,
            ElementId referencedElementId,
            TagStateSnapshot oldState,
            TagStateSnapshot newState,
            string reason = null)
        {
            TagId = tagId;
            ReferencedElementId = referencedElementId;
            OldState = oldState;
            NewState = newState;
            Reason = reason ?? string.Empty;
        }

        public bool IsSignificantChange(double toleranceFeet = 0.00164)
        {
            if (OldState == null || NewState == null)
            {
                return false;
            }

            var positionDelta = (NewState.TagHeadPosition - OldState.TagHeadPosition).GetLength();
            if (positionDelta > toleranceFeet)
            {
                return true;
            }

            if (OldState.HasLeader != NewState.HasLeader)
            {
                return true;
            }

            return false;
        }
    }
}
