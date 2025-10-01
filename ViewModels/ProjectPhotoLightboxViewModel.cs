using System;
using System.Collections.Generic;
using ProjectManagement.Models;

namespace ProjectManagement.ViewModels
{
    public class ProjectPhotoLightboxViewModel
    {
        public ProjectPhotoLightboxViewModel(
            int projectId,
            string projectName,
            IReadOnlyList<ProjectPhoto> photos,
            int? coverPhotoId,
            int? coverPhotoVersion,
            string modalId)
        {
            ProjectId = projectId;
            ProjectName = projectName;
            Photos = photos ?? Array.Empty<ProjectPhoto>();
            CoverPhotoId = coverPhotoId;
            CoverPhotoVersion = coverPhotoVersion;
            ModalId = string.IsNullOrWhiteSpace(modalId) ? $"project-gallery-modal-{projectId}" : modalId;
        }

        public int ProjectId { get; }

        public string ProjectName { get; }

        public IReadOnlyList<ProjectPhoto> Photos { get; }

        public int? CoverPhotoId { get; }

        public int? CoverPhotoVersion { get; }

        public string ModalId { get; }

        public int GetVersion(ProjectPhoto photo)
        {
            if (photo is null)
            {
                throw new ArgumentNullException(nameof(photo));
            }

            if (CoverPhotoId.HasValue && photo.Id == CoverPhotoId.Value && CoverPhotoVersion.HasValue)
            {
                return CoverPhotoVersion.Value;
            }

            return photo.Version;
        }
    }
}
