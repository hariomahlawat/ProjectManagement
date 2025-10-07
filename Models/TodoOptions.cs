namespace ProjectManagement.Models
{
    public class TodoOptions
    {
        public int RetentionDays { get; set; } = 7;
        public int MaxOpenTasks { get; set; } = 500;
    }
}
