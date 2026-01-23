using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using InkVault.Data;
using InkVault.Models;
using InkVault.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure port for Render deployment
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://*:{port}");

// Database configuration - SQL Server for development, PostgreSQL for production
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("LocalConnection")));
}
else
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
}


// Identity with persistent login support
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Configure persistent authentication cookies
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = ".AspNetCore.Identity.Application";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    
    // In development, allow cookies over HTTP for testing
    // In production, require HTTPS (secure)
    if (builder.Environment.IsProduction())
    {
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    }
    else
    {
        options.Cookie.SecurePolicy = CookieSecurePolicy.None;  // Allow HTTP in development
    }
    
    options.ExpireTimeSpan = TimeSpan.FromDays(14); // Remember me duration: 14 days
    options.SlidingExpiration = true; // Extend expiration on each request
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    
    // In development, reduce cookie timeout for testing
    if (!builder.Environment.IsProduction())
    {
        options.ExpireTimeSpan = TimeSpan.FromDays(7); // 7 days in development
    }
});

// Services
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IOTPService, OTPService>();
builder.Services.AddScoped<IBirthdayService, BirthdayService>();

// Hosted service for daily birthday email check
// Temporarily disabled for debugging - enable after fixing login
// builder.Services.AddHostedService<BirthdayBackgroundService>();

// Session
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// MVC
builder.Services.AddControllersWithViews();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Only use HTTPS redirection in development
// Render handles SSL termination at the load balancer level
if (!app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

// Handle forwarded headers from Render's load balancer
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor 
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
});

app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
name: "default",
pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
