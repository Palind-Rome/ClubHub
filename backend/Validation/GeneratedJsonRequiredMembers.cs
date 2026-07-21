using System.Text.Json.Serialization.Metadata;
using Org.OpenAPITools.Models;

namespace ClubHub.Api.Validation;

/// <summary>
/// Keeps runtime JSON binding aligned with OpenAPI required fields that generated
/// non-nullable value-type models cannot validate through DataAnnotations alone.
/// </summary>
public static class GeneratedJsonRequiredMembers
{
    private static readonly IReadOnlyDictionary<Type, IReadOnlySet<string>> RequiredJsonProperties =
        new Dictionary<Type, IReadOnlySet<string>>
        {
            [typeof(ReviewProjectTaskDeliverableRequest)] = new HashSet<string>(StringComparer.Ordinal)
            {
                "approved",
                nameof(ReviewProjectTaskDeliverableRequest.Approved)
            }
        };

    public static void Apply(JsonTypeInfo typeInfo)
    {
        if (!RequiredJsonProperties.TryGetValue(typeInfo.Type, out var requiredProperties))
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            if (requiredProperties.Contains(property.Name))
            {
                property.IsRequired = true;
            }
        }
    }
}
