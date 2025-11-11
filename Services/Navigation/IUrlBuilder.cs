using System;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

namespace ProjectManagement.Services.Navigation;

// SECTION: URL builder contract
public interface IUrlBuilder
{
    string DocumentRepositoryView(Guid documentId);
    string DocumentRepositoryDownload(Guid documentId);
    string FfcRecordManage(long recordId);
    string FfcAttachmentView(long attachmentId);
    string IprRecordManage(int recordId);
    string IprAttachmentDownload(int recordId, int attachmentId);
    string ActivityDetails(int activityId);
    string ProjectDocumentPreview(int documentId);
    string ProjectOverview(int projectId);
    string ProjectOfficeVisitDetails(Guid id);
    string ProjectOfficeSocialMediaDetails(Guid id);
    string ProjectOfficeTrainingManage(Guid trainingId);
    string ProjectOfficeTotTracker(int projectId);
    string ProjectOfficeProliferationManage(int projectId, ProliferationRecordKind kind, ProliferationSource source, int? year);
}
