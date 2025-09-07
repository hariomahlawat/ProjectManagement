using System;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Models
{
    public class Project
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = IstClock.Now;
    }
}
