using System;
using System.Collections.Generic;

namespace ProjectManagement.Services.Projects
{
    public interface IOngoingProjectsExcelBuilder
    {
        byte[] Build(OngoingProjectsExportContext context);
    }

    public sealed class OngoingProjectsExportContext
    {
        public OngoingProjectsExportContext(
            IReadOnlyList<OngoingProjectRowDto> items,
            DateTimeOffset generatedAtUtc,
            int? projectCategoryId,
            string? search)
        {
            Items = items;
            GeneratedAtUtc = generatedAtUtc;
            ProjectCategoryId = projectCategoryId;
            Search = search;
        }

        public IReadOnlyList<OngoingProjectRowDto> Items { get; }
        public DateTimeOffset GeneratedAtUtc { get; }
        public int? ProjectCategoryId { get; }
        public string? Search { get; }
    }
}
