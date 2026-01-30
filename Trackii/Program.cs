using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.IdentityModel.Tokens;
using Trackii.Services;
using Trackii.Services.Admin;
using Trackii.Services.Api;

var builder = WebApplication.CreateBuilder(args);

// =====================
// Authentication (Cookies + JWT)
// =====================
builder.Services.AddAuthentication(options =>
{
    // Para la WEB: cookies siguen como default
    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Denied";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    })
    .AddJwtBearer("ApiBearer", options =>
    {
        var jwt = builder.Configuration.GetSection("Jwt");
        var issuer = jwt["Issuer"] ?? "Trackii";
        var audience = jwt["Audience"] ?? "Trackii.Tablets";
        var secret = jwt["Secret"] ?? throw new Exception("Jwt:Secret no configurado en appsettings.json");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,

            ValidateAudience = true,
            ValidAudience = audience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });

// =====================
// Authorization (GLOBAL para MVC)
// =====================
// Mantiene tu web protegida por cookie.
// Los controllers API usarán [Authorize(AuthenticationSchemes="ApiBearer")] explícito.
builder.Services.AddControllersWithViews(options =>
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.Filters.Add(new AuthorizeFilter(policy));
});

// =====================
// Services (existentes)
// =====================
builder.Services.AddScoped<AuthService>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<AreaService>();
builder.Services.AddScoped<FamilyService>();
builder.Services.AddScoped<SubfamilyService>();
builder.Services.AddScoped<LocationService>();
builder.Services.AddScoped<RoleService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<Trackii.Services.Admin.RouteService>();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<DeviceActivationApiService>();

// =====================
// Services (API)
// =====================
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<DeviceApiService>();
builder.Services.AddScoped<ScanApiService>();

var app = builder.Build();

// =====================
// Pipeline
// =====================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Attribute routing (API)
app.MapControllers();

// Conventional routing (MVC)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
