using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Api;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests
{
    // SECTION: Proliferation report export tests
    public sealed class ProliferationReportsServiceExportTests
    {
        [Fact]
        public async Task ExportAsync_GranularLedger_WritesTypedDateAndNumberAndFriendlyFilters()
        {
            // SECTION: Arrange
            using var context = CreateContext();

            var projectCategory = new ProjectCategory
            {
                Id = 1,
                Name = "Readiness",
                SortOrder = 1,
                IsActive = true,
                CreatedByUserId = "seed",
                CreatedAt = DateTime.UtcNow
            };

            var technicalCategory = new TechnicalCategory
            {
                Id = 7,
                Name = "Simulation",
                SortOrder = 1,
                IsActive = true,
                CreatedByUserId = "seed",
                CreatedAt = DateTime.UtcNow
            };

            var project = new Project
            {
                Id = 42,
                Name = "Simulator Expansion",
                CaseFileNumber = "SIM-042",
                CreatedByUserId = "seed",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                CategoryId = projectCategory.Id,
                TechnicalCategoryId = technicalCategory.Id
            };

            var granular = new ProliferationGranular
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Source = ProliferationSource.Sdd,
                UnitName = "Alpha",
                ProliferationDate = new DateOnly(2024, 3, 15),
                Quantity = 70,
                ApprovalStatus = ApprovalStatus.Approved,
                SubmittedByUserId = "seed",
                CreatedOnUtc = DateTime.UtcNow,
                LastUpdatedOnUtc = DateTime.UtcNow,
                RowVersion = Guid.NewGuid().ToByteArray()
            };

            context.ProjectCategories.Add(projectCategory);
            context.TechnicalCategories.Add(technicalCategory);
            context.Projects.Add(project);
            context.ProliferationGranularEntries.Add(granular);
            await context.SaveChangesAsync();

            var service = new ProliferationReportsService(context, new ProliferationReportExcelWorkbookBuilder());

            // SECTION: Act
            var (contentBytes, fileName) = await service.ExportAsync(new ProliferationReportQueryDto
            {
                Report = ProliferationReportKind.GranularLedger,
                Source = ProliferationSource.Sdd,
                ProjectId = project.Id,
                ProjectCategoryId = projectCategory.Id,
                TechnicalCategoryId = technicalCategory.Id,
                FromDateUtc = new DateTime(2024, 1, 1),
                ToDateUtc = new DateTime(2024, 12, 31),
                ApprovalStatus = "Approved",
                Page = 1,
                PageSize = 50
            }, CancellationToken.None);

            // SECTION: Assert (file)
            Assert.NotNull(contentBytes);
            Assert.NotEmpty(contentBytes);
            Assert.EndsWith(".xlsx", fileName, StringComparison.OrdinalIgnoreCase);

            using var stream = new MemoryStream(contentBytes);
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheet("Report");

            // SECTION: Assert (filters)
            Assert.Equal("Readiness", FindFilterValue(sheet, "Project category"));
            Assert.Equal("Simulation", FindFilterValue(sheet, "Technical category"));
            Assert.Equal("Simulator Expansion (SIM-042)", FindFilterValue(sheet, "Project"));
            Assert.Equal("SDD", FindFilterValue(sheet, "Source"));

            // SECTION: Assert (typed cells)
            var headerRow = FindHeaderRow(sheet, "Project", "Proliferation date");
            Assert.True(headerRow > 0);

            var dataRow = headerRow + 1;

            Assert.Equal("Simulator Expansion", sheet.Cell(dataRow, 1).GetString());
            Assert.Equal("SIM-042", sheet.Cell(dataRow, 2).GetString());
            Assert.Equal("SDD", sheet.Cell(dataRow, 3).GetString());

            Assert.Equal(new DateTime(2024, 3, 15), sheet.Cell(dataRow, 4).GetDateTime().Date);
            Assert.Equal("Alpha", sheet.Cell(dataRow, 5).GetString());
            Assert.Equal(70, sheet.Cell(dataRow, 6).GetValue<int>());
            Assert.Equal("Approved", sheet.Cell(dataRow, 8).GetString());
        }

        // SECTION: Helpers
        private static string FindFilterValue(IXLWorksheet sheet, string key)
        {
            for (var r = 4; r <= 50; r++)
            {
                if (sheet.Cell(r, 1).GetString().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return sheet.Cell(r, 2).GetString();
                }
            }
            return string.Empty;
        }

        private static int FindHeaderRow(IXLWorksheet sheet, string col1, string col4)
        {
            for (var r = 1; r <= 80; r++)
            {
                if (sheet.Cell(r, 1).GetString() == col1 && sheet.Cell(r, 4).GetString() == col4)
                {
                    return r;
                }
            }
            return 0;
        }

        private static ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }
    }
}
