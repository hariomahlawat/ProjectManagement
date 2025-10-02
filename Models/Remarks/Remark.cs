using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.Remarks
{
    public enum RemarkType
    {
        Internal = 0,
        External = 1
    }

    public enum RemarkActorRole
    {
        Unknown = 0,
        ProjectOfficer = 1,
        HeadOfDepartment = 2,
        Commandant = 3,
        Administrator = 4,
        Ta = 5,
        Mco = 6,
        ProjectOffice = 7,
        MainOffice = 8
    }

    public enum RemarkAuditAction
    {
        Created = 0,
        Edited = 1,
        Deleted = 2
    }

    public sealed class Remark
    {
        public int Id { get; set; }

        [Required]
        public int ProjectId { get; set; }

        [Required]
        [MaxLength(450)]
        public string AuthorUserId { get; set; } = string.Empty;

        [Required]
        public RemarkActorRole AuthorRole { get; set; }

        [Required]
        public RemarkType Type { get; set; }

        [Required]
        [MaxLength(4000)]
        public string Body { get; set; } = string.Empty;

        public DateOnly EventDate { get; set; }

        [MaxLength(64)]
        public string? StageRef { get; set; }

        [MaxLength(256)]
        public string? StageNameSnapshot { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? LastEditedAtUtc { get; set; }

        public bool IsDeleted { get; set; }

        public DateTime? DeletedAtUtc { get; set; }

        [MaxLength(450)]
        public string? DeletedByUserId { get; set; }

        public RemarkActorRole? DeletedByRole { get; set; }

        [ConcurrencyCheck]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public Project? Project { get; set; }

        public ICollection<RemarkAudit> AuditEntries { get; set; } = new List<RemarkAudit>();
    }

    public sealed class RemarkAudit
    {
        public int Id { get; set; }

        [Required]
        public int RemarkId { get; set; }

        [Required]
        public RemarkAuditAction Action { get; set; }

        [Required]
        public RemarkType SnapshotType { get; set; }

        [Required]
        public RemarkActorRole SnapshotAuthorRole { get; set; }

        [MaxLength(450)]
        public string SnapshotAuthorUserId { get; set; } = string.Empty;

        public DateOnly SnapshotEventDate { get; set; }

        [MaxLength(64)]
        public string? SnapshotStageRef { get; set; }

        [MaxLength(256)]
        public string? SnapshotStageName { get; set; }

        [Required]
        [MaxLength(4000)]
        public string SnapshotBody { get; set; } = string.Empty;

        public DateTime SnapshotCreatedAtUtc { get; set; }

        public DateTime? SnapshotLastEditedAtUtc { get; set; }

        public bool SnapshotIsDeleted { get; set; }

        public DateTime? SnapshotDeletedAtUtc { get; set; }

        [MaxLength(450)]
        public string? SnapshotDeletedByUserId { get; set; }

        public RemarkActorRole? SnapshotDeletedByRole { get; set; }

        public int SnapshotProjectId { get; set; }

        [MaxLength(450)]
        public string? ActorUserId { get; set; }

        public RemarkActorRole ActorRole { get; set; }

        public DateTime ActionAtUtc { get; set; }

        public string? Meta { get; set; }

        public Remark Remark { get; set; } = null!;
    }

    public static class RemarkActorRoleExtensions
    {
        public static bool TryParse(string roleName, out RemarkActorRole role)
        {
            if (string.IsNullOrWhiteSpace(roleName))
            {
                role = RemarkActorRole.Unknown;
                return false;
            }

            var candidate = roleName.Trim();

            if (Matches(candidate, "Project Officer", "ProjectOfficer"))
            {
                return Set(RemarkActorRole.ProjectOfficer, out role);
            }

            if (Matches(candidate, "HoD", "Head Of Department", "HeadOfDepartment"))
            {
                return Set(RemarkActorRole.HeadOfDepartment, out role);
            }

            if (Matches(candidate, "Comdt", "Commandant"))
            {
                return Set(RemarkActorRole.Commandant, out role);
            }

            if (Matches(candidate, "Admin", "Administrator"))
            {
                return Set(RemarkActorRole.Administrator, out role);
            }

            if (Matches(candidate, "TA", "Ta"))
            {
                return Set(RemarkActorRole.Ta, out role);
            }

            if (Matches(candidate, "MCO", "Mco"))
            {
                return Set(RemarkActorRole.Mco, out role);
            }

            if (Matches(candidate, "Project Office", "ProjectOffice"))
            {
                return Set(RemarkActorRole.ProjectOffice, out role);
            }

            if (Matches(candidate, "Main Office", "MainOffice"))
            {
                return Set(RemarkActorRole.MainOffice, out role);
            }

            role = RemarkActorRole.Unknown;
            return false;
        }

        private static bool Matches(string value, params string[] options)
        {
            foreach (var option in options)
            {
                if (string.Equals(value, option, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Set(RemarkActorRole value, out RemarkActorRole role, bool success = true)
        {
            role = value;
            return success;
        }
    }
}
