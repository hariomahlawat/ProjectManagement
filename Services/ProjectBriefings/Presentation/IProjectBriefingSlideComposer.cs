namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public interface IProjectBriefingSlideComposer
{
    (byte[] Content, int SlideCount) Compose(ProjectBriefingPresentationData data);

    // The same composition, with what each slide is (role and project) in deck order.
    ProjectBriefingComposition ComposeWithOutline(ProjectBriefingPresentationData data);
}
