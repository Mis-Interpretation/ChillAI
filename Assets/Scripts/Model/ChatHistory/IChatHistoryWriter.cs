namespace ChillAI.Model.ChatHistory
{
    public interface IChatHistoryWriter : IChatHistoryReader
    {
        void RegisterPersistentAgent(string agentId);
        void AddEntry(string agentId, string role, string content);
        void ClearHistory(string agentId);
        void Save();
        void Load();
    }
}
