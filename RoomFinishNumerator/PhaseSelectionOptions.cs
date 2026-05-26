namespace RoomFinishNumerator
{
    public sealed class PhaseSelectionOptions
    {
        public PhaseSelectionOptions(bool considerPhase, long? selectedPhaseIdValue, long? selectedPhaseFilterIdValue = null)
        {
            ConsiderPhase = considerPhase;
            SelectedPhaseIdValue = selectedPhaseIdValue;
            SelectedPhaseFilterIdValue = selectedPhaseFilterIdValue;
        }

        public bool ConsiderPhase { get; }

        public long? SelectedPhaseIdValue { get; }

        public long? SelectedPhaseFilterIdValue { get; }

        public bool MatchesPhaseId(long? phaseIdValue)
        {
            if (!ConsiderPhase)
            {
                return true;
            }

            return SelectedPhaseIdValue.HasValue
                && phaseIdValue.HasValue
                && SelectedPhaseIdValue.Value == phaseIdValue.Value;
        }
    }
}
