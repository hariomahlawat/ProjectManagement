using System;

namespace ProjectManagement.Application.Ipr;

public sealed record IprKpis(
    int Total,
    int FilingUnderProcess,
    int Filed,
    int Granted,
    int Rejected,
    int Withdrawn)
{
    public int TotalPercent => Total > 0 ? 100 : 0;

    public int FilingUnderProcessPercent => CalculatePercent(FilingUnderProcess);

    public int FiledPercent => CalculatePercent(Filed);

    public int GrantedPercent => CalculatePercent(Granted);

    public int RejectedPercent => CalculatePercent(Rejected);

    public int WithdrawnPercent => CalculatePercent(Withdrawn);

    private int CalculatePercent(int value)
    {
        if (Total <= 0)
        {
            return 0;
        }

        var percent = (int)Math.Round(value * 100d / Total, MidpointRounding.AwayFromZero);
        return Math.Clamp(percent, 0, 100);
    }
}
