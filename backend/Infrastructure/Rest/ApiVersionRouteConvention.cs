using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace ClubHub.Api.Infrastructure.Rest;

public sealed class ApiVersionRouteConvention : IApplicationModelConvention
{
    private const string ApiRoute = "api";
    private const string ApiRoutePrefix = "api/";
    private const string VersionedApiRoute = "api/v1";

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            AddVersionedSelectors(controller);
        }
    }

    private static void AddVersionedSelectors(ControllerModel controller)
    {
        var legacySelectors = controller.Selectors
            .Where(selector => IsLegacyApiRoute(selector.AttributeRouteModel?.Template))
            .ToArray();

        foreach (var legacySelector in legacySelectors)
        {
            var versionedSelector = new SelectorModel(legacySelector);
            var versionedRoute = new AttributeRouteModel(legacySelector.AttributeRouteModel!);
            var legacyTemplate = versionedRoute.Template!;
            versionedRoute.Template = legacyTemplate.Equals(ApiRoute, StringComparison.OrdinalIgnoreCase)
                ? VersionedApiRoute
                : $"{VersionedApiRoute}/{legacyTemplate[ApiRoutePrefix.Length..]}";
            versionedSelector.AttributeRouteModel = versionedRoute;
            controller.Selectors.Add(versionedSelector);
        }
    }

    private static bool IsLegacyApiRoute(string? template) =>
        template is not null &&
        (template.Equals(ApiRoute, StringComparison.OrdinalIgnoreCase) ||
         template.StartsWith(ApiRoutePrefix, StringComparison.OrdinalIgnoreCase));
}
