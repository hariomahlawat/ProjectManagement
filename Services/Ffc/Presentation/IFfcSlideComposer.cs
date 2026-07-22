namespace ProjectManagement.Services.Ffc.Presentation;

public interface IFfcSlideComposer
{
    (byte[] Content, int SlideCount) Compose(FfcPresentationData data);
}
