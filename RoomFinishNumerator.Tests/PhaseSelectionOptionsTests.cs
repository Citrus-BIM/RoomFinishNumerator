using Xunit;

namespace RoomFinishNumerator.Tests
{
    public class PhaseSelectionOptionsTests
    {
        [Fact]
        public void MatchesPhaseIdAllowsAllRoomsWhenPhaseIsNotConsidered()
        {
            var options = new PhaseSelectionOptions(false, null);

            Assert.True(options.MatchesPhaseId(null));
            Assert.True(options.MatchesPhaseId(7));
        }

        [Fact]
        public void MatchesPhaseIdRequiresSelectedPhaseWhenPhaseIsConsidered()
        {
            var options = new PhaseSelectionOptions(true, 42);

            Assert.True(options.MatchesPhaseId(42));
            Assert.False(options.MatchesPhaseId(7));
            Assert.False(options.MatchesPhaseId(null));
        }

        [Fact]
        public void ConstructorStoresSelectedPhaseFilterWhenProvided()
        {
            var options = new PhaseSelectionOptions(true, 42, 9);

            Assert.Equal(9L, options.SelectedPhaseFilterIdValue);
        }
    }
}
