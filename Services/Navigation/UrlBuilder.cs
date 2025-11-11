using System;
using System.Globalization;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

namespace ProjectManagement.Services.Navigation;

// SECTION: URL builder implementation
public sealed class UrlBuilder : IUrlBuilder
{
    public string DocumentRepositoryView(Guid documentId)
        => $"/DocumentRepository/Documents/View?id={documentId}";

    public string DocumentRepositoryDownload(Guid documentId)
        => $"/DocumentRepository/Documents/Download?id={documentId}";

    public string FfcRecordManage(long recordId)
        => $"/ProjectOfficeReports/FFC/Records/Manage?editId={recordId.ToString(CultureInfo.InvariantCulture)}";

    public string FfcAttachmentView(long attachmentId)
        => $"/ProjectOfficeReports/FFC/Attachments/View?id={attachmentId.ToString(CultureInfo.InvariantCulture)}";

    public string IprRecordManage(int recordId)
        => $"/ProjectOfficeReports/Ipr/Manage?id={recordId.ToString(CultureInfo.InvariantCulture)}";

    public string IprAttachmentDownload(int recordId, int attachmentId)
        => $"/ProjectOfficeReports/Ipr/Download?iprRecordId={recordId.ToString(CultureInfo.InvariantCulture)}&attachmentId={attachmentId.ToString(CultureInfo.InvariantCulture)}";

    public string ActivityDetails(int activityId)
        => $"/Activities/Details?id={activityId.ToString(CultureInfo.InvariantCulture)}";

    public string ProjectOverview(int projectId)
        => $"/Projects/Overview/{projectId.ToString(CultureInfo.InvariantCulture)}";

    public string ProjectOfficeVisitDetails(Guid id)
        => $"/ProjectOfficeReports/Visits/Details/{id}";

    public string ProjectOfficeSocialMediaDetails(Guid id)
        => $"/ProjectOfficeReports/SocialMedia/Details/{id}";

    public string ProjectOfficeTrainingManage(Guid trainingId)
        => $"/ProjectOfficeReports/Training/Manage?id={trainingId}";

    public string ProjectOfficeTotTracker(int projectId)
        => $"/ProjectOfficeReports/Tot/Index?selectedProjectId={projectId.ToString(CultureInfo.InvariantCulture)}";

    public string ProjectOfficeProliferationManage(int projectId, ProliferationRecordKind kind, ProliferationSource source, int? year)
    {
        var yearPart = year.HasValue
            ? $"&year={year.Value.ToString(CultureInfo.InvariantCulture)}"
            : string.Empty;

        return $"/ProjectOfficeReports/Proliferation/Manage?projectId={projectId.ToString(CultureInfo.InvariantCulture)}&kind={kind}&source={source}{yearPart}";
    }
}
