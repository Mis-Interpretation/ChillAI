using System.Collections.Generic;

namespace ChillAI.Model.TaskDecomposition
{
    public interface ITaskDecompositionReader
    {
        IReadOnlyList<BigEvent> BigEvents { get; }
        BigEvent GetBigEvent(string bigEventId);
    }
}
