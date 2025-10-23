namespace ProjectManagement.Application.Ipr;

public sealed record IprKpis(
    int Total,
    int FilingUnderProcess,
    int Filed,
    int Granted,
    int Rejected,
    int Withdrawn);
