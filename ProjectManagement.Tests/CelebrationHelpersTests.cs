using System;
using ProjectManagement.Helpers;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests
{
    public class CelebrationHelpersTests
    {
        [Fact]
        public void LeapDay_OnNonLeapYear_ShowsFeb28()
        {
            var c = new Celebration { Day = 29, Month = 2 };
            var today = new DateOnly(2023, 2, 28);
            var next = CelebrationHelpers.NextOccurrenceLocal(c, today);
            Assert.Equal(new DateOnly(2023, 2, 28), next);
        }

        [Fact]
        public void LeapDay_AfterFeb_InNonLeapYear_GoesToNextLeap()
        {
            var c = new Celebration { Day = 29, Month = 2 };
            var today = new DateOnly(2023, 3, 1);
            var next = CelebrationHelpers.NextOccurrenceLocal(c, today);
            Assert.Equal(new DateOnly(2024, 2, 29), next);
        }

        [Fact]
        public void NormalDate_PastThisYear_GoesNextYear()
        {
            var c = new Celebration { Day = 1, Month = 1 };
            var today = new DateOnly(2023, 2, 1);
            var next = CelebrationHelpers.NextOccurrenceLocal(c, today);
            Assert.Equal(new DateOnly(2024, 1, 1), next);
        }

        [Fact]
        public void DaysAway_ComputesCorrectly()
        {
            var today = new DateOnly(2023, 2, 1);
            var next = new DateOnly(2023, 2, 3);
            var diff = CelebrationHelpers.DaysAway(today, next);
            Assert.Equal(2, diff);
        }
    }
}
