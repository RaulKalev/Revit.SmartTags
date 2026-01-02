using Autodesk.Revit.DB;

namespace SmartTags.Models
{
    public class ScheduleOption
    {
        public string Name { get; set; }
        public ElementId Id { get; set; }
        public ViewSchedule Schedule { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }
}
