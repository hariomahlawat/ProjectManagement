using System;
using System.Globalization;

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
}
