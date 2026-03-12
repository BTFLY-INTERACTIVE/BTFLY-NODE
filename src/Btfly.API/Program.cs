using System.Text;
using Btfly.API.Data;
using Btfly.API.Middleware;
using Btfly.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BtflyDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.ConfigureWarnings(w =>
        w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});

var rsa = RSA.Create(2048);
var privateKeyB64 = builder.Configuration["Btfly:NodePrivateKeyB64"];
var publicKeyB64  = builder.Configuration["Btfly:NodePublicKeyB64"];

if (string.IsNullOrWhiteSpace(privateKeyB64) || string.IsNullOrWhiteSpace(publicKeyB64))
{
    // On Railway, set BTFLY__NODEPRIVATEKEYB64 and BTFLY__NODEPUBLICKEYB64 env vars.
    // Generate them locally first with: openssl genrsa | base64 -w0
    // Running with temporary keys this session only.
    Console.WriteLine("BTFLY: No RSA keys configured — generated temporary keys. Set env vars to persist.");
}
else
{
    var privDecoded = Encoding.UTF8.GetString(Convert.FromBase64String(privateKeyB64));
    if (privDecoded.TrimStart().StartsWith("<"))
        rsa.FromXmlString(privDecoded);
    else
        rsa.ImportFromPem(privDecoded);
    Console.WriteLine("BTFLY: RSA keys loaded from environment.");
}

var rsaSecurityKey = new RsaSecurityKey(rsa) { KeyId = "btfly-node-key-1" };

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = builder.Configuration["Btfly:JwtIssuer"],
            ValidateAudience         = true,
            ValidAudience            = builder.Configuration["Btfly:JwtAudience"],
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = rsaSecurityKey,
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<INodeService, NodeService>();
builder.Services.AddScoped<IPostService, PostService>();
builder.Services.AddScoped<IModerationService, ModerationService>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "BTFLY Node API", Version = "v0.1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization", Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer", BearerFormat = "JWT", In = ParameterLocation.Header
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {{
        new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }},
        Array.Empty<string>()
    }});
});

// Cors__AllowedOrigins accepts a comma-separated list of origins.
// e.g. on Railway: Cors__AllowedOrigins=https://app.btfly.social,https://mynodedomain.com
var allowedOrigins = (builder.Configuration["Cors__AllowedOrigins"]
                   ?? builder.Configuration["Cors:AllowedOrigins"]
                   ?? "http://localhost:8080,http://localhost:3000")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
    options.AddPolicy("BtflyPolicy", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader().AllowAnyMethod()));

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls("http://0.0.0.0:" + port);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BtflyDbContext>();
    db.Database.EnsureCreated();
}

app.UseMiddleware<ExceptionMiddleware>();
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "BTFLY Node API v0.1"));
app.UseCors("BtflyPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
