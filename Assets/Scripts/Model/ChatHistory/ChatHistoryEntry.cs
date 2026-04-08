namespace ChillAI.Model.ChatHistory
{
    public class ChatHistoryEntry
    {
        public string Role { get; }
        public string Content { get; }

        public ChatHistoryEntry(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }
}
