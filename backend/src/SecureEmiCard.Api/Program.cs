using System.IdentityModel.Tokens.Jwt;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SecureEmiCard.Api;
using SecureEmiCard.Api.Infrastructure;
using SecureEmiCard.Application;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Infrastructure;
using SecureEmiCard.Infrastructure.Persistence;
using SecureEmiCard.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// ---- 1. Layers (Clean Architecture) -------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// ---- 2. Controllers, errors, Swagger ----------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Secure Credit EMI Card API", Version = "v1" });
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the token returned by /api/auth/login",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    c.AddSecurityDefinition("Bearer", scheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
});

// ---- 3. JWT authentication (HMAC-SHA512) --------------------------------------------
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // keep short claim names ("sub", "role")
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                string.IsNullOrEmpty(jwt.SigningKey) ? new byte[64] : Convert.FromBase64String(jwt.SigningKey)),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha512 },
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = JwtTokenGenerator.RoleClaimType
        };
    });
builder.Services.AddAuthorization();

// ---- 4. Rate limiting against brute force (login, PIN) -------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Sensitive, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Per authenticated user when available, otherwise per client IP.
            partitionKey: httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                          ?? httpContext.Connection.RemoteIpAddress?.ToString()
                          ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimiting:SensitivePermitPerMinute", 10),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// ---- 5. CORS for the Angular client ------------------------------------------------
const string AngularCors = "angular";
builder.Services.AddCors(options => options.AddPolicy(AngularCors, policy =>
    policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
          .AllowAnyHeader()
          .AllowAnyMethod()));

var app = builder.Build();

// ---- HTTP pipeline (order matters!) ---------------------------------------------------
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    // Basic security headers for an API that returns sensitive financial data.
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Cache-Control"] = "no-store";
    await next();
});

app.UseCors(AngularCors);
app.UseAuthentication();
app.UseRateLimiter(); // after authentication so the limiter can partition by user id
app.UseAuthorization();

app.MapControllers();

await DbInitializer.InitializeAsync(app.Services);

app.Run();

// Makes Program visible to integration tests (WebApplicationFactory<Program>).
public partial class Program { }
