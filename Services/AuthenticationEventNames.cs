namespace ProjectManagement.Services;

public static class AuthenticationEventNames
{
    public const string LoginSucceeded = "LoginSucceeded";
    public const string AuditLoginSuccess = "LoginSuccess";
    public const string AuditLoginFailed = "LoginFailed";
    public const string AuditLoginLockedOut = "LoginLockedOut";
}
