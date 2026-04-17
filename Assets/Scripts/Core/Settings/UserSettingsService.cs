using System;
using System.IO;
using UnityEngine;
using Zenject;

namespace ChillAI.Core.Settings
{
    /// <summary>
    /// Loads, provides, and persists <see cref="UserSettingsData"/> as JSON
    /// in <c>Application.persistentDataPath/user_settings.json</c>.
    /// First run copies defaults from <see cref="UserSettingsDefaults"/> SO.
    /// </summary>
    public class UserSettingsService
    {
        const string FileName = "user_settings.json";
        static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        readonly UserSettingsDefaults _defaults;

        public UserSettingsData Data { get; private set; }

        [Inject]
        public UserSettingsService(UserSettingsDefaults defaults)
        {
            _defaults = defaults;
            Load();
        }

        void Load()
        {
            if (File.Exists(FilePath))
            {
                try
                {
                    var json = File.ReadAllText(FilePath);
                    Data = JsonUtility.FromJson<UserSettingsData>(json) ?? _defaults.CreateData();
                    Debug.Log($"[ChillAI] User settings loaded from {FilePath}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ChillAI] user_settings read failed: {e.Message}");
                    Data = _defaults.CreateData();
                }
            }
            else
            {
                Data = _defaults.CreateData();
                Save();
                Debug.Log($"[ChillAI] Default user settings created at {FilePath}");
            }

            AudioListener.volume = Mathf.Clamp01(Data.globalVolume);
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChillAI] user_settings write failed: {e.Message}");
            }
        }
    }
}
