namespace ProjectManagement.Services.Storage;

public interface IUploadRootProvider
{
    string RootPath { get; }

    string GetProjectRoot(int projectId);

    string GetProjectPhotosRoot(int projectId);

    string GetProjectDocumentsRoot(int projectId);

    string GetProjectCommentsRoot(int projectId);

    string GetProjectVideosRoot(int projectId);
}
