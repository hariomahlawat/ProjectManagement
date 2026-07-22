namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public interface IProjectBriefingSlideComposer
{
    (byte[] Content, int SlideCount) Compose(ProjectBriefingPresentationData data);
}
