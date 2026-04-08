using ChillAI.Core;

namespace ChillAI.Model.BehaviorMapping
{
    public class BehaviorMappingModel : IBehaviorMappingReader
    {
        readonly BehaviorMappingData _data;

        public BehaviorMappingModel(BehaviorMappingData data)
        {
            _data = data;
        }

        public SoftwareCategory GetCategory(string processName)
        {
            return _data.TryGetCategory(processName, out var category)
                ? category
                : _data.defaultCategory;
        }

        public bool IsWhitelisted(string processName)
        {
            return _data.IsWhitelisted(processName);
        }
    }
}
