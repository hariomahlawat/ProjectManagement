namespace ProjectManagement.Application.Ipr;

public enum IprValidationCode
{
    FilingNumberRequired = 1,
    TitleRequired = 2,
    DuplicateFilingNumber = 3,
    FiledDateRequired = 4,
    FiledDateInFuture = 5,
    GrantDateRequired = 6,
    GrantDateInFuture = 7,
    GrantDateWithoutFilingDate = 8,
    GrantDateBeforeFilingDate = 9
}
