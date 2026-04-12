using System.Collections.Generic;

namespace ChillAI.Core.Signals
{
    public class ProfileUpdatedSignal
    {
        public IReadOnlyList<string> UpdatedQuestionIds { get; }

        public ProfileUpdatedSignal(IReadOnlyList<string> updatedQuestionIds)
        {
            UpdatedQuestionIds = updatedQuestionIds;
        }
    }
}
