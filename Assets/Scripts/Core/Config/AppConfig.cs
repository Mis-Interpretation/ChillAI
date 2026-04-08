using System;

namespace ChillAI.Core.Config
{
    [Serializable]
    public class AppConfig
    {
        public string openaiApiKey = "";
        public float processScanIntervalOverride = -1f;
        public float windowAlphaOverride = -1f;
    }
}
