using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models
{
    public class Status
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public int SortOrder { get; set; }

        [ConcurrencyCheck]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        private ICollection<WorkflowStatus> _workflowStatuses = new List<WorkflowStatus>();

        public ICollection<WorkflowStatus> WorkflowStatuses
        {
            get => _workflowStatuses;
            set => _workflowStatuses = value ?? new List<WorkflowStatus>();
        }
    }
}
