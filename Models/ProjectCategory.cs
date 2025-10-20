using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models
{
    public class ProjectCategory
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string CreatedByUserId { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int? ParentId { get; set; }
        public ProjectCategory? Parent { get; set; }

        public ICollection<ProjectCategory> Children { get; set; } = new List<ProjectCategory>();

        public ICollection<Project> Projects { get; set; } = new List<Project>();

        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; }
    }
}
