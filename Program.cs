using MeetAndGreet.API.Data;
using MeetAndGreet.API.Hubs;
using MeetAndGreet.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")).UseLazyLoadingProxies());

builder.Services.AddHostedService<CleanupService>();

builder.Services.AddHttpClient<RussianCityService>();
builder.Services.AddMemoryCache();

builder.Services.AddSingleton<RedisService>();
builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
builder.Services.AddSingleton<DeviceFingerprintService>();

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ChannelInitializer>();
builder.Services.AddScoped<TokenService>();

builder.Services.AddHttpContextAccessor();

// Add SignalR
builder.Services
    .AddSignalR(options => 
    { 
        options.DisableImplicitFromServicesParameters = true;
        options.EnableDetailedErrors = true;
        options.MaximumReceiveMessageSize = 1024 * 10;
        options.ClientTimeoutInterval = TimeSpan.FromMinutes(2);
    })
    .AddHubOptions<ChatHub>(options =>
    {
        options.MaximumParallelInvocationsPerClient = 2;
    })
    .AddStackExchangeRedis(builder.Configuration.GetConnectionString("Redis"))
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = null;
    });

// Add CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin",
        builder =>
        {
            builder.WithOrigins("http://localhost:5173")
                   .AllowAnyMethod()
                   .AllowAnyHeader()
                   .AllowCredentials();
        });
});

// Add authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    (path.StartsWithSegments("/chatHub")))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Meet & Greet API",
        Version = "v1",
        Description = "Документация для чата"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<ChannelInitializer>();
    await initializer.InitializeAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseHttpsRedirection();
app.UseRouting();
// Enable CORS
app.UseCors("AllowSpecificOrigin");

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<DeviceCheckMiddleware>();

app.MapControllers();

// Map SignalR Hub
app.MapHub<ChatHub>("/chatHub");

// Clear Redis on startup
ClearRedis(app.Services, app.Configuration);

app.Run();

static void ClearRedis(IServiceProvider services, IConfiguration configuration)
{
    try
    {
        string redisConnectionString = configuration.GetConnectionString("Redis");
        using (ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisConnectionString))
        {
            IDatabase db = redis.GetDatabase();
            var server = redis.GetServer(redis.GetEndPoints()[0]);

            var keys = server.Keys(pattern: "channel:*:users")
                .Concat(server.Keys(pattern: "user:*:channel"))
                .Concat(server.Keys(pattern: "connection:*:user"));

            foreach (var key in keys)
            {
                db.KeyDelete(key);
            }

            Console.WriteLine("Cleared all Redis connection data on startup");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error clearing Redis: {ex.Message}");
    }
}