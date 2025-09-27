using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models
{
    public class Workflow
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [ConcurrencyCheck]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        private ICollection<WorkflowStatus> _statuses = new List<WorkflowStatus>();

        public ICollection<WorkflowStatus> Statuses
        {
            get => _statuses;
            set => _statuses = value ?? new List<WorkflowStatus>();
        }
    }

    public class WorkflowStatus
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public int SortOrder { get; set; }

        public int WorkflowId { get; set; }
        public Workflow? Workflow { get; set; }

        public int? StatusId { get; set; }
        public Status? Status { get; set; }

        [ConcurrencyCheck]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
}
