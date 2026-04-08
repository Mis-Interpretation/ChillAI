using System;

namespace ChillAI.Model.TaskDecomposition
{
    public class SubTask
    {
        public string Id { get; }
        public string Title { get; set; }
        public int Order { get; set; }
        public bool IsCompleted { get; set; }

        public SubTask(string title, int order)
        {
            Id = Guid.NewGuid().ToString();
            Title = title;
            Order = order;
        }
    }
}
