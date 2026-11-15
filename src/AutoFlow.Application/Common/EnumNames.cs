namespace AutoFlow.Application.Common;

/// <summary>
/// Reads an enum value from the name the API exposes it by. Unlike
/// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> it accepts no
/// numbers (<c>"1"</c>) and no combinations (<c>"Cash, Card"</c>).
/// </summary>
internal static class EnumNames
{
    public static bool TryParse<TEnum>(string? name, out TEnum value)
        where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(candidate.ToString(), name?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }
}
