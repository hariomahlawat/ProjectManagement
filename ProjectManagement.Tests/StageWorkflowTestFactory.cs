using ProjectManagement.Data;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Stages;

namespace ProjectManagement.Tests;

internal static class StageWorkflowTestFactory
{
    public static IProjectStageWorkflowPolicy CreatePolicy(ApplicationDbContext db) =>
        new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider());
}
