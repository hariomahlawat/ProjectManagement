namespace ProjectManagement.Models
{
    public static class ProjectFieldLimits
    {
        // SECTION: Project description
        public const int DescriptionMaxLength = 5000;
        public const int DescriptionPreviewCollapseWords = 260;

        // SECTION: Project brief
        // The database character limit accommodates normal editorial variation.
        // The UI recommends 200–250 words and the service applies a hard 300-word ceiling.
        public const int ProjectBriefMaxLength = 3000;
        public const int ProjectBriefIncompleteThresholdWords = 150;
        public const int ProjectBriefRecommendedMinimumWords = 200;
        public const int ProjectBriefRecommendedMaximumWords = 250;
        public const int ProjectBriefHardMaximumWords = 300;

        // Retained for compatibility with code added in Phase 11.
        public const int ProjectBriefMaximumWords = ProjectBriefHardMaximumWords;

        // SECTION: Capability overview
        public const int CapabilityStatementMaxLength = 500;
        public const int CapabilityRecommendedMinimumCount = 5;
        public const int CapabilityMaximumCount = 8;
    }
}
