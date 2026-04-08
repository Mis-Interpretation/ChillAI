using System.IO;
using UnityEngine;

namespace ChillAI.Core.Config
{
    public class ConfigService : IConfigWriter
    {
        static readonly string ConfigPath = Path.Combine(Application.persistentDataPath, "config.json");

        public AppConfig Config { get; private set; }
        public string OpenAIApiKey => Config.openaiApiKey;
        public bool HasApiKey => !string.IsNullOrWhiteSpace(Config.openaiApiKey);
        public bool IsFirstRun { get; private set; }

        /// <summary>
        /// Returns the full path to config.json for display to users.
        /// </summary>
        public string ConfigFilePath => ConfigPath;

        public ConfigService()
        {
            Load();
        }

        void Load()
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                Config = JsonUtility.FromJson<AppConfig>(json) ?? new AppConfig();
                Debug.Log($"[ChillAI] Config loaded from {ConfigPath}");
            }
            else
            {
                IsFirstRun = true;
                Config = new AppConfig();
                Save(Config);
                Debug.Log($"[ChillAI] First run! Default config created at {ConfigPath}");
                Debug.Log("[ChillAI] Please edit config.json to add your OpenAI API key.");
            }
        }

        public void Save(AppConfig config)
        {
            Config = config;
            var json = JsonUtility.ToJson(config, true);
            File.WriteAllText(ConfigPath, json);
        }

        /// <summary>
        /// Returns the effective value considering config.json override, or the ScriptableObject default.
        /// </summary>
        public float GetEffectiveFloat(float overrideValue, float defaultValue)
        {
            return overrideValue >= 0f ? overrideValue : defaultValue;
        }
    }
}
