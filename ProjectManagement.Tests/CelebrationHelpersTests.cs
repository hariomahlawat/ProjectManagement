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

        [Fact]
        public void DisplayName_UsesTheAccountNameForALinkedEntry_AndTheWrittenNameOtherwise()
        {
            var names = new System.Collections.Generic.Dictionary<string, string> { ["u1"] = "Col R Sharma" };
            var linked = new Celebration { EventType = CelebrationType.Anniversary, Name = "Maj Sharma", SpouseName = "Anita", UserId = "u1" };
            var gone = new Celebration { EventType = CelebrationType.Birthday, Name = "Maj Rao", UserId = "u-gone" };
            var unlinked = new Celebration { EventType = CelebrationType.Birthday, Name = "Mrs Verma" };

            Assert.Equal("Col R Sharma & Anita", CelebrationHelpers.DisplayName(linked, names));
            Assert.Equal("Maj Sharma & Anita", CelebrationHelpers.DisplayName(linked));
            Assert.Equal("Maj Rao", CelebrationHelpers.DisplayName(gone, names));
            Assert.Equal("Mrs Verma", CelebrationHelpers.DisplayName(unlinked, names));
        }
    }
}
