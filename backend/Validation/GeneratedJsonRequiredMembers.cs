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
        TrackResubmitRequestPresence(typeInfo);

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

    private static void TrackResubmitRequestPresence(JsonTypeInfo typeInfo)
    {
        // Nullable PATCH fields need to distinguish an omitted property from an
        // explicitly supplied null so the controller can preserve or clear values.
        if (typeInfo.Type != typeof(ResubmitBudgetApplicationRequest))
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            var propertyName = property.Name;
            if (!propertyName.Equals("activityId", StringComparison.OrdinalIgnoreCase) &&
                !propertyName.Equals("detail", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var setter = property.Set;
            if (setter is null)
            {
                continue;
            }

            property.Set = (obj, value) =>
            {
                setter(obj, value);
                var request = (ResubmitBudgetApplicationRequest)obj;
                if (propertyName.Equals("activityId", StringComparison.OrdinalIgnoreCase))
                {
                    request.ActivityIdWasProvided = true;
                }
                else
                {
                    request.DetailWasProvided = true;
                }
            };
        }
    }
}
