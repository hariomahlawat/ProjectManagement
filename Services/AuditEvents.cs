using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using ProjectManagement.Models;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Services;

public static class Audit
{
    public static class Events
    {
        public static AuditEvent DraftDeleted(int projectId, int planVersionId, string userId, DateTimeOffset deletedAt)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["PlanVersionId"] = planVersionId.ToString(),
                ["DeletedOnUtc"] = deletedAt.UtcDateTime.ToString("O")
            };

            return new AuditEvent("Plan.DraftDeleted", userId, data);
        }

        public static AuditEvent ProjectPhotoAdded(int projectId, int photoId, string userId, bool isCover)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["PhotoId"] = photoId.ToString(),
                ["IsCover"] = isCover ? "true" : "false"
            };

            return new AuditEvent("Project.PhotoAdded", userId, data);
        }

        public static AuditEvent ProjectPhotoUpdated(int projectId, int photoId, string userId, string changeType)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["PhotoId"] = photoId.ToString(),
                ["ChangeType"] = changeType
            };

            return new AuditEvent("Project.PhotoUpdated", userId, data);
        }

        public static AuditEvent ProjectPhotoRemoved(int projectId, int photoId, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["PhotoId"] = photoId.ToString()
            };

            return new AuditEvent("Project.PhotoRemoved", userId, data);
        }

        public static AuditEvent ProjectPhotoReordered(int projectId, string userId, IEnumerable<int> orderedPhotoIds)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["Order"] = string.Join(',', orderedPhotoIds)
            };

            return new AuditEvent("Project.PhotoReordered", userId, data);
        }

        public static AuditEvent ProjectLifecycleMarkedCompleted(int projectId, string userId, int? completionYear)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["CompletionYear"] = completionYear?.ToString(CultureInfo.InvariantCulture)
            };

            return new AuditEvent("Project.LifecycleCompleted", userId, data);
        }

        public static AuditEvent ProjectLifecycleCompletionEndorsed(int projectId, string userId, DateOnly completionDate, int? completionYear)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["CompletionDate"] = completionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["CompletionYear"] = completionYear?.ToString(CultureInfo.InvariantCulture)
            };

            return new AuditEvent("Project.LifecycleCompletionEndorsed", userId, data);
        }

        public static AuditEvent ProjectLifecycleCancelled(int projectId, string userId, DateOnly cancelledOn, string reason)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["CancelledOn"] = cancelledOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Reason"] = reason
            };

            return new AuditEvent("Project.LifecycleCancelled", userId, data);
        }

        public static AuditEvent ProjectDocumentRequested(int projectId, int requestId, string userId, ProjectDocumentRequestType type, int? documentId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["RequestId"] = requestId.ToString(),
                ["Type"] = type.ToString(),
                ["DocumentId"] = documentId?.ToString()
            };

            return new AuditEvent("Project.DocumentRequested", userId, data);
        }

        public static AuditEvent ProjectDocumentApproved(int projectId, int requestId, string userId, ProjectDocumentRequestType type, int? documentId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["RequestId"] = requestId.ToString(),
                ["Type"] = type.ToString(),
                ["DocumentId"] = documentId?.ToString()
            };

            return new AuditEvent("Project.DocumentApproved", userId, data);
        }

        public static AuditEvent ProjectDocumentRejected(int projectId, int requestId, string userId, ProjectDocumentRequestType type, int? documentId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["RequestId"] = requestId.ToString(),
                ["Type"] = type.ToString(),
                ["DocumentId"] = documentId?.ToString()
            };

            return new AuditEvent("Project.DocumentRejected", userId, data);
        }

        public static AuditEvent ProjectDocumentPublished(int projectId, int documentId, string? userId, int fileStamp)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["DocumentId"] = documentId.ToString(),
                ["FileStamp"] = fileStamp.ToString()
            };

            return new AuditEvent("Project.DocumentPublished", userId, data);
        }

        public static AuditEvent ProjectDocumentReplaced(int projectId, int documentId, string? userId, int fileStamp)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["DocumentId"] = documentId.ToString(),
                ["FileStamp"] = fileStamp.ToString()
            };

            return new AuditEvent("Project.DocumentReplaced", userId, data);
        }

        public static AuditEvent ProjectDocumentRemoved(int projectId, int documentId, string? userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["DocumentId"] = documentId.ToString()
            };

            return new AuditEvent("Project.DocumentRemoved", userId, data);
        }

        public static AuditEvent ProjectDocumentRestored(int projectId, int documentId, string? userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["DocumentId"] = documentId.ToString()
            };

            return new AuditEvent("Project.DocumentRestored", userId, data);
        }

        public static AuditEvent ProjectDocumentHardDeleted(int projectId, int documentId, string? userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["DocumentId"] = documentId.ToString()
            };

            return new AuditEvent("Project.DocumentHardDeleted", userId, data);
        }

        public static AuditEvent ProjectDocumentsRestoredBulk(string? userId, IReadOnlyCollection<int> documentIds)
        {
            var data = new Dictionary<string, string?>
            {
                ["Count"] = documentIds.Count.ToString(CultureInfo.InvariantCulture),
                ["DocumentIds"] = string.Join(',', documentIds)
            };

            return new AuditEvent("Project.DocumentRestoredBulk", userId, data);
        }

        public static AuditEvent ProjectDocumentsHardDeletedBulk(string? userId, IReadOnlyCollection<int> documentIds)
        {
            var data = new Dictionary<string, string?>
            {
                ["Count"] = documentIds.Count.ToString(CultureInfo.InvariantCulture),
                ["DocumentIds"] = string.Join(',', documentIds)
            };

            return new AuditEvent("Project.DocumentHardDeletedBulk", userId, data);
        }

        public static AuditEvent VisitTypeAdded(Guid visitTypeId, string name, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitTypeId"] = visitTypeId.ToString(),
                ["Name"] = name
            };

            return new AuditEvent("ProjectOfficeReports.VisitTypeAdded", userId, data);
        }

        public static AuditEvent VisitTypeUpdated(Guid visitTypeId, string name, bool isActive, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitTypeId"] = visitTypeId.ToString(),
                ["Name"] = name,
                ["IsActive"] = isActive ? "true" : "false"
            };

            return new AuditEvent("ProjectOfficeReports.VisitTypeUpdated", userId, data);
        }

        public static AuditEvent VisitTypeDeleted(Guid visitTypeId, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitTypeId"] = visitTypeId.ToString()
            };

            return new AuditEvent("ProjectOfficeReports.VisitTypeDeleted", userId, data);
        }

        public static AuditEvent SocialMediaEventTypeAdded(Guid eventTypeId, string name, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["SocialMediaEventTypeId"] = eventTypeId.ToString(),
                ["Name"] = name
            };

            return new AuditEvent("ProjectOfficeReports.SocialMediaEventTypeAdded", userId, data);
        }

        public static AuditEvent SocialMediaEventTypeUpdated(Guid eventTypeId, string name, bool isActive, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["SocialMediaEventTypeId"] = eventTypeId.ToString(),
                ["Name"] = name,
                ["IsActive"] = isActive ? "true" : "false"
            };

            return new AuditEvent("ProjectOfficeReports.SocialMediaEventTypeUpdated", userId, data);
        }

        public static AuditEvent SocialMediaEventTypeDeleted(Guid eventTypeId, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["SocialMediaEventTypeId"] = eventTypeId.ToString()
            };

            return new AuditEvent("ProjectOfficeReports.SocialMediaEventTypeDeleted", userId, data);
        }

        public static AuditEvent SocialMediaPlatformAdded(Guid platformId, string name, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["SocialMediaPlatformId"] = platformId.ToString(),
                ["Name"] = name
            };

            return new AuditEvent("ProjectOfficeReports.SocialMediaPlatformAdded", userId, data);
        }

        public static AuditEvent SocialMediaPlatformUpdated(Guid platformId, string name, bool isActive, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["SocialMediaPlatformId"] = platformId.ToString(),
                ["Name"] = name,
                ["IsActive"] = isActive ? "true" : "false"
            };

            return new AuditEvent("ProjectOfficeReports.SocialMediaPlatformUpdated", userId, data);
        }

        public static AuditEvent SocialMediaPlatformDeleted(Guid platformId, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["SocialMediaPlatformId"] = platformId.ToString()
            };

            return new AuditEvent("ProjectOfficeReports.SocialMediaPlatformDeleted", userId, data);
        }

        public static AuditEvent VisitCreated(Guid visitId, Guid visitTypeId, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitId"] = visitId.ToString(),
                ["VisitTypeId"] = visitTypeId.ToString()
            };

            return new AuditEvent("ProjectOfficeReports.VisitCreated", userId, data);
        }

        public static AuditEvent VisitUpdated(Guid visitId, Guid visitTypeId, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitId"] = visitId.ToString(),
                ["VisitTypeId"] = visitTypeId.ToString()
            };

            return new AuditEvent("ProjectOfficeReports.VisitUpdated", userId, data);
        }

        public static AuditEvent VisitDeleted(Guid visitId, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitId"] = visitId.ToString()
            };

            return new AuditEvent("ProjectOfficeReports.VisitDeleted", userId, data);
        }

        public static AuditEvent VisitExported(
            string userId,
            Guid? visitTypeId,
            DateOnly? startDate,
            DateOnly? endDate,
            string? query,
            int count)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitTypeId"] = visitTypeId?.ToString(),
                ["StartDate"] = startDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["EndDate"] = endDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Query"] = query,
                ["Count"] = count.ToString(CultureInfo.InvariantCulture)
            };

            return new AuditEvent("ProjectOfficeReports.VisitExported", userId, data);
        }

        public static AuditEvent SocialMediaEventExported(
            string userId,
            Guid? socialMediaEventTypeId,
            DateOnly? startDate,
            DateOnly? endDate,
            string? query,
            Guid? platformId,
            string? platformName,
            bool onlyActiveEventTypes,
            int count)
        {
            var data = new Dictionary<string, string?>
            {
                ["SocialMediaEventTypeId"] = socialMediaEventTypeId?.ToString(),
                ["StartDate"] = startDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["EndDate"] = endDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Query"] = query,
                ["PlatformId"] = platformId?.ToString(),
                ["Platform"] = platformName,
                ["OnlyActiveEventTypes"] = onlyActiveEventTypes ? "true" : "false",
                ["Count"] = count.ToString(CultureInfo.InvariantCulture)
            };

            return new AuditEvent("ProjectOfficeReports.SocialMediaEventExported", userId, data);
        }

        public static AuditEvent VisitPhotoAdded(Guid visitId, Guid photoId, string userId, bool isCover)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitId"] = visitId.ToString(),
                ["PhotoId"] = photoId.ToString(),
                ["IsCover"] = isCover ? "true" : "false"
            };

            return new AuditEvent("ProjectOfficeReports.VisitPhotoAdded", userId, data);
        }

        public static AuditEvent VisitPhotoDeleted(Guid visitId, Guid photoId, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitId"] = visitId.ToString(),
                ["PhotoId"] = photoId.ToString()
            };

            return new AuditEvent("ProjectOfficeReports.VisitPhotoDeleted", userId, data);
        }

        public static AuditEvent VisitCoverPhotoChanged(Guid visitId, Guid photoId, string userId)
        {
            var data = new Dictionary<string, string?>
            {
                ["VisitId"] = visitId.ToString(),
                ["PhotoId"] = photoId.ToString()
            };

            return new AuditEvent("ProjectOfficeReports.VisitCoverPhotoChanged", userId, data);
        }

        public static AuditEvent ProliferationPreferenceChanged(
            int projectId,
            ProliferationSource source,
            int? year,
            string preferenceUserId,
            string actorUserId,
            string changeType)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                ["Source"] = source.ToDisplayName(),
                ["Year"] = year?.ToString(CultureInfo.InvariantCulture),
                ["PreferenceUserId"] = preferenceUserId,
                ["ChangeType"] = changeType
            };

            return new AuditEvent("ProjectOfficeReports.Proliferation.PreferenceChanged", actorUserId, data);
        }

        public static AuditEvent ProliferationRecordCreated(
            int projectId,
            ProliferationSource source,
            int year,
            int? directBeneficiaries,
            int? indirectBeneficiaries,
            decimal? investmentValue,
            string userId,
            string origin,
            ProliferationGranularity? granularity = null,
            int? period = null,
            string? periodLabel = null)
        {
            return CreateProliferationRecordEvent(
                "ProjectOfficeReports.Proliferation.RecordCreated",
                userId,
                projectId,
                source,
                year,
                directBeneficiaries,
                indirectBeneficiaries,
                investmentValue,
                origin,
                granularity,
                period,
                periodLabel,
                requestId: null,
                decisionNotes: null,
                submittedByUserId: null);
        }

        public static AuditEvent ProliferationRecordEdited(
            int projectId,
            ProliferationSource source,
            int year,
            int? directBeneficiaries,
            int? indirectBeneficiaries,
            decimal? investmentValue,
            string userId,
            string origin,
            ProliferationGranularity? granularity = null,
            int? period = null,
            string? periodLabel = null)
        {
            return CreateProliferationRecordEvent(
                "ProjectOfficeReports.Proliferation.RecordEdited",
                userId,
                projectId,
                source,
                year,
                directBeneficiaries,
                indirectBeneficiaries,
                investmentValue,
                origin,
                granularity,
                period,
                periodLabel,
                requestId: null,
                decisionNotes: null,
                submittedByUserId: null);
        }

        public static AuditEvent ProliferationRecordApproved(
            Guid requestId,
            int projectId,
            ProliferationSource source,
            int year,
            int? directBeneficiaries,
            int? indirectBeneficiaries,
            decimal? investmentValue,
            string decidedByUserId,
            ProliferationGranularity? granularity = null,
            int? period = null,
            string? periodLabel = null,
            string? decisionNotes = null,
            string? submittedByUserId = null)
        {
            return CreateProliferationRecordEvent(
                "ProjectOfficeReports.Proliferation.RecordApproved",
                decidedByUserId,
                projectId,
                source,
                year,
                directBeneficiaries,
                indirectBeneficiaries,
                investmentValue,
                origin: "Approval",
                granularity,
                period,
                periodLabel,
                requestId,
                decisionNotes,
                submittedByUserId);
        }

        public static AuditEvent ProliferationImportCompleted(
            string userId,
            string importType,
            ProliferationSource source,
            string? fileName,
            int processedRows,
            int importedRows,
            int errorCount)
        {
            var data = new Dictionary<string, string?>
            {
                ["ImportType"] = importType,
                ["Source"] = source.ToDisplayName(),
                ["FileName"] = fileName,
                ["ProcessedRows"] = processedRows.ToString(CultureInfo.InvariantCulture),
                ["ImportedRows"] = importedRows.ToString(CultureInfo.InvariantCulture),
                ["ErrorCount"] = errorCount.ToString(CultureInfo.InvariantCulture)
            };

            return new AuditEvent("ProjectOfficeReports.Proliferation.ImportCompleted", userId, data);
        }

        public static AuditEvent ProliferationExportGenerated(
            string userId,
            ProliferationSource? source,
            int? yearFrom,
            int? yearTo,
            int? sponsoringUnitId,
            string? simulatorUserId,
            string? searchTerm,
            int rowCount,
            string fileName)
        {
            var data = new Dictionary<string, string?>
            {
                ["Source"] = source?.ToDisplayName(),
                ["YearFrom"] = yearFrom?.ToString(CultureInfo.InvariantCulture),
                ["YearTo"] = yearTo?.ToString(CultureInfo.InvariantCulture),
                ["SponsoringUnitId"] = sponsoringUnitId?.ToString(CultureInfo.InvariantCulture),
                ["SimulatorUserId"] = simulatorUserId,
                ["SearchTerm"] = searchTerm,
                ["RowCount"] = rowCount.ToString(CultureInfo.InvariantCulture),
                ["FileName"] = fileName
            };

            return new AuditEvent("ProjectOfficeReports.Proliferation.ExportGenerated", userId, data);
        }

        public static AuditEvent ProliferationYearlySubmitted(
            int projectId,
            ProliferationSource source,
            int year,
            string submittedByUserId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["Source"] = source.ToDisplayName(),
                ["Year"] = year.ToString(CultureInfo.InvariantCulture)
            };

            return new AuditEvent("ProjectOfficeReports.ProliferationYearlySubmitted", submittedByUserId, data);
        }

        public static AuditEvent ProliferationYearlyDecided(
            int projectId,
            ProliferationSource source,
            int year,
            bool approved,
            string decidedByUserId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["Source"] = source.ToDisplayName(),
                ["Year"] = year.ToString(CultureInfo.InvariantCulture),
                ["Approved"] = approved ? "true" : "false"
            };

            return new AuditEvent("ProjectOfficeReports.ProliferationYearlyDecided", decidedByUserId, data);
        }

        public static AuditEvent ProliferationGranularSubmitted(
            int projectId,
            ProliferationSource source,
            int year,
            ProliferationGranularity granularity,
            int period,
            string submittedByUserId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["Source"] = source.ToDisplayName(),
                ["Year"] = year.ToString(CultureInfo.InvariantCulture),
                ["Granularity"] = granularity.ToString(),
                ["Period"] = period.ToString(CultureInfo.InvariantCulture)
            };

            return new AuditEvent("ProjectOfficeReports.ProliferationGranularSubmitted", submittedByUserId, data);
        }

        public static AuditEvent ProliferationGranularDecided(
            int projectId,
            ProliferationSource source,
            int year,
            ProliferationGranularity granularity,
            int period,
            bool approved,
            string decidedByUserId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(),
                ["Source"] = source.ToDisplayName(),
                ["Year"] = year.ToString(CultureInfo.InvariantCulture),
                ["Granularity"] = granularity.ToString(),
                ["Period"] = period.ToString(CultureInfo.InvariantCulture),
                ["Approved"] = approved ? "true" : "false"
            };

            return new AuditEvent("ProjectOfficeReports.ProliferationGranularDecided", decidedByUserId, data);
        }

        private static AuditEvent CreateProliferationRecordEvent(
            string action,
            string userId,
            int projectId,
            ProliferationSource source,
            int year,
            int? directBeneficiaries,
            int? indirectBeneficiaries,
            decimal? investmentValue,
            string? origin,
            ProliferationGranularity? granularity,
            int? period,
            string? periodLabel,
            Guid? requestId,
            string? decisionNotes,
            string? submittedByUserId)
        {
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                ["Source"] = source.ToDisplayName(),
                ["Year"] = year.ToString(CultureInfo.InvariantCulture),
                ["DirectBeneficiaries"] = directBeneficiaries?.ToString(CultureInfo.InvariantCulture),
                ["IndirectBeneficiaries"] = indirectBeneficiaries?.ToString(CultureInfo.InvariantCulture),
                ["InvestmentValue"] = investmentValue?.ToString(CultureInfo.InvariantCulture),
                ["Origin"] = origin,
                ["PeriodLabel"] = periodLabel,
                ["DecisionNotes"] = decisionNotes,
                ["SubmittedByUserId"] = submittedByUserId,
                ["RequestId"] = requestId?.ToString()
            };

            if (granularity.HasValue)
            {
                data["Granularity"] = granularity.Value.ToString();
                data["Period"] = period?.ToString(CultureInfo.InvariantCulture);
            }

            return new AuditEvent(action, userId, data);
        }
    }
}

public readonly record struct AuditEvent(string Action, string? UserId, IDictionary<string, string?> Data)
{
    public Task WriteAsync(IAuditService audit, string? message = null, string level = "Info", string? userName = null)
        => audit.LogAsync(Action, message, level, UserId, userName, Data);
}
