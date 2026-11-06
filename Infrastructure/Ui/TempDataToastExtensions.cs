using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ProjectManagement.Infrastructure.Ui;

public static class TempDataToastExtensions
{
    public const string ToastJsonKey = "ToastJson";

    public static void ToastSuccess(this ITempDataDictionary tempData, string text) => tempData["ToastSuccess"] = text;

    public static void ToastInfo(this ITempDataDictionary tempData, string text) => tempData["ToastInfo"] = text;

    public static void ToastWarning(this ITempDataDictionary tempData, string text) => tempData["ToastWarning"] = text;

    public static void ToastError(this ITempDataDictionary tempData, string text) => tempData["ToastError"] = text;

    public static void ToastMany(this ITempDataDictionary tempData, params (string level, string text)[] items)
    {
        if (items is null || items.Length == 0)
        {
            return;
        }

        var payload = items
            .Where(item => !string.IsNullOrWhiteSpace(item.text))
            .Select(item => new ToastPayload(item.level, item.text, null))
            .ToList();

        if (payload.Count == 0)
        {
            return;
        }

        tempData[ToastJsonKey] = JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// A toast with one action the server can really carry out, usually Undo: the button posts
    /// the given fields (with the antiforgery token) to a local page handler, which re-checks
    /// permission and the record version as it always does. docs/ux/toasts.md.
    /// </summary>
    public static void ToastWithAction(this ITempDataDictionary tempData, string level, string text, ToastAction action)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var payload = ReadPayload(tempData);
        payload.Add(new ToastPayload(level, text, action));
        tempData[ToastJsonKey] = JsonSerializer.Serialize(payload);
    }

    private static List<ToastPayload> ReadPayload(ITempDataDictionary tempData)
    {
        if (tempData.Peek(ToastJsonKey) is not string json || string.IsNullOrWhiteSpace(json))
        {
            return new List<ToastPayload>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<ToastPayload>>(json) ?? new List<ToastPayload>();
        }
        catch (JsonException)
        {
            return new List<ToastPayload>();
        }
    }

    private sealed record ToastPayload(string Level, string Text, ToastAction? Action);
}

/// <summary>One toast button that posts to a local page handler (see <see cref="TempDataToastExtensions.ToastWithAction"/>).</summary>
public sealed record ToastAction(string Label, string PostUrl, IReadOnlyDictionary<string, string> Fields);
