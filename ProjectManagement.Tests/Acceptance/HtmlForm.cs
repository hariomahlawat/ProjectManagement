using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace ProjectManagement.Tests.Acceptance;

/// <summary>
/// The successful controls of one rendered form, collected as a browser without
/// JavaScript would submit them: enabled inputs with a name (checkbox/radio only when
/// checked, submit buttons only when clicked) and textareas. Enough for PRISM's server
/// rendered forms; not a general HTML parser.
/// </summary>
internal sealed class HtmlForm
{
    private static readonly Regex FormPattern = new(@"<form\b(?<attrs>[^>]*)>(?<body>.*?)</form>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex InputPattern = new(@"<input\b(?<attrs>[^>]*?)/?>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex TextAreaPattern = new(@"<textarea\b(?<attrs>[^>]*)>(?<value>.*?)</textarea>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex FieldSetPattern = new(@"<fieldset\b(?<attrs>[^>]*)>(?<body>.*?)</fieldset>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private HtmlForm(string action, string body, List<KeyValuePair<string, string>> fields)
    {
        Action = action;
        Body = body;
        Fields = fields;
    }

    public string Action { get; }

    public string Body { get; }

    public List<KeyValuePair<string, string>> Fields { get; }

    /// <summary>The first form whose opening tag contains <paramref name="marker"/>.</summary>
    public static HtmlForm Find(string html, string marker)
    {
        var match = FormPattern.Matches(html).FirstOrDefault(form => form.Groups["attrs"].Value.Contains(marker, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No form with '{marker}' was rendered.");
        var body = match.Groups["body"].Value;

        // Controls inside a disabled fieldset are not submitted.
        var submittable = FieldSetPattern.Replace(body, fieldset => HasAttribute(fieldset.Groups["attrs"].Value, "disabled") ? string.Empty : fieldset.Value);

        var fields = new List<KeyValuePair<string, string>>();
        foreach (Match input in InputPattern.Matches(submittable))
        {
            var attrs = input.Groups["attrs"].Value;
            var name = Attribute(attrs, "name");
            var type = (Attribute(attrs, "type") ?? "text").ToLowerInvariant();
            if (name is null || HasAttribute(attrs, "disabled") || type is "submit" or "button" or "file"
                || (type is "checkbox" or "radio" && !HasAttribute(attrs, "checked")))
            {
                continue;
            }

            fields.Add(new(name, WebUtility.HtmlDecode(Attribute(attrs, "value") ?? string.Empty)));
        }

        foreach (Match area in TextAreaPattern.Matches(submittable))
        {
            var attrs = area.Groups["attrs"].Value;
            if (Attribute(attrs, "name") is { } name && !HasAttribute(attrs, "disabled"))
            {
                fields.Add(new(name, WebUtility.HtmlDecode(area.Groups["value"].Value.TrimStart('\r', '\n'))));
            }
        }

        return new HtmlForm(WebUtility.HtmlDecode(Attribute(match.Groups["attrs"].Value, "action") ?? string.Empty), body, fields);
    }

    public string? Get(string name) => Fields.FirstOrDefault(field => field.Key == name).Value;

    public HtmlForm Set(string name, string value)
    {
        var index = Fields.FindIndex(field => field.Key == name);
        if (index < 0)
        {
            throw new InvalidOperationException($"The form has no submittable field '{name}'.");
        }

        Fields[index] = new(name, value);
        return this;
    }

    public HtmlForm Add(string name, string value)
    {
        Fields.Add(new(name, value));
        return this;
    }

    /// <summary>The editor row index of a stage, from its hidden StageCode field.</summary>
    public int RowOf(string stageCode)
    {
        var field = Fields.Single(item => item.Key.EndsWith("].StageCode", StringComparison.Ordinal) && item.Value == stageCode);
        return int.Parse(Regex.Match(field.Key, @"\[(\d+)\]").Groups[1].Value);
    }

    public FormUrlEncodedContent Content() => new(Fields);

    private static string? Attribute(string attrs, string name)
    {
        var match = Regex.Match(attrs, $@"(?:^|\s){Regex.Escape(name)}\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)')", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["v"].Value : null;
    }

    private static bool HasAttribute(string attrs, string name) =>
        Regex.IsMatch(attrs, $@"(?:^|\s){Regex.Escape(name)}(?:\s|=|$|/)", RegexOptions.IgnoreCase);
}
