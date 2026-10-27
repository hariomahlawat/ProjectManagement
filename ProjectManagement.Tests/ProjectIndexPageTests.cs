using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Pages.Projects;
using ProjectManagement.Services;
using ProjectManagement.Services.Analytics;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests
{
    public class ProjectIndexPageTests
    {
        [Fact]
        public async Task Pagination_SurfacesOlderProjectsOnLaterPages()
        {
            await using var context = CreateContext();
            var baseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            for (var i = 0; i < 25; i++)
            {
                context.Projects.Add(new Project
                {
                    Name = $"Project {i:D2}",
                    CreatedAt = baseDate.AddDays(i),
                    CreatedByUserId = "creator"
                });
            }

            await context.SaveChangesAsync();

            var firstPage = CreateModel(context);
            firstPage.CurrentPage = 1;
            firstPage.PageSize = 10;
            await firstPage.OnGetAsync();

            var secondPage = CreateModel(context);
            secondPage.CurrentPage = 2;
            secondPage.PageSize = 10;
            await secondPage.OnGetAsync();

            Assert.Equal(10, firstPage.Projects.Count);
            Assert.Equal(10, secondPage.Projects.Count);
            Assert.True(firstPage.Projects.First().CreatedAt > secondPage.Projects.First().CreatedAt);

            var thirdPage = CreateModel(context);
            thirdPage.CurrentPage = 3;
            thirdPage.PageSize = 10;
            await thirdPage.OnGetAsync();

            Assert.Equal(5, thirdPage.Projects.Count);
            Assert.Contains(thirdPage.Projects, p => p.Name == "Project 00");
        }

        [Fact]
        public async Task Search_ReordersResultsByRelevance()
        {
            await using var context = CreateContext();
            var baseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var caseMatch = new Project
            {
                Name = "Legacy capital works",
                CaseFileNumber = "ALPHA100",
                CreatedAt = baseDate.AddDays(1),
                CreatedByUserId = "creator"
            };
            var nameOlder = new Project
            {
                Name = "Alpha100 legacy",
                CreatedAt = baseDate.AddDays(2),
                CreatedByUserId = "creator"
            };
            var nameNewer = new Project
            {
                Name = "Alpha100 expansion",
                CreatedAt = baseDate.AddDays(3),
                CreatedByUserId = "creator"
            };
            var unrelated = new Project
            {
                Name = "Harbour renewal",
                CreatedAt = baseDate.AddDays(4),
                CreatedByUserId = "creator"
            };

            context.Projects.AddRange(caseMatch, nameOlder, nameNewer, unrelated);
            await context.SaveChangesAsync();

            var model = CreateModel(context);
            model.Query = "alpha100";
            model.PageSize = 10;

            await model.OnGetAsync();

            Assert.Equal(3, model.Projects.Count);
            Assert.Equal(caseMatch.Id, model.Projects[0].Id);
            Assert.Equal(nameNewer.Id, model.Projects[1].Id);
            Assert.Equal(nameOlder.Id, model.Projects[2].Id);
        }

        [Fact]
        public async Task OnGet_FiltersProjectsByLifecycle()
        {
            await using var context = CreateContext();

            var active = new Project
            {
                Name = "Active",
                LifecycleStatus = ProjectLifecycleStatus.Active,
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow
            };

            var completed = new Project
            {
                Name = "Completed",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow
            };

            context.Projects.AddRange(active, completed);
            await context.SaveChangesAsync();

            var model = CreateModel(context);
            model.Lifecycle = ProjectLifecycleFilter.Completed;

            await model.OnGetAsync();

            Assert.Single(model.Projects);
            Assert.All(model.Projects, p => Assert.Equal(ProjectLifecycleStatus.Completed, p.LifecycleStatus));
        }

        [Fact]
        public async Task LifecycleTabs_HighlightSelectedFilter()
        {
            await using var context = CreateContext();

            context.Projects.Add(new Project
            {
                Name = "Only",
                LifecycleStatus = ProjectLifecycleStatus.Active,
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            var model = CreateModel(context);
            model.Lifecycle = ProjectLifecycleFilter.Cancelled;

            await model.OnGetAsync();

            Assert.Contains(model.LifecycleTabs, tab => tab.Filter == ProjectLifecycleFilter.Cancelled && tab.IsActive);
            Assert.Contains(model.LifecycleTabs, tab => tab.Filter == ProjectLifecycleFilter.All && !tab.IsActive);
        }

        [Fact]
        public async Task LegacyLink_OpensCompletedWithLegacyEntriesSelected()
        {
            await using var context = CreateContext();

            context.Projects.AddRange(
                new Project
                {
                    Name = "Legacy",
                    LifecycleStatus = ProjectLifecycleStatus.Completed,
                    IsLegacy = true,
                    CreatedByUserId = "creator",
                    CreatedAt = DateTime.UtcNow
                },
                new Project
                {
                    Name = "Recorded",
                    LifecycleStatus = ProjectLifecycleStatus.Completed,
                    CreatedByUserId = "creator",
                    CreatedAt = DateTime.UtcNow
                });
            await context.SaveChangesAsync();

            var model = CreateModel(context);
            model.Lifecycle = ProjectLifecycleFilter.Legacy;

            await model.OnGetAsync();

            Assert.Equal(ProjectLifecycleFilter.Completed, model.Lifecycle);
            Assert.Equal(ProjectRegisterRecord.Legacy, model.Record);
            Assert.True(model.IsCompletedRegister);
            Assert.Contains(model.LifecycleTabs, tab => tab.Filter == ProjectLifecycleFilter.Completed && tab.IsActive);
            Assert.DoesNotContain(model.LifecycleTabs, tab => tab.Filter == ProjectLifecycleFilter.Legacy);
            Assert.Equal(new[] { "Legacy" }, model.Projects.Select(p => p.Name));
            Assert.Equal((1, 1), (model.RecordPrismCount, model.RecordLegacyCount));
            Assert.Equal("Completed · Legacy entries", model.ActiveLifecycleLabel);
            Assert.Equal("Legacy", model.BuildBaseRouteValues()["Record"]);

            var prism = CreateModel(context);
            prism.Lifecycle = ProjectLifecycleFilter.Completed;
            prism.Record = ProjectRegisterRecord.Prism;
            await prism.OnGetAsync();
            Assert.Equal(new[] { "Recorded" }, prism.Projects.Select(p => p.Name));
            Assert.All(prism.Projects, project => Assert.Equal("Recorded in PRISM", prism.ClosedRegisterRows[project.Id].RecordText));

            // The record filter belongs to the Completed tab.
            var all = CreateModel(context);
            all.Record = ProjectRegisterRecord.Legacy;
            await all.OnGetAsync();
            Assert.Null(all.Record);
            Assert.Equal(2, all.FilteredTotal);
        }

        [Fact]
        public async Task LifecycleTabs_DisplayCounts()
        {
            await using var context = CreateContext();

            var now = DateTime.UtcNow;

            context.Projects.AddRange(
                new Project
                {
                    Name = "Active",
                    LifecycleStatus = ProjectLifecycleStatus.Active,
                    CreatedByUserId = "creator",
                    CreatedAt = now
                },
                new Project
                {
                    Name = "Completed",
                    LifecycleStatus = ProjectLifecycleStatus.Completed,
                    CreatedByUserId = "creator",
                    CreatedAt = now
                },
                new Project
                {
                    Name = "Cancelled",
                    LifecycleStatus = ProjectLifecycleStatus.Cancelled,
                    CreatedByUserId = "creator",
                    CreatedAt = now
                },
                new Project
                {
                    Name = "Legacy",
                    LifecycleStatus = ProjectLifecycleStatus.Completed,
                    IsLegacy = true,
                    CreatedByUserId = "creator",
                    CreatedAt = now
                });

            await context.SaveChangesAsync();

            var model = CreateModel(context);

            await model.OnGetAsync();

            Assert.Equal(4, model.LifecycleTabs.Single(tab => tab.Filter == ProjectLifecycleFilter.All).Count);
            Assert.Equal(1, model.LifecycleTabs.Single(tab => tab.Filter == ProjectLifecycleFilter.Active).Count);
            Assert.Equal(2, model.LifecycleTabs.Single(tab => tab.Filter == ProjectLifecycleFilter.Completed).Count);
            Assert.Equal(1, model.LifecycleTabs.Single(tab => tab.Filter == ProjectLifecycleFilter.Cancelled).Count);
            Assert.DoesNotContain(model.LifecycleTabs, tab => tab.Filter == ProjectLifecycleFilter.Legacy);
            Assert.Equal(
                new[] { ProjectLifecycleFilter.Active, ProjectLifecycleFilter.Completed, ProjectLifecycleFilter.Cancelled, ProjectLifecycleFilter.All },
                model.LifecycleTabs.Select(tab => tab.Filter));
        }

        [Fact]
        public async Task OnGet_FiltersProjectsByTechnicalCategory()
        {
            await using var context = CreateContext();

            var techA = new TechnicalCategory { Name = "Infrastructure" };
            var techB = new TechnicalCategory { Name = "Applications" };

            context.TechnicalCategories.AddRange(techA, techB);
            await context.SaveChangesAsync();

            var projectA = new Project
            {
                Name = "Tech A",
                TechnicalCategoryId = techA.Id,
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow
            };

            var projectB = new Project
            {
                Name = "Tech B",
                TechnicalCategoryId = techB.Id,
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow
            };

            context.Projects.AddRange(projectA, projectB);
            await context.SaveChangesAsync();

            var model = CreateModel(context);
            model.TechnicalCategoryId = techA.Id;

            await model.OnGetAsync();

            Assert.Single(model.Projects);
            Assert.Equal(projectA.Id, model.Projects[0].Id);
        }

        [Fact]
        public async Task OnGet_ExcludesTrashedProjects()
        {
            await using var context = CreateContext();

            var now = DateTime.UtcNow;

            context.Projects.AddRange(
                new Project
                {
                    Name = "Active",
                    CreatedByUserId = "creator",
                    CreatedAt = now
                },
                new Project
                {
                    Name = "Archived",
                    CreatedByUserId = "creator",
                    CreatedAt = now.AddDays(-1),
                    IsArchived = true
                },
                new Project
                {
                    Name = "Trashed",
                    CreatedByUserId = "creator",
                    CreatedAt = now.AddDays(-2),
                    IsDeleted = true,
                    DeletedAt = now.AddDays(-2)
                });

            await context.SaveChangesAsync();

            var defaultModel = CreateModel(context);
            defaultModel.PageSize = 10;

            await defaultModel.OnGetAsync();

            Assert.Single(defaultModel.Projects);
            Assert.All(defaultModel.Projects, p => Assert.False(p.IsDeleted));

            var includeArchivedModel = CreateModel(context);
            includeArchivedModel.PageSize = 10;
            includeArchivedModel.IncludeArchived = true;

            await includeArchivedModel.OnGetAsync();

            Assert.Equal(2, includeArchivedModel.Projects.Count);
            Assert.Contains(includeArchivedModel.Projects, p => p.IsArchived);
            Assert.DoesNotContain(includeArchivedModel.Projects, p => p.IsDeleted);

            var lifecycleTab = includeArchivedModel.LifecycleTabs.Single(tab => tab.Filter == ProjectLifecycleFilter.All);
            Assert.Equal(2, lifecycleTab.Count);
        }

        [Fact]
        public async Task AnalyticsDrilldown_IncludesCategoryDescendants_WhenFlagged()
        {
            await using var context = CreateContext();

            var parent = new ProjectCategory { Name = "Parent" };
            var child = new ProjectCategory { Name = "Child", Parent = parent };
            var sibling = new ProjectCategory { Name = "Sibling" };

            context.ProjectCategories.AddRange(parent, child, sibling);
            await context.SaveChangesAsync();

            var now = DateTime.UtcNow;

            context.Projects.AddRange(
                new Project
                {
                    Name = "Parent Project",
                    CategoryId = parent.Id,
                    CreatedByUserId = "creator",
                    CreatedAt = now
                },
                new Project
                {
                    Name = "Child Project",
                    CategoryId = child.Id,
                    CreatedByUserId = "creator",
                    CreatedAt = now
                },
                new Project
                {
                    Name = "Sibling Project",
                    CategoryId = sibling.Id,
                    CreatedByUserId = "creator",
                    CreatedAt = now
                });

            await context.SaveChangesAsync();

            var withDescendants = CreateModel(context);
            withDescendants.CategoryId = parent.Id;
            withDescendants.IncludeCategoryDescendants = true;
            await withDescendants.OnGetAsync();

            Assert.Equal(2, withDescendants.Projects.Count);
            Assert.Contains(withDescendants.Projects, p => p.Name == "Parent Project");
            Assert.Contains(withDescendants.Projects, p => p.Name == "Child Project");

            var directOnly = CreateModel(context);
            directOnly.CategoryId = parent.Id;
            directOnly.IncludeCategoryDescendants = false;
            await directOnly.OnGetAsync();

            Assert.Single(directOnly.Projects);
            Assert.Equal("Parent Project", directOnly.Projects.Single().Name);
        }

        [Fact]
        public async Task UrlOnlyFilters_HaveChipLabels()
        {
            await using var context = CreateContext();
            var stages = new WorkflowStageMetadataProvider();

            var reached = CreateModel(context);
            reached.StageCode = "TEC";
            reached.SlipBucket = "8-30";
            await reached.OnGetAsync();

            Assert.Equal($"Reached {stages.GetDisplayName(null, "TEC")}", reached.StageFilterLabel);
            Assert.Equal("Slip: 8-30 days", reached.SlipBucketFilterLabel);

            var completedInMonth = CreateModel(context);
            completedInMonth.StageCode = "TEC";
            completedInMonth.StageCompletedMonth = "2026-09";
            await completedInMonth.OnGetAsync();

            Assert.Equal($"{stages.GetDisplayName(null, "TEC")} completed in Sep 2026", completedInMonth.StageFilterLabel);

            var none = CreateModel(context);
            none.StageCompletedMonth = "2026-09";
            none.IncludeCategoryDescendants = true;
            await none.OnGetAsync();

            // A month without a stage, or sub-categories without a category, does not filter.
            Assert.Null(none.StageFilterLabel);
            Assert.Null(none.SlipBucketFilterLabel);
            Assert.False(none.AppliesCategoryDescendants);
        }

        [Fact]
        public async Task ViewCookie_SelectsTheRenderedView()
        {
            await using var context = CreateContext();

            var withoutCookie = CreateModel(context);
            await withoutCookie.OnGetAsync();
            Assert.False(withoutCookie.ShowTableView);

            var table = CreateModel(context);
            table.HttpContext.Request.Headers["Cookie"] = $"{IndexModel.ViewCookieName}=table";
            await table.OnGetAsync();
            Assert.True(table.ShowTableView);

            var unknown = CreateModel(context);
            unknown.HttpContext.Request.Headers["Cookie"] = $"{IndexModel.ViewCookieName}=wall";
            await unknown.OnGetAsync();
            Assert.False(unknown.ShowTableView);
        }

        [Fact]
        public async Task OfficerAndHodOptions_ListEachPersonOnce()
        {
            await using var context = CreateContext();
            context.Users.AddRange(
                new ApplicationUser { Id = "hod-1", UserName = "hod1", FullName = "Col A Verma" },
                new ApplicationUser { Id = "po-1", UserName = "po1", FullName = "K Srinath" });
            for (var i = 0; i < 3; i++)
            {
                context.Projects.Add(new Project
                {
                    Name = $"Project {i}",
                    HodUserId = "hod-1",
                    LeadPoUserId = "po-1",
                    CreatedByUserId = "creator",
                    CreatedAt = DateTime.UtcNow
                });
            }

            await context.SaveChangesAsync();

            var model = CreateModel(context);
            await model.OnGetAsync();

            Assert.Equal(new[] { "Any HoD", "Col A Verma" }, model.HodOptions.Select(o => o.Text));
            Assert.Equal(new[] { "Any Project Officer", "K Srinath" }, model.LeadPoOptions.Select(o => o.Text));
        }

        [Fact]
        public async Task ActiveRegister_OrdersBlockedThenMostOverdueAcrossPages()
        {
            await using var context = CreateContext();
            // Read on 1 Jan 2024 (TestClock). Each project is in its first stage (FS).
            AddActive(context, 1, "Alpha on plan", StageStatus.InProgress, new DateOnly(2023, 12, 20), due: new DateOnly(2024, 1, 20));
            AddActive(context, 2, "Bravo late 10", StageStatus.InProgress, new DateOnly(2023, 11, 1), due: new DateOnly(2023, 12, 22));
            AddActive(context, 3, "Charlie late 40", StageStatus.InProgress, new DateOnly(2023, 11, 1), due: new DateOnly(2023, 11, 22));
            AddActive(context, 4, "Delta blocked", StageStatus.Blocked, new DateOnly(2023, 12, 1), due: null);
            AddActive(context, 5, "Echo no PDC", StageStatus.InProgress, new DateOnly(2023, 12, 1), due: null);
            await context.SaveChangesAsync();

            var pages = new List<string>();
            for (var page = 1; page <= 3; page++)
            {
                var model = CreateModel(context);
                model.Lifecycle = ProjectLifecycleFilter.Active;
                model.PageSize = 2;
                model.CurrentPage = page;
                await model.OnGetAsync();

                Assert.True(model.IsActiveRegister);
                Assert.Equal("Blocked first, then most overdue", model.OrderDescription);
                Assert.All(model.Projects, project => Assert.True(model.ActiveRegisterRows.ContainsKey(project.Id)));
                pages.AddRange(model.Projects.Select(project => project.Name));
            }

            Assert.Equal(
                new[] { "Delta blocked", "Charlie late 40", "Bravo late 10", "Alpha on plan", "Echo no PDC" },
                pages);

            // An explicit sort still orders the whole result by that column.
            var byName = CreateModel(context);
            byName.Lifecycle = ProjectLifecycleFilter.Active;
            byName.Sort = ProjectRepositorySort.Project;
            await byName.OnGetAsync();
            Assert.Equal("Alpha on plan", byName.Projects.First().Name);

            // Other tabs keep the existing table and order.
            var all = CreateModel(context);
            await all.OnGetAsync();
            Assert.False(all.IsActiveRegister);
            Assert.Empty(all.ActiveRegisterRows);
        }

        [Fact]
        public async Task ActiveRegister_PipelineFiltersNarrowTheListAndOnlyApplyOnActive()
        {
            await using var context = CreateContext();
            AddActive(context, 1, "Alpha on plan", StageStatus.InProgress, new DateOnly(2023, 12, 20), due: new DateOnly(2024, 1, 20));
            AddActive(context, 2, "Bravo late", StageStatus.InProgress, new DateOnly(2023, 11, 1), due: new DateOnly(2023, 12, 22));
            AddActive(context, 3, "Charlie blocked", StageStatus.Blocked, new DateOnly(2023, 12, 1), due: null);
            await context.SaveChangesAsync();

            var all = CreateModel(context);
            all.Lifecycle = ProjectLifecycleFilter.Active;
            await all.OnGetAsync();
            Assert.NotNull(all.Pipeline);
            Assert.Equal(3, all.Pipeline!.Stages.Single(stage => stage.Code == StageCodes.FS).Total);
            Assert.Equal(1, all.Pipeline.Attention.Single(item => item.Attention == ProjectRegisterAttention.Late).Count);

            var late = CreateModel(context);
            late.Lifecycle = ProjectLifecycleFilter.Active;
            late.Attention = ProjectRegisterAttention.Late;
            await late.OnGetAsync();
            Assert.Equal(new[] { "Bravo late" }, late.Projects.Select(p => p.Name));
            Assert.Equal(1, late.FilteredTotal);
            Assert.Equal("Late against plan", late.AttentionFilterLabel);
            Assert.Equal("Late", late.BuildBaseRouteValues()["Attention"]);

            var inFs = CreateModel(context);
            inFs.Lifecycle = ProjectLifecycleFilter.Active;
            inFs.CurrentStage = "fs";
            await inFs.OnGetAsync();
            Assert.Equal(3, inFs.FilteredTotal);
            Assert.Equal("FS", inFs.CurrentStage);
            Assert.StartsWith("Now in ", inFs.CurrentStageFilterLabel);

            var inDevp = CreateModel(context);
            inDevp.Lifecycle = ProjectLifecycleFilter.Active;
            inDevp.CurrentStage = StageCodes.DEVP;
            await inDevp.OnGetAsync();
            Assert.Equal(0, inDevp.FilteredTotal);
            Assert.NotNull(inDevp.Pipeline);

            // Outside the Active tab the pipeline filters are dropped.
            var allTab = CreateModel(context);
            allTab.Attention = ProjectRegisterAttention.Late;
            allTab.CurrentStage = StageCodes.DEVP;
            await allTab.OnGetAsync();
            Assert.Null(allTab.Pipeline);
            Assert.Null(allTab.Attention);
            Assert.Null(allTab.CurrentStage);
            Assert.Equal(3, allTab.FilteredTotal);
        }

        [Fact]
        public async Task QuickFilters_CountUnderEveryOtherFilterAndChipsRemoveOneFilter()
        {
            await using var context = CreateContext();
            context.Users.AddRange(
                new ApplicationUser { Id = "po-a", UserName = "poa", FullName = "Officer A" },
                new ApplicationUser { Id = "po-b", UserName = "pob", FullName = "Officer B" },
                new ApplicationUser { Id = "hod-x", UserName = "hodx", FullName = "HoD X" });
            var catA = new ProjectCategory { Name = "Cat A" };
            var catB = new ProjectCategory { Name = "Cat B" };
            context.ProjectCategories.AddRange(catA, catB);
            await context.SaveChangesAsync();

            void Add(string name, string po, ProjectCategory category, bool repeat) => context.Projects.Add(new Project
            {
                Name = name,
                LeadPoUserId = po,
                HodUserId = "hod-x",
                CategoryId = category.Id,
                IsBuild = repeat,
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow
            });

            Add("One", "po-a", catA, repeat: false);
            Add("Two", "po-a", catB, repeat: true);
            Add("Three", "po-b", catA, repeat: false);
            await context.SaveChangesAsync();

            var model = CreateModel(context);
            model.LeadPoUserId = "po-a";
            model.CategoryId = catA.Id;
            await model.OnGetAsync();

            Assert.Equal(new[] { "One" }, model.Projects.Select(p => p.Name));

            // Officer counts ignore the officer choice but follow the category: both officers have one in Cat A.
            var officers = model.QuickFacets.Single(f => f.Key == "LeadPoUserId");
            Assert.Equal(new[] { ("Officer A", 1, true), ("Officer B", 1, false) },
                officers.Options.Where(o => o.Value is not null).Select(o => (o.Label, o.Count!.Value, o.IsSelected)));
            Assert.Equal("Officer A", officers.Selected!.Label);

            // Category counts ignore the category choice but follow the officer: A has one in each.
            var categories = model.QuickFacets.Single(f => f.Key == "CategoryId");
            Assert.Equal(new[] { ("Cat A", 1), ("Cat B", 1) },
                categories.Options.Where(o => o.Value is not null).Select(o => (o.Label, o.Count!.Value)));

            // Build counts follow both choices.
            var build = model.QuickFacets.Single(f => f.Key == "Build");
            Assert.Equal((1, 0), (build.Options.Single(o => o.Value == "New").Count!.Value, build.Options.Single(o => o.Value == "Repeat").Count!.Value));

            var officerChip = model.FilterChips.Single(c => c.Label == "Project Officer: Officer A");
            Assert.False(officerChip.RemoveRoute.ContainsKey("LeadPoUserId"));
            Assert.Equal(catA.Id.ToString(), officerChip.RemoveRoute["CategoryId"]);
            var categoryChip = model.FilterChips.Single(c => c.Label == "Category: Cat A");
            Assert.False(categoryChip.RemoveRoute.ContainsKey("CategoryId"));
            Assert.Equal("po-a", categoryChip.RemoveRoute["LeadPoUserId"]);
        }

        [Fact]
        public async Task QuickFilters_TechnicalCategoryMenuReplacesHodAndCountsUnderTheOtherFilters()
        {
            await using var context = CreateContext();
            var simulators = new TechnicalCategory { Name = "Simulators" };
            context.TechnicalCategories.Add(simulators);
            await context.SaveChangesAsync();
            var driving = new TechnicalCategory { Name = "Driving", ParentId = simulators.Id };
            var software = new TechnicalCategory { Name = "Software" };
            context.TechnicalCategories.AddRange(driving, software);
            await context.SaveChangesAsync();

            void Add(string name, string po, TechnicalCategory? technical) => context.Projects.Add(new Project
            {
                Name = name,
                LeadPoUserId = po,
                HodUserId = "hod-x",
                TechnicalCategoryId = technical?.Id,
                CreatedByUserId = "creator",
                CreatedAt = DateTime.UtcNow
            });

            Add("Tank driving sim", "po-a", driving);
            Add("Truck driving sim", "po-b", driving);
            Add("Range app", "po-a", software);
            Add("Untagged", "po-a", null);
            await context.SaveChangesAsync();

            var model = CreateModel(context);
            model.LeadPoUserId = "po-a";
            model.TechnicalCategoryId = driving.Id;
            await model.OnGetAsync();

            Assert.Equal(new[] { "Tank driving sim" }, model.Projects.Select(p => p.Name));
            Assert.Equal(new[] { "LeadPoUserId", "CategoryId", "TechnicalCategoryId", "Build" }, model.QuickFacets.Select(f => f.Key));

            // Counts ignore the technical category choice but follow the officer; a sub-category
            // is labelled with its parent.
            var technical = model.QuickFacets.Single(f => f.Key == "TechnicalCategoryId");
            Assert.Equal("Technical category", technical.Name);
            Assert.Equal(new[] { ("Simulators › Driving", 1, true), ("Software", 1, false) },
                technical.Options.Where(o => o.Value is not null).Select(o => (o.Label, o.Count!.Value, o.IsSelected)));
            Assert.Equal("Simulators › Driving", technical.Selected!.Label);

            var chip = model.FilterChips.Single(c => c.Label == "Technical category: Driving");
            Assert.False(chip.RemoveRoute.ContainsKey("TechnicalCategoryId"));
            Assert.Equal("po-a", chip.RemoveRoute["LeadPoUserId"]);

            // HoD is now a More filters choice: it still filters, counts on the More badge and has a chip.
            var withHod = CreateModel(context);
            withHod.HodUserId = "hod-x";
            await withHod.OnGetAsync();
            Assert.Equal(4, withHod.Projects.Count);
            Assert.Equal(1, withHod.MoreFiltersCount);
            Assert.Contains(withHod.FilterChips, c => c.Label.StartsWith("HoD:", StringComparison.Ordinal));
        }

        private static void AddActive(ApplicationDbContext context, int id, string name, StageStatus status, DateOnly start, DateOnly? due)
        {
            context.Projects.Add(new Project
            {
                Id = id,
                Name = name,
                CreatedByUserId = "creator",
                CreatedAt = new DateTime(2023, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                LifecycleStatus = ProjectLifecycleStatus.Active
            });
            context.ProjectStages.Add(new ProjectStage
            {
                ProjectId = id,
                StageCode = StageCodes.FS,
                SortOrder = 0,
                Status = status,
                ActualStart = start,
                PlannedDue = due
            });
        }

        [Fact]
        public async Task PlainProjectsUrl_OpensTheRememberedTabElseActive()
        {
            await using var context = CreateContext();
            context.Projects.AddRange(
                new Project { Name = "Running", LifecycleStatus = ProjectLifecycleStatus.Active, CreatedByUserId = "creator", CreatedAt = DateTime.UtcNow },
                new Project { Name = "Done", LifecycleStatus = ProjectLifecycleStatus.Completed, CreatedByUserId = "creator", CreatedAt = DateTime.UtcNow });
            await context.SaveChangesAsync();

            // First visit: no query string, no cookie → Active.
            var first = CreateModel(context, queryString: null);
            await first.OnGetAsync();
            Assert.Equal(ProjectLifecycleFilter.Active, first.Lifecycle);
            Assert.Equal(new[] { "Running" }, first.Projects.Select(p => p.Name));

            // Choosing a tab remembers it.
            var chosen = CreateModel(context, queryString: "?Lifecycle=Completed");
            chosen.Lifecycle = ProjectLifecycleFilter.Completed;
            await chosen.OnGetAsync();
            var setCookie = chosen.HttpContext.Response.Headers["Set-Cookie"].ToString();
            Assert.Contains($"{IndexModel.TabCookieName}=Completed", setCookie);
            Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);

            // A later plain visit opens the remembered tab.
            var back = CreateModel(context, queryString: null);
            back.HttpContext.Request.Headers["Cookie"] = $"{IndexModel.TabCookieName}=Completed";
            await back.OnGetAsync();
            Assert.Equal(ProjectLifecycleFilter.Completed, back.Lifecycle);

            // An unreadable or retired value falls back to Active.
            var legacy = CreateModel(context, queryString: null);
            legacy.HttpContext.Request.Headers["Cookie"] = $"{IndexModel.TabCookieName}=Legacy";
            await legacy.OnGetAsync();
            Assert.Equal(ProjectLifecycleFilter.Active, legacy.Lifecycle);

            // A link with any parameter keeps its meaning (All unless it names a tab) and is not remembered.
            var drillDown = CreateModel(context, queryString: "?StageCompletedFrom=2023-01-01");
            drillDown.HttpContext.Request.Headers["Cookie"] = $"{IndexModel.TabCookieName}=Completed";
            await drillDown.OnGetAsync();
            Assert.Equal(ProjectLifecycleFilter.All, drillDown.Lifecycle);
            Assert.Empty(drillDown.HttpContext.Response.Headers["Set-Cookie"].ToString());

            // The All tab is explicit in links, so choosing it is remembered too.
            Assert.Equal("All", drillDown.BuildBaseRouteValues()["Lifecycle"]);
            Assert.Equal("All", drillDown.LifecycleTabs.Single(tab => tab.Filter == ProjectLifecycleFilter.All).RouteValue);
        }

        private static IndexModel CreateModel(ApplicationDbContext context, string? queryString = "?p=1")
        {
            var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            if (queryString is not null)
            {
                // Tests set the filters on the model; a query string keeps the plain-URL default tab out of the way.
                httpContext.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString(queryString);
            }

            var categories = new ProjectCategoryHierarchyService(context);
            var clock = new TestClock();
            var stages = new WorkflowStageMetadataProvider();
            var analytics = new ProjectAnalyticsService(context, clock, categories, stages);
            var registerFacts = new ProjectRegisterFactsService(
                context,
                new OngoingProjectsReadService(context, stages, clock),
                clock);
            return new IndexModel(context, analytics, categories, stages, registerFacts)
            {
                PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext
                {
                    HttpContext = httpContext
                }
            };
        }

        private static ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }

        private sealed class TestClock : IClock
        {
            public DateTimeOffset UtcNow { get; } = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }
    }
}
