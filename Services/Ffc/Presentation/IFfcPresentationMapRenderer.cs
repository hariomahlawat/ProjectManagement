using System.Collections.Generic;

namespace ProjectManagement.Services.Ffc.Presentation;

public interface IFfcPresentationMapRenderer
{
    byte[] Render(IReadOnlyList<FfcPresentationCountry> countries, int width = 1800, int height = 1180);

    /// <summary>
    /// Renders a tighter active-country viewport for embedded briefing slides. Implementations
    /// that do not provide a specialised view fall back to the standard renderer.
    /// </summary>
    byte[] RenderFocused(IReadOnlyList<FfcPresentationCountry> countries, int width = 1800, int height = 1180)
        => Render(countries, width, height);
}
