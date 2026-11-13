namespace ProjectManagement.Utilities;

/// <summary>
/// How Pages/Shared/_PrismSpectrum.cshtml draws the PRISM mark.
/// </summary>
/// <param name="ShowLabels">
/// True draws the ray labels (landing hero); false draws the bare artwork, whose rays run on
/// past the drawing (sign-in, About).
/// </param>
/// <param name="IdPrefix">
/// Prefix for the gradient, filter and title ids inside the SVG. A page that draws the mark
/// twice gives the second one its own prefix so the ids stay unique.
/// </param>
/// <param name="BeamStartX">
/// Where the incoming beam starts, in the artwork's units (the glass is the triangle
/// 300,90 / 180,430 / 420,430 and the beam meets it at 226,301). The default starts just
/// outside the drawing's left edge; the About page starts it at the archer's arrowhead.
/// </param>
/// <param name="BeamStartY">See <paramref name="BeamStartX"/>.</param>
public sealed record PrismSpectrumOptions(
    bool ShowLabels = false,
    string IdPrefix = "lpPrism",
    double BeamStartX = -10,
    double BeamStartY = 360);
