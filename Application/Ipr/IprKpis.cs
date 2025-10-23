namespace ProjectManagement.Application.Ipr;

public sealed record IprKpis(
    int Total,
    int Draft,
    int Filed,
    int Granted,
    int Rejected,
    int Expired);
