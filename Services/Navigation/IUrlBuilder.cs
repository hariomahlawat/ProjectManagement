using System;

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
}
