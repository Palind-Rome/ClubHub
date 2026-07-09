using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClubHub.Api.Serialization;

public sealed class EnumMemberJsonStringEnumConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(EnumMemberConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class EnumMemberConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        private readonly Dictionary<string, TEnum> _fromJson = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<TEnum, string> _toJson = [];

        public EnumMemberConverter()
        {
            var enumType = typeof(TEnum);
            foreach (var value in Enum.GetValues<TEnum>())
            {
                var name = Enum.GetName(enumType, value)!;
                var enumMember = enumType.GetField(name)?.GetCustomAttribute<EnumMemberAttribute>();
                var jsonName = enumMember?.Value ?? name;

                _fromJson[jsonName] = value;
                _fromJson[name] = value;
                _toJson[value] = jsonName;
            }
        }

        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var jsonValue = reader.GetString();
                if (jsonValue is not null && _fromJson.TryGetValue(jsonValue, out var enumValue))
                {
                    return enumValue;
                }
            }

            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numericValue))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), numericValue);
            }

            throw new JsonException($"Cannot convert JSON value to {typeof(TEnum).Name}.");
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(_toJson.TryGetValue(value, out var jsonName) ? jsonName : value.ToString());
        }
    }
}
