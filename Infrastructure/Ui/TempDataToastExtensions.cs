using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ProjectManagement.Infrastructure.Ui;

public static class TempDataToastExtensions
{
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
            .Select(item => new ToastPayload(item.level, item.text))
            .ToList();

        if (payload.Count == 0)
        {
            return;
        }

        tempData["ToastJson"] = JsonSerializer.Serialize(payload);
    }

    private sealed record ToastPayload(string Level, string Text);
}
