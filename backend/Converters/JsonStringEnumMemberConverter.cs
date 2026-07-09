using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Org.OpenAPITools.Converters;

/// <summary>
/// Converts OpenAPI-generated enums with EnumMemberAttribute to and from their wire-format strings.
/// </summary>
public sealed class JsonStringEnumMemberConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsEnum;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(EnumMemberJsonConverter<>).MakeGenericType(typeToConvert);

        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class EnumMemberJsonConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        private static readonly Dictionary<string, TEnum> FromWireValue = BuildFromWireValue();
        private static readonly Dictionary<TEnum, string> ToWireValue = BuildToWireValue();

        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString();
                if (!string.IsNullOrWhiteSpace(text) && FromWireValue.TryGetValue(text, out var enumValue))
                {
                    return enumValue;
                }
            }
            else if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numberValue))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), numberValue);
            }

            throw new JsonException($"Invalid value for enum {typeof(TEnum).Name}.");
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(ToWireValue.TryGetValue(value, out var wireValue) ? wireValue : value.ToString());
        }

        private static Dictionary<string, TEnum> BuildFromWireValue()
        {
            var values = new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var enumValue = (TEnum)field.GetValue(null)!;
                var wireValue = field.GetCustomAttribute<EnumMemberAttribute>()?.Value;

                values[field.Name] = enumValue;
                if (!string.IsNullOrWhiteSpace(wireValue))
                {
                    values[wireValue] = enumValue;
                }
            }

            return values;
        }

        private static Dictionary<TEnum, string> BuildToWireValue()
        {
            return typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static)
                .ToDictionary(
                    field => (TEnum)field.GetValue(null)!,
                    field => field.GetCustomAttribute<EnumMemberAttribute>()?.Value ?? field.Name);
        }
    }
}
