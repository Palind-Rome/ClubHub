using ClubHub.Api.Data;
using ClubHub.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddScoped<AuthService>();

builder.Services.AddDbContext<ClubHubDbContext>(options =>
    options.UseOracle(builder.Configuration.GetConnectionString("Default"))
);

var app = builder.Build();

app.UseCors(policy =>
    policy.AllowAnyOrigin()
          .AllowAnyMethod()
          .AllowAnyHeader());

app.MapControllers();
app.Run();
