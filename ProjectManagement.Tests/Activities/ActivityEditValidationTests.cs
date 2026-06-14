using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using ProjectManagement.Pages.Activities;
using Xunit;

namespace ProjectManagement.Tests.Activities;

public sealed class ActivityEditValidationTests
{
    [Fact]
    public void CreateInput_WithoutEventDate_IsRejected()
    {
        // SECTION: Arrange a create payload with the event date omitted.
        var input = CreateValidInput();
        input.ScheduledStart = null;

        var errors = Validate(input);

        // SECTION: Assert the required event date error is returned.
        var error = Assert.Single(errors, error => error.MemberNames.Contains(nameof(EditModel.InputModel.ScheduledStart)));
        Assert.Equal("Event date is required.", error.ErrorMessage);
    }

    [Fact]
    public void CreateInput_WithEventDate_Succeeds()
    {
        // SECTION: Arrange a complete create payload with an event date.
        var input = CreateValidInput();

        var errors = Validate(input);

        // SECTION: Assert no validation errors block activity creation.
        Assert.Empty(errors);
    }

    [Fact]
    public void CreateInput_WithEndDateBeforeEventDate_IsRejected()
    {
        // SECTION: Arrange a create payload whose optional end date precedes the event date.
        var input = CreateValidInput();
        input.ScheduledEnd = input.ScheduledStart!.Value.AddDays(-1);

        var errors = Validate(input);

        // SECTION: Assert the optional end date chronology is enforced.
        var error = Assert.Single(errors, error => error.MemberNames.Contains(nameof(EditModel.InputModel.ScheduledEnd)));
        Assert.Equal("End date must be on or after the event date.", error.ErrorMessage);
    }

    [Fact]
    public void CreateInput_WithEmptyOptionalEndDate_Succeeds()
    {
        // SECTION: Arrange a create payload without an optional end date.
        var input = CreateValidInput();
        input.ScheduledEnd = null;

        var errors = Validate(input);

        // SECTION: Assert the optional end date can remain empty.
        Assert.Empty(errors);
    }

    private static EditModel.InputModel CreateValidInput() => new()
    {
        Title = "Project Kickoff",
        ActivityTypeId = 1,
        ScheduledStart = new DateTime(2026, 6, 14),
        ScheduledEnd = new DateTime(2026, 6, 14)
    };

    private static IReadOnlyList<ValidationResult> Validate(EditModel.InputModel input)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(input, new ValidationContext(input), results, validateAllProperties: true);
        return results;
    }
}
