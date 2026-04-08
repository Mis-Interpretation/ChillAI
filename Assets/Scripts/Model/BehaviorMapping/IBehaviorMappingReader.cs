using ChillAI.Core;

namespace ChillAI.Model.BehaviorMapping
{
    public interface IBehaviorMappingReader
    {
        SoftwareCategory GetCategory(string processName);
        bool IsWhitelisted(string processName);
    }
}
