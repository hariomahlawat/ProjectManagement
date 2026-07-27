namespace ProjectManagement.Models
{
    public static class ProjectFieldLimits
    {
        // SECTION: Project description
        public const int DescriptionMaxLength = 5000;
        public const int DescriptionPreviewCollapseWords = 260;

        // SECTION: Project brief
        // The database character limit accommodates normal editorial variation.
        // 1–49 words need expansion; 50–99 are concise; 100–150 are recommended.
        // The service applies a hard 200-word ceiling.
        public const int ProjectBriefMaxLength = 3000;
        public const int ProjectBriefConciseMinimumWords = 50;
        public const int ProjectBriefRecommendedMinimumWords = 100;
        public const int ProjectBriefRecommendedMaximumWords = 150;
        public const int ProjectBriefHardMaximumWords = 200;

        // Retained for compatibility with code added in Phase 11.
        public const int ProjectBriefMaximumWords = ProjectBriefHardMaximumWords;

        // SECTION: Capability overview
        public const int CapabilityStatementMaxLength = 500;
        public const int CapabilityRecommendedMinimumCount = 5;
        public const int CapabilityMaximumCount = 8;
    }
}
