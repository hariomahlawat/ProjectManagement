namespace ProjectManagement.Models;

/// <summary>
/// Classifies an identity for population-based reporting. Only human accounts are included
/// in command adoption analytics unless an administrator explicitly expands the scope.
/// </summary>
public enum UserAccountKind
{
    Human = 1,
    Service = 2,
    Test = 3
}
