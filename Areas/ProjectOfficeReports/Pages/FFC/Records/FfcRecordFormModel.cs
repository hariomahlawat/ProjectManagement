using Microsoft.AspNetCore.Mvc.Rendering;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Records;

public record FfcRecordFormModel(ManageModel.InputModel Input, SelectList CountryList, bool IsEdit);
