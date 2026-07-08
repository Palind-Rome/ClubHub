using ClubHub.Api;
using ClubHub.Api.Data;
using ClubHub.Api.Services;
using Microsoft.EntityFrameworkCore;
using Org.OpenAPITools.Converters;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumMemberConverter());
    });
builder.Services.AddScoped<AuthService>();

builder.Services.AddDbContext<ClubHubDbContext>(options =>
    options.UseOracle(builder.Configuration.GetConnectionString("Default"))
);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
    await authService.InitializeBaseRolesAsync();
}

app.UseCors(policy =>
    policy.AllowAnyOrigin()
          .AllowAnyMethod()
          .AllowAnyHeader());

app.MapControllers();
app.Run();
