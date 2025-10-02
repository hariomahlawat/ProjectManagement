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
        private static readonly IReadOnlyDictionary<string, RemarkActorRole> RoleAliases = new Dictionary<string, RemarkActorRole>(StringComparer.OrdinalIgnoreCase)
        {
            ["Project Officer"] = RemarkActorRole.ProjectOfficer,
            ["ProjectOfficer"] = RemarkActorRole.ProjectOfficer,
            ["HoD"] = RemarkActorRole.HeadOfDepartment,
            ["HeadOfDepartment"] = RemarkActorRole.HeadOfDepartment,
            ["Head of Department"] = RemarkActorRole.HeadOfDepartment,
            ["Comdt"] = RemarkActorRole.Commandant,
            ["Commandant"] = RemarkActorRole.Commandant,
            ["Admin"] = RemarkActorRole.Administrator,
            ["Administrator"] = RemarkActorRole.Administrator,
            ["TA"] = RemarkActorRole.Ta,
            ["Ta"] = RemarkActorRole.Ta,
            ["MCO"] = RemarkActorRole.Mco,
            ["Mco"] = RemarkActorRole.Mco,
            ["Project Office"] = RemarkActorRole.ProjectOffice,
            ["ProjectOffice"] = RemarkActorRole.ProjectOffice,
            ["Main Office"] = RemarkActorRole.MainOffice,
            ["MainOffice"] = RemarkActorRole.MainOffice
        };

        public static bool TryParse(string roleName, out RemarkActorRole role)
        {
            if (string.IsNullOrWhiteSpace(roleName))
            {
                role = RemarkActorRole.Unknown;
                return false;
            }

            var normalized = roleName.Trim();
            if (RoleAliases.TryGetValue(normalized, out var mapped))
            {
                role = mapped;
                return true;
            }

            role = RemarkActorRole.Unknown;
            return false;
        }
    }
}
