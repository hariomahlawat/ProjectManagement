using System;
using System.Collections.Generic;
using ProjectManagement.Models.Execution;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

public class OngoingProjectsReadServiceTests
{
    [Fact]
    public void ResolveStageMilestoneDate_ReturnsDate_WhenStageCompleted()
    {
        // SECTION: Arrange
        var stages = new List<OngoingProjectStageDto>
        {
            new()
            {
                Code = "IPA",
                Status = StageStatus.Completed,
                ActualCompletedOn = new DateOnly(2024, 6, 10)
            }
        };

        // SECTION: Act
        var result = OngoingProjectsReadService.ResolveStageMilestoneDate(stages, "IPA");

        // SECTION: Assert
        Assert.Equal(new DateOnly(2024, 6, 10), result);
    }

    [Fact]
    public void ResolveStageMilestoneDate_ReturnsNull_WhenStageNotCompleted()
    {
        // SECTION: Arrange
        var stages = new List<OngoingProjectStageDto>
        {
            new()
            {
                Code = "IPA",
                Status = StageStatus.InProgress,
                ActualCompletedOn = new DateOnly(2024, 6, 10)
            }
        };

        // SECTION: Act
        var result = OngoingProjectsReadService.ResolveStageMilestoneDate(stages, "IPA");

        // SECTION: Assert
        Assert.Null(result);
    }

    [Fact]
    public void ResolveStageMilestoneDate_ReturnsNull_WhenStageMissing()
    {
        // SECTION: Arrange
        var stages = new List<OngoingProjectStageDto>
        {
            new()
            {
                Code = "AON",
                Status = StageStatus.Completed,
                ActualCompletedOn = new DateOnly(2024, 6, 11)
            }
        };

        // SECTION: Act
        var result = OngoingProjectsReadService.ResolveStageMilestoneDate(stages, "IPA");

        // SECTION: Assert
        Assert.Null(result);
    }
}
