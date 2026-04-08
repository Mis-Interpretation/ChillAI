namespace ChillAI.Core.Config
{
    public interface IConfigWriter : IConfigReader
    {
        void Save(AppConfig config);
    }
}
