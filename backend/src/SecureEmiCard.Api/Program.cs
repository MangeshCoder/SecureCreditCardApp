using System.IdentityModel.Tokens.Jwt;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SecureEmiCard.Api;
using SecureEmiCard.Api.Infrastructure;
using SecureEmiCard.Application;
using SecureEmiCard.Application.Abstractions.Messaging;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Infrastructure;
using SecureEmiCard.Infrastructure.Notifications;
using SecureEmiCard.Infrastructure.Persistence;
using SecureEmiCard.Infrastructure.Security;
using SecureEmiCard.Api.InterBank;

var builder = WebApplication.CreateBuilder(args);

// ---- 1. Layers (Clean Architecture) -------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IOtpProofAccessor, HttpOtpProofAccessor>(); // Module 7: X-Otp-* headers

// Module 7: the simulated SMS / e-mail sender keeps one-time codes and alerts in server memory. A bank must
// never run like that for real customers: outside Development and the test environment the app refuses to
// start while the simulator is the registered IMessageSender (register a real provider instead).
var messageSender = builder.Services.Last(d => d.ServiceType == typeof(IMessageSender)).ImplementationType;
if (messageSender == typeof(SimulatedMessageSender)
    && !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
    throw new InvalidOperationException(
        "The simulated SMS / e-mail sender is for Development only. Register a real IMessageSender before running in " +
        $"'{builder.Environment.EnvironmentName}'.");

// ---- 1b. Inter-bank gateway security (Module 5) ---------------------------------------
builder.Services.AddOptions<InterBankOptions>()
    .Bind(builder.Configuration.GetSection(InterBankOptions.SectionName))
    .Validate(o => o.AllowedClockSkewSeconds is > 0 and <= 900, "InterBank:AllowedClockSkewSeconds must be 1-900.")
    .Validate(o => o.NonceTtlSeconds >= 2 * o.AllowedClockSkewSeconds,
              "InterBank:NonceTtlSeconds must be at least twice AllowedClockSkewSeconds (otherwise replays slip through).")
    .Validate(o => o.MaxBodyBytes is >= 1024 and <= 1_048_576, "InterBank:MaxBodyBytes must be 1 KB - 1 MB.")
    .Validate(o => o.Partners.Select(p => p.PartnerId).Distinct().Count() == o.Partners.Count &&
                   o.Partners.All(p => !string.IsNullOrWhiteSpace(p.PartnerId) && p.PartnerId.Length <= 50),
              "InterBank:Partners must have unique PartnerIds (max 50 characters).")
    .Validate(o => o.Partners.All(p => KeyLength(p.EncryptionKey) == 32 && KeyLength(p.SigningKey) >= 32 &&
                                       p.EncryptionKey != p.SigningKey),
              "Each partner needs a Base64 32-byte EncryptionKey and a different Base64 SigningKey of at least 32 bytes.")
    .ValidateOnStart();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<PartnerRegistry>();
builder.Services.AddSingleton<INonceStore, MemoryNonceStore>();

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
    // Production: force HTTPS. In Development we don't redirect, because the Angular dev server
    // calls http://localhost:5080 and browsers refuse to follow a redirect on a CORS preflight
    // (OPTIONS) request - the login would fail with "Cannot reach the server".
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    // Basic security headers for an API that returns sensitive financial data.
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Cache-Control"] = "no-store";
    await next();
});

// Partner banks (server-to-server) are authenticated by signature, not JWT; this middleware also
// decrypts their requests and encrypts + signs our responses. It runs only for /api/gateway.
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api/gateway"),
            gateway => gateway.UseMiddleware<InterBankSecurityMiddleware>());

app.UseCors(AngularCors);
app.UseAuthentication();
app.UseRateLimiter(); // after authentication so the limiter can partition by user id
app.UseAuthorization();

app.MapControllers();

await DbInitializer.InitializeAsync(app.Services);

app.Run();
static int KeyLength(string base64)
{
    var buffer = new byte[base64.Length];
    return Convert.TryFromBase64String(base64, buffer, out var written) ? written : -1;
}

// Makes Program visible to integration tests (WebApplicationFactory<Program>).
public partial class Program { }
