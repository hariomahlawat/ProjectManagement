namespace ProjectManagement.Services.Notebook;

// SECTION: Notebook validation failures raised by service-layer commands
public sealed class NotebookValidationException : ArgumentException
{
    public NotebookValidationException(string message) : base(message)
    {
    }
}
