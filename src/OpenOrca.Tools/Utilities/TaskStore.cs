using System.Text;

namespace OpenOrca.Tools.Utilities;

/// <summary>
/// In-memory session-scoped task list. Resets on process exit.
/// </summary>
public static class TaskStore
{
    private static readonly List<TaskItem> Tasks = [];
    private static int _nextId = 1;
    private static readonly Lock Lock = new();

    public static int Add(string description)
    {
        lock (Lock)
        {
            var item = new TaskItem(_nextId++, description);
            Tasks.Add(item);
            return item.Id;
        }
    }

    public static bool Complete(int id)
    {
        lock (Lock)
        {
            var item = Tasks.Find(t => t.Id == id);
            if (item is null) return false;
            item.IsCompleted = true;
            return true;
        }
    }

    public static bool Remove(int id)
    {
        lock (Lock)
        {
            return Tasks.RemoveAll(t => t.Id == id) > 0;
        }
    }

    public static string List()
    {
        lock (Lock)
        {
            if (Tasks.Count == 0)
                return "No tasks.";

            var sb = new StringBuilder();
            var completed = Tasks.Count(t => t.IsCompleted);
            sb.AppendLine($"Tasks: {completed}/{Tasks.Count} completed");
            sb.AppendLine();
            foreach (var t in Tasks)
            {
                var check = t.IsCompleted ? "x" : " ";
                sb.AppendLine($"  [{check}] #{t.Id}: {t.Description}");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Reset the store (used in tests).
    /// </summary>
    public static void Reset()
    {
        lock (Lock)
        {
            Tasks.Clear();
            _nextId = 1;
        }
    }

    public sealed class TaskItem
    {
        public int Id { get; }
        public string Description { get; }
        public bool IsCompleted { get; set; }

        public TaskItem(int id, string description)
        {
            Id = id;
            Description = description;
        }
    }
}
