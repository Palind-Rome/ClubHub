using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;

namespace Org.OpenAPITools.Converters;

/// <summary>
/// Supports OpenAPI-generated enum models that serialize values through EnumMemberAttribute.
/// </summary>
public sealed class CustomEnumConverter<TEnum> : TypeConverter where TEnum : struct, Enum
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string text && TryParseEnumMember(text, out var parsed))
        {
            return parsed;
        }

        return base.ConvertFrom(context, culture, value);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(string) || base.CanConvertTo(context, destinationType);
    }

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        if (destinationType == typeof(string) && value is TEnum enumValue)
        {
            return GetEnumMemberValue(enumValue) ?? enumValue.ToString();
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    private static bool TryParseEnumMember(string text, out TEnum value)
    {
        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var enumMember = field.GetCustomAttribute<EnumMemberAttribute>();
            if (string.Equals(enumMember?.Value, text, StringComparison.OrdinalIgnoreCase)
                || string.Equals(field.Name, text, StringComparison.OrdinalIgnoreCase))
            {
                value = (TEnum)field.GetValue(null)!;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetEnumMemberValue(TEnum value)
    {
        var field = typeof(TEnum).GetField(value.ToString());
        return field?.GetCustomAttribute<EnumMemberAttribute>()?.Value;
    }
}
