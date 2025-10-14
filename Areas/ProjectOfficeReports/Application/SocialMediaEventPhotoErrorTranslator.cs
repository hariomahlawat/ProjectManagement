using System;
using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

internal static class SocialMediaEventPhotoErrorTranslator
{
    private const string DefaultMessage = "Unable to save social media photo metadata.";

    public static string GetUserFacingMessage(DbUpdateException exception)
    {
        if (exception == null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        var providerException = exception.InnerException ?? exception;

        if (TryTranslatePostgres(providerException, out var message))
        {
            return message;
        }

        if (TryTranslateSqlServer(providerException, out message))
        {
            return message;
        }

        if (TryTranslateGeneric(providerException, out message))
        {
            return message;
        }

        return DefaultMessage;
    }

    private static bool TryTranslatePostgres(Exception providerException, out string message)
    {
        message = DefaultMessage;
        if (!TryGetStringProperty(providerException, "SqlState", out var rawSqlState) || string.IsNullOrWhiteSpace(rawSqlState))
        {
            return false;
        }

        var sqlState = rawSqlState.ToUpperInvariant();

        switch (sqlState)
        {
            case "42P01":
            case "42703":
            case "42P07":
            case "3F000":
                message = "The database for social media photos is missing required tables. Please run the latest database migrations and try again.";
                return true;
            case "23503":
                if (TryGetStringProperty(providerException, "ConstraintName", out var constraint) &&
                    string.Equals(constraint, "FK_SocialMediaEventPhotos_SocialMediaEvents_SocialMediaEventId", StringComparison.OrdinalIgnoreCase))
                {
                    message = "The social media event no longer exists. Please refresh the page and try again.";
                    return true;
                }

                break;
            case "23502":
                message = "Required social media photo information was missing. Please contact an administrator.";
                return true;
            case "23505":
                if (TryGetStringProperty(providerException, "ConstraintName", out var uniqueConstraint) &&
                    string.Equals(uniqueConstraint, "UX_SocialMediaEventPhotos_IsCover", StringComparison.OrdinalIgnoreCase))
                {
                    message = "Another photo is already marked as cover. Please refresh and try again.";
                    return true;
                }

                message = "A conflicting social media photo entry already exists. Please refresh the page and try again.";
                return true;
            case "22001":
                message = "Photo details were too long to save. Please shorten the caption and try again.";
                return true;
        }

        return false;
    }

    private static bool TryTranslateSqlServer(Exception providerException, out string message)
    {
        message = DefaultMessage;
        if (!TryGetProperty(providerException, "Number", out int sqlNumber))
        {
            return false;
        }

        switch (sqlNumber)
        {
            case 208:
            case 4902:
                message = "The database for social media photos is missing required tables. Please run the latest database migrations and try again.";
                return true;
            case 547:
                if (TryGetStringProperty(providerException, "Constraint", out var constraint) &&
                    string.Equals(constraint, "FK_SocialMediaEventPhotos_SocialMediaEvents_SocialMediaEventId", StringComparison.OrdinalIgnoreCase))
                {
                    message = "The social media event no longer exists. Please refresh the page and try again.";
                    return true;
                }

                message = "Social media photo references are invalid. Please refresh the page and try again.";
                return true;
            case 515:
                message = "Required social media photo information was missing. Please contact an administrator.";
                return true;
            case 2627:
            case 2601:
                if (TryGetStringProperty(providerException, "Constraint", out var uniqueConstraint) &&
                    string.Equals(uniqueConstraint, "UX_SocialMediaEventPhotos_IsCover", StringComparison.OrdinalIgnoreCase))
                {
                    message = "Another photo is already marked as cover. Please refresh and try again.";
                    return true;
                }

                message = "A conflicting social media photo entry already exists. Please refresh the page and try again.";
                return true;
            case 8152:
                message = "Photo details were too long to save. Please shorten the caption and try again.";
                return true;
        }

        return false;
    }

    private static bool TryTranslateGeneric(Exception providerException, out string message)
    {
        message = DefaultMessage;

        if (TryGetStringProperty(providerException, "Message", out var providerMessage) &&
            providerMessage.Contains("string or binary data", StringComparison.OrdinalIgnoreCase))
        {
            message = "Photo details were too long to save. Please shorten the caption and try again.";
            return true;
        }

        return false;
    }

    private static bool TryGetStringProperty(object instance, string propertyName, out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(instance, propertyName, out object? raw) || raw == null)
        {
            return false;
        }

        if (raw is string str)
        {
            value = str;
            return true;
        }

        if (raw is IFormattable formattable)
        {
            value = formattable.ToString(null, CultureInfo.InvariantCulture);
            return true;
        }

        value = raw.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetProperty<T>(object instance, string propertyName, out T value)
    {
        var raw = GetPropertyValue(instance, propertyName);
        if (raw is T typed)
        {
            value = typed;
            return true;
        }

        if (raw != null)
        {
            try
            {
                value = (T)Convert.ChangeType(raw, typeof(T), CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                // ignored
            }
        }

        value = default!;
        return false;
    }

    private static object? GetPropertyValue(object instance, string propertyName)
    {
        if (instance == null || string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property?.GetValue(instance);
    }
}
