namespace ChillAI.Core.Config
{
    public interface IConfigReader
    {
        AppConfig Config { get; }
        string OpenAIApiKey { get; }
        bool HasApiKey { get; }
        string ConfigFilePath { get; }

        /// <summary>
        /// Returns overrideValue if >= 0, otherwise returns defaultValue.
        /// </summary>
        float GetEffectiveFloat(float overrideValue, float defaultValue);
    }
}
