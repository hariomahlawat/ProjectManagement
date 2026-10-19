using ProjectManagement.Services.Stages;

namespace ProjectManagement.Tests.Fakes;

/// <summary>Server-derived actors for direct Actual-date write tests.</summary>
internal static class TestActors
{
    public static ActualWriteActor Hod(string userId) => new(userId, IsAdmin: false, IsHoD: true, IsProjectOfficer: false);

    public static ActualWriteActor Admin(string userId) => new(userId, IsAdmin: true, IsHoD: false, IsProjectOfficer: false);

    public static ActualWriteActor ProjectOfficer(string userId) => new(userId, IsAdmin: false, IsHoD: false, IsProjectOfficer: true);

    public static ActualWriteActor HodAndProjectOfficer(string userId) => new(userId, IsAdmin: false, IsHoD: true, IsProjectOfficer: true);

    public static ActualWriteActor AdminAndProjectOfficer(string userId) => new(userId, IsAdmin: true, IsHoD: false, IsProjectOfficer: true);
}
