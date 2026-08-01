using System;

namespace EventEase.Core.Enums
{
    public static class EnumExtensions
    {
        public static T ToEnum<T>(this string? value, T defaultValue = default) where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;

            if (Enum.TryParse<T>(value, ignoreCase: true, out var result))
                return result;

            return defaultValue;
        }

        public static string ToDbString<T>(this T enumValue) where T : struct, Enum
        {
            return enumValue.ToString();
        }

        public static bool EqualsEnum<T>(this string? value, T enumValue) where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return string.Equals(value, enumValue.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
