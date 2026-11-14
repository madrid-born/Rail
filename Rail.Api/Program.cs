using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Rail.Api.Controllers;
using Rail.Api.Models;
using Rail.Api.Services;

var builder = WebApplication.CreateBuilder(args);
// A stable discriminator keeps tokens valid when the application deployment path changes.
// Production hosts must retain the platform key ring (or mount a persistent shared one).
builder.Services.AddDataProtection().SetApplicationName("Rail.Api");

builder.Services.AddOpenApi(options =>
{
    options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            Description = "Paste the Rail accessToken only. Swagger adds the Bearer prefix automatically."
        };
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, _) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var requiresAuthentication = metadata.OfType<IAuthorizeData>().Any() &&
                                     !metadata.OfType<IAllowAnonymous>().Any();
        // Exchange is anonymous for login, but accepts the current token when linking Google.
        var optionalAuthentication = context.Description.ActionDescriptor is ControllerActionDescriptor action &&
                                     action.MethodInfo.Name == nameof(AuthenticationController.ExchangeGoogleLogin);
        if (requiresAuthentication || optionalAuthentication)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
            });
            if (optionalAuthentication) operation.Security.Add(new OpenApiSecurityRequirement());
        }
        return Task.CompletedTask;
    });
});
builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddCors(options => options.AddPolicy("RailWebPages", policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["https://appdir", "https://0.0.0.1", "https://appassets.androidplatform.net"];
    policy.WithOrigins(origins).WithMethods("GET", "POST", "DELETE").AllowAnyHeader();
}));
builder.Services.AddAuthorization();
builder.Services.AddScoped<JourneyPlanner>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<HolidayCalendar>(client =>
{
    client.BaseAddress = new Uri("https://holidayapi.ir/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<TimeTableCacheBuilder>();
builder.Services.AddHttpClient<StreetRouting>(client =>
{
    client.BaseAddress = new Uri("https://api.neshan.org/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddIdentityApiEndpoints<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();
builder.Services.AddSingleton<GoogleLoginSessions>();
// Password login remains available when Google credentials have not been configured.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Google:ClientId"]) &&
    !string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Google:ClientSecret"]))
{
    builder.Services.AddAuthentication().AddGoogle(options =>
        GoogleAuthentication.Configure(options, builder.Configuration));
}
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Email"));
if (builder.Environment.IsDevelopment())
{
    // An empty environment/configuration value must not hide the local SMTP secret.
    var localSecrets = new ConfigurationBuilder()
        .AddUserSecrets<ApplicationUser>(optional: true)
        .Build();
    builder.Services.PostConfigure<SmtpOptions>(options =>
    {
        if (string.IsNullOrWhiteSpace(options.Password))
            options.Password = localSecrets["Email:Password"];
    });
}
builder.Services.AddTransient<IEmailSender<ApplicationUser>, IdentityEmailSender>();
builder.Services.Configure<BearerTokenOptions>(
    IdentityConstants.BearerScheme, options =>
    {
        options.BearerTokenExpiration = TimeSpan.FromMinutes(15);
        // Each successful refresh renews this window; normal app updates preserve it.
        options.RefreshTokenExpiration = TimeSpan.FromDays(180);
    });

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
            sqlOptions.CommandTimeout(60);
        }
    );

    if (!builder.Environment.IsDevelopment()) return;
    options.EnableSensitiveDataLogging();
    options.EnableDetailedErrors();
});


var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Rail API v1");
    });
}

app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedProto });
app.UseHttpsRedirection();
app.UseCors("RailWebPages");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
