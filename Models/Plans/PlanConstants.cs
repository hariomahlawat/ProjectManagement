using ProjectManagement.Models.Stages;

namespace ProjectManagement.Models.Plans;

public static class PlanConstants
{
    // SECTION: Stage Template Versions
    public const string StageTemplateVersionV1 = "SDD-1.0";
    public const string StageTemplateVersionV2 = "SDD-2.0";

    // SECTION: Defaults
    public const string DefaultStageTemplateVersion = StageTemplateVersionV2;
    public const string DefaultAnchorStageCode = StageCodes.IPA;
}
