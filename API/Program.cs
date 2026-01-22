using API.Hubs;
using Application.JWTToken;
using Application.Orders.Services;
using Application.Payments;
using Core.Common;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"));
});

//builder.Services.AddScoped<ICurrentCampusService>(_ =>
//{
//    return new CurrentCampusService
//    {
//        CampusId = Guid.Parse("PUT-YOUR-TEST-CAMPUS-ID-HERE")
//    };
//});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentCampusService, CurrentCampusService>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<VnpayService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<IInventoryNotifier, InventoryNotifier>();

builder.Services.AddSignalR();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)
            )
        };
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<OrderHub>("/hubs/order");
app.MapHub<ManagementHub>("/hubs/management");

app.MapControllers();

app.Run();
