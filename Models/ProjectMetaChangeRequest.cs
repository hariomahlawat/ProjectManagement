using System;

namespace ProjectManagement.Models
{
    public class ProjectMetaChangeRequest
    {
        public int Id { get; set; }

        public int ProjectId { get; set; }

        public Project? Project { get; set; }

        public string ChangeType { get; set; } = string.Empty;

        public string Payload { get; set; } = string.Empty;

        public string DecisionStatus { get; set; } = "Pending";

        public string? DecisionNote { get; set; }

        public string? RequestedByUserId { get; set; }

        public DateTimeOffset RequestedOnUtc { get; set; } = DateTimeOffset.UtcNow;

        public string? DecidedByUserId { get; set; }

        public DateTimeOffset? DecidedOnUtc { get; set; }
    }
}
