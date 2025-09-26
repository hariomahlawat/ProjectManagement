using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models
{
    public abstract class ProjectFactBase
    {
        public int Id { get; set; }

        [Required]
        public int ProjectId { get; set; }

        [Required]
        [MaxLength(64)]
        public string CreatedByUserId { get; set; } = string.Empty;

        public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;

        [ConcurrencyCheck]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }

    public class ProjectIpaFact : ProjectFactBase
    {
        public decimal IpaCost { get; set; }
    }

    public class ProjectSowFact : ProjectFactBase
    {
        [Required]
        [MaxLength(200)]
        public string SponsoringUnit { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string SponsoringLineDirectorate { get; set; } = string.Empty;
    }

    public class ProjectAonFact : ProjectFactBase
    {
        public decimal AonCost { get; set; }
    }

    public class ProjectBenchmarkFact : ProjectFactBase
    {
        public decimal BenchmarkCost { get; set; }
    }

    public class ProjectCommercialFact : ProjectFactBase
    {
        public decimal L1Cost { get; set; }
    }

    public class ProjectPncFact : ProjectFactBase
    {
        public decimal PncCost { get; set; }
    }

    public class ProjectSupplyOrderFact : ProjectFactBase
    {
        public DateOnly SupplyOrderDate { get; set; }
    }
}
