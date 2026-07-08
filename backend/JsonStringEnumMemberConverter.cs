using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClubHub.Api;

public sealed class JsonStringEnumMemberConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsEnum;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(EnumMemberConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class EnumMemberConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        private static readonly Dictionary<string, TEnum> FromWire = BuildFromWire();
        private static readonly Dictionary<TEnum, string> ToWire = BuildToWire();

        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), number);
            }

            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Expected string value for enum {typeof(TEnum).Name}.");
            }

            var value = reader.GetString();
            if (value is not null && FromWire.TryGetValue(value, out var enumValue))
            {
                return enumValue;
            }

            throw new JsonException($"Unknown value '{value}' for enum {typeof(TEnum).Name}.");
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(ToWire.TryGetValue(value, out var wireValue) ? wireValue : value.ToString());
        }

        private static Dictionary<string, TEnum> BuildFromWire()
        {
            return Enum.GetValues<TEnum>()
                .Select(value => new { Value = value, Wire = WireName(value) })
                .ToDictionary(item => item.Wire, item => item.Value, StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<TEnum, string> BuildToWire()
        {
            return Enum.GetValues<TEnum>().ToDictionary(value => value, WireName);
        }

        private static string WireName(TEnum value)
        {
            var member = typeof(TEnum).GetMember(value.ToString()).FirstOrDefault();
            return member?.GetCustomAttribute<EnumMemberAttribute>()?.Value ?? value.ToString();
        }
    }
}
