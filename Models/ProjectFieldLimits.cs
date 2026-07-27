namespace ProjectManagement.Models
{
    public static class ProjectFieldLimits
    {
        // SECTION: Project description
        public const int DescriptionMaxLength = 5000;

        // SECTION: Project brief
        // The character limit protects the database and accommodates normal
        // editorial variation; the application separately enforces 250 words.
        public const int ProjectBriefMaxLength = 2500;
        public const int ProjectBriefRecommendedMinimumWords = 200;
        public const int ProjectBriefMaximumWords = 250;

        // SECTION: Capability overview
        public const int CapabilityStatementMaxLength = 500;
        public const int CapabilityRecommendedMinimumCount = 5;
        public const int CapabilityMaximumCount = 8;
    }
}
