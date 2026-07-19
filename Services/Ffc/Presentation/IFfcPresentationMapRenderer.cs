using System.Collections.Generic;

namespace ProjectManagement.Services.Ffc.Presentation;

public interface IFfcPresentationMapRenderer
{
    byte[] Render(IReadOnlyList<FfcPresentationCountry> countries, int width = 1800, int height = 1180);
}
