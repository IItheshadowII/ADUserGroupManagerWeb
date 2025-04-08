using System.Text.Json.Serialization;
using ADUserGroupManagerWeb.Data;
using ADUserGroupManagerWeb.Services;
using ADUserGroupManagerWeb.Authorization;
using ADUserGroupManagerWeb.Middleware;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add controllers with JSON options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("http://localhost", "http://localhost:7180", "http://localhost:3000")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Windows Authentication
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();

// Servicios de autorización personalizada
builder.Services.AddScoped<IADRoleProvider, ADRoleProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

// Configurar autorización con políticas basadas en roles y permisos
builder.Services.AddAuthorization(options =>
{
    // Política predeterminada para requerir autenticación
    options.FallbackPolicy = options.DefaultPolicy;

    // Política para administradores
    options.AddPolicy("RequireAdministrator", policy =>
        policy.RequireRole("Administrator"));

    // Política para gestión de usuarios
    options.AddPolicy("CanManageUsers", policy =>
        policy.RequireAssertion(context =>
            context.User.IsInRole("Administrator") ||
            context.User.IsInRole("UserManager")));

    // Política para crear entornos de clínica
    options.AddPolicy("CanCreateClinic", policy =>
        policy.RequireAssertion(context =>
            context.User.IsInRole("Administrator") ||
            context.User.IsInRole("ClinicCreator")));

    // Política para visualización
    options.AddPolicy("CanViewDashboard", policy =>
        policy.RequireAssertion(context =>
            context.User.IsInRole("Administrator") ||
            context.User.IsInRole("ClinicCreator") ||
            context.User.IsInRole("Viewer")));

    // Configurar políticas de permisos dinámicos
    var permissionTypes = new[]
    {
        "CreateUsers", "ModifyUsers", "DeleteUsers", "UnlockUsers",
        "DisableUsers", "EnableUsers", "ResetPasswords", "CreateClinicEnvironment",
        "ViewDashboard", "ViewAuditLogs", "ModifySettings", "ViewUserDetails"
    };

    foreach (var permission in permissionTypes)
    {
        options.AddPolicy($"Permission:{permission}", policy =>
            policy.Requirements.Add(new PermissionRequirement(permission)));
    }
});

// Services
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IADService, ADService>();
builder.Services.AddScoped<IReportingService, ReportingService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddHttpContextAccessor();

// Database context
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "ADUserGroupManager Web API", Version = "v1" });
    c.AddSecurityDefinition("negotiate", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "negotiate",
        Description = "Windows Authentication"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "negotiate" }
            },
            new string[] { }
        }
    });
});

var app = builder.Build();

// Middleware
app.UseSwagger();
app.UseSwaggerUI();

app.UseExceptionHandler("/Error");
app.UseHsts();

app.UseStaticFiles();
app.UseRouting();

app.UseCors("AllowReactApp");

app.UseAuthentication();
app.UseAuthorization();

// Middleware para enriquecer identidades Windows con grupos de AD
app.UseWindowsAuthClaims();

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();