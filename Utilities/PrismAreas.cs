namespace ProjectManagement.Utilities
{
    // ===========================
    // PRISM AREAS
    // The six areas of the platform, one per ray of the PRISM mark, in refraction order (top ray
    // to bottom ray), leading with the project record. Drawn by Pages/Shared/_PrismSpectrum.cshtml
    // and listed on About PRISM; keep the copy true to the current modules (docs/ux/public-pages.md).
    // ===========================
    public static class PrismAreas
    {
        public static IReadOnlyList<(string Name, string Detail)> All { get; } = new[]
        {
            ("Projects", "Stages, plans and facts"),
            ("Desks", "Command and briefing desks"),
            ("Teamwork", "Chat, tasks, notebook, calendar"),
            ("Office records", "Visits, training, IPR, ToT, FFC"),
            ("Media", "Photos, videos, publications"),
            ("Documents", "Repository, AOTS, OCR search"),
        };
    }
}
