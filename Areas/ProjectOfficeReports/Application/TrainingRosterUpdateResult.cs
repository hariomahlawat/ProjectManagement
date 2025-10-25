using System;
using System.Collections.Generic;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public enum TrainingRosterFailureCode
{
    None = 0,
    TrainingNotFound = 1,
    ConcurrencyConflict = 2,
    MissingUserId = 3,
    DuplicateArmyNumber = 4,
    InvalidRequest = 5
}

public sealed record TrainingRosterCounters(
    int Officers,
    int JuniorCommissionedOfficers,
    int OtherRanks,
    int Total,
    TrainingCounterSource Source);

public sealed record TrainingRosterUpdateResult(
    bool IsSuccess,
    TrainingRosterFailureCode FailureCode,
    string? ErrorMessage,
    byte[]? RowVersion,
    IReadOnlyList<TrainingRosterRow> Roster,
    TrainingRosterCounters? Counters)
{
    public static TrainingRosterUpdateResult Success(byte[] rowVersion, IReadOnlyList<TrainingRosterRow> roster, TrainingRosterCounters counters)
        => new(true, TrainingRosterFailureCode.None, null, rowVersion, roster, counters);

    public static TrainingRosterUpdateResult Failure(TrainingRosterFailureCode code, string? message)
        => new(false, code, message, null, Array.Empty<TrainingRosterRow>(), null);
}
