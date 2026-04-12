namespace ChillAI.Model.UserProfile
{
    public interface IProfileWriter : IProfileReader
    {
        void SetAnswer(string questionId, string answer, float confidence);
        void RecordRunTime();
        void Save();
        void Load();
    }
}
