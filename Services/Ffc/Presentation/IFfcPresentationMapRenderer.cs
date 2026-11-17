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

    /// <summary>
    /// A wide band for the briefing deck's FFC slide: the active countries fill the band, coloured
    /// for the deck's theme, each labelled with its name and quantity; no legend is drawn (the
    /// slide's table carries the figures). Implementations without one fall back to the focused map.
    /// </summary>
    byte[] RenderBriefingBand(IReadOnlyList<FfcPresentationCountry> countries, int width, int height, bool dark)
        => RenderFocused(countries, width, height);
}
