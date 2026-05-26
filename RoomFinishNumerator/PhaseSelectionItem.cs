using Autodesk.Revit.DB;

namespace RoomFinishNumerator
{
    public sealed class PhaseSelectionItem
    {
        public PhaseSelectionItem(ElementId phaseId, string name)
        {
            PhaseId = phaseId;
            Name = name;
        }

        public ElementId PhaseId { get; }

        public string Name { get; }
    }
}
