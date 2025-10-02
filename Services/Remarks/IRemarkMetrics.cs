namespace ProjectManagement.Services.Remarks;

public interface IRemarkMetrics
{
    void RecordCreated();
    void RecordDeleted();
    void RecordEditDeniedWindowExpired(string action);
    void RecordPermissionDenied(string action, string reason);
}
