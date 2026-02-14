namespace ProjectManagement.Services.Text;

public interface IMarkdownRenderer
{
    string ToSafeHtml(string? markdown);
}

