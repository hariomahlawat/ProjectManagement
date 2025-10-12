using System;
using System.Collections.Generic;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Visits;

public sealed record VisitPhotosViewModel(Guid VisitId, IReadOnlyList<VisitPhoto> Photos, Guid? CoverPhotoId, bool CanManage);
