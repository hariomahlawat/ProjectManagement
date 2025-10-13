using System;
using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

internal static class VisitPhotoErrorTranslator
{
    private const string DefaultMessage = "Unable to save photo metadata.";

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
            case "42P01": // undefined table
            case "42703": // undefined column
            case "42P07": // duplicate table (migration drift)
            case "3F000": // schema does not exist
                message = "The database for visit photos is missing required tables. Please run the latest database migrations and try again.";
                return true;
            case "23503": // foreign key violation
                if (TryGetStringProperty(providerException, "ConstraintName", out var constraint) &&
                    string.Equals(constraint, "FK_VisitPhotos_Visits_VisitId", StringComparison.OrdinalIgnoreCase))
                {
                    message = "The visit no longer exists. Please refresh the page and try again.";
                    return true;
                }
                break;
            case "23502": // not null violation
                message = "Required visit photo information was missing. Please contact an administrator.";
                return true;
            case "22001": // string data right truncation
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
            case 208: // invalid object name
            case 4902: // cannot find the object
                message = "The database for visit photos is missing required tables. Please run the latest database migrations and try again.";
                return true;
            case 547: // foreign key violation
                if (TryGetStringProperty(providerException, "Constraint", out var constraint) &&
                    string.Equals(constraint, "FK_VisitPhotos_Visits_VisitId", StringComparison.OrdinalIgnoreCase))
                {
                    message = "The visit no longer exists. Please refresh the page and try again.";
                    return true;
                }

                message = "Visit photo references are invalid. Please refresh the page and try again.";
                return true;
            case 515: // cannot insert null
                message = "Required visit photo information was missing. Please contact an administrator.";
                return true;
            case 2627: // unique constraint violation
            case 2601: // duplicate key
                message = "A conflicting photo entry already exists. Please refresh the page and try again.";
                return true;
            case 8152: // string or binary data would be truncated
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
