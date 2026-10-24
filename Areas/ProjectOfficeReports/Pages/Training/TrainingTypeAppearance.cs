using System;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Training;

// -----------------------------------------------------------------------------
// Colour key for a training type, shared by its tag, headline figure and chart bar.
// Simulator and Drone have their own colours; any other type uses a neutral one.
// Tokens: --training-type-* in wwwroot/css/project-office-reports/training/manage.css
// -----------------------------------------------------------------------------
public static class TrainingTypeAppearance
{
    public static string Modifier(string? typeName)
    {
        if (string.Equals(typeName, "Simulator", StringComparison.OrdinalIgnoreCase))
        {
            return "simulator";
        }

        if (string.Equals(typeName, "Drone", StringComparison.OrdinalIgnoreCase))
        {
            return "drone";
        }

        return "other";
    }
}
