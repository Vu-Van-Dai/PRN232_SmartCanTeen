using API.Hubs;
using Application.JWTToken;
using Application.Orders;
using Application.Payments;
using Core.Common;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using API.Seed;
using API.Services;

var builder = WebApplication.CreateBuilder(args);

// CORS (needed for FE dev server calling BE)
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[]
            {
                "http://localhost:8080",
                "http://localhost:5173",
                "https://localhost:8080",
                "https://localhost:5173"
            };

        policy
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

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
builder.Services.AddScoped<JwtTokenService>();
builder.Services.Configure<PayosOptions>(builder.Configuration.GetSection("PayOS"));
builder.Services.AddHttpClient<PayosService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<PayosPaymentProcessor>();
builder.Services.AddScoped<IInventoryNotifier, InventoryNotifier>();
builder.Services.AddScoped<OrderSchedulerService>();
builder.Services.AddHostedService<OrderSchedulerHostedService>();

builder.Services.AddMemoryCache();

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

// CORS must run before auth/authorization to allow preflight (OPTIONS) requests.
app.UseCors("DevCors");

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<OrderHub>("/hubs/order");
app.MapHub<ManagementHub>("/hubs/management");
app.MapHub<KitchenHub>("/hubs/kitchen");

app.MapControllers();

await DbSeeder.SeedAsync(app.Services, app.Configuration);

app.Run();
