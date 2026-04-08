namespace ChillAI.Model.TaskDecomposition
{
    public class SubTask
    {
        public string Title { get; }
        public int Order { get; }
        public bool IsCompleted { get; set; }

        public SubTask(string title, int order)
        {
            Title = title;
            Order = order;
        }
    }
}
