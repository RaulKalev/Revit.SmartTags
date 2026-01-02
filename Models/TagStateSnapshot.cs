using Autodesk.Revit.DB;

namespace SmartTags.Models
{
    public class TagStateSnapshot
    {
        public XYZ TagHeadPosition { get; set; }
        public bool HasLeader { get; set; }
        public LeaderEndCondition LeaderEndCondition { get; set; }
        public TagOrientation Orientation { get; set; }

        public TagStateSnapshot()
        {
        }

        public TagStateSnapshot(IndependentTag tag)
        {
            if (tag == null)
            {
                return;
            }

            TagHeadPosition = tag.TagHeadPosition;
            HasLeader = tag.HasLeader;

            try
            {
                Orientation = tag.TagOrientation;
            }
            catch
            {
                Orientation = TagOrientation.Horizontal;
            }

            try
            {
                LeaderEndCondition = tag.LeaderEndCondition;
            }
            catch
            {
                LeaderEndCondition = 0;
            }
        }

        public void ApplyToTag(Document doc, IndependentTag tag)
        {
            if (doc == null || tag == null)
            {
                return;
            }

            if (TagHeadPosition != null)
            {
                try
                {
                    tag.TagHeadPosition = TagHeadPosition;
                }
                catch
                {
                }
            }

            try
            {
                tag.HasLeader = HasLeader;
            }
            catch
            {
            }

            try
            {
                tag.LeaderEndCondition = LeaderEndCondition;
            }
            catch
            {
            }
        }
    }
}
