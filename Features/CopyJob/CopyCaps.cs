using System.Globalization;

namespace Laneway.Api;

public static class CopyCaps
{
    public static List<ApiError>? Exceeded(int cards, int files)
    {
        if (cards > FieldLimits.CardsPerCopy)
        {
            return [Capped("cards", cards, FieldLimits.CardsPerCopy)];
        }

        return files > FieldLimits.FilesPerCopy ? [Capped("files", files, FieldLimits.FilesPerCopy)] : null;
    }

    private static ApiError Capped(string what, int counted, int cap) =>
        new(null, ErrorCodes.LimitReached, string.Create(CultureInfo.InvariantCulture,
            $"A copy takes at most {cap:N0} {what}. This one has {counted:N0}."));
}
