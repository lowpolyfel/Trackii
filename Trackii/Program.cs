using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Trackii.Services;
using Trackii.Services.Admin;

var builder = WebApplication.CreateBuilder(args);

// =====================
// Authentication (Cookies)
// =====================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Denied";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

// =====================
// Authorization (GLOBAL)
// =====================
builder.Services.AddControllersWithViews(options =>
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.Filters.Add(new AuthorizeFilter(policy));
});

// =====================
// Services
// =====================
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AreaService>();
builder.Services.AddScoped<FamilyService>();
builder.Services.AddScoped<SubfamilyService>();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<LocationService>();
builder.Services.AddScoped<RoleService>();
builder.Services.AddScoped<UserService>();

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

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
