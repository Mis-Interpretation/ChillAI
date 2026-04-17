namespace ChillAI.Model.TaskDecomposition
{
    /// <summary>
    /// High-level bucket for a big task. Drives the side-tab filter in TaskPanelView.
    /// Integer values are persisted to tasks.json via JsonUtility — do NOT reorder or
    /// insert values in the middle. Append-only.
    /// </summary>
    public enum TaskCategory
    {
        // Default for all pre-existing/legacy tasks (JsonUtility gives 0 for a missing field).
        Wanting = 0,
        Doing = 1
    }
}
