using System.Text;
using FluentValidation;
using Localll.Common.Auth;
using Localll.Common.Caching;
using Localll.Common.Middleware;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using StackExchange.Redis;

namespace Localll.Common.Extensions;

public static class PlatformExtensions
{
    /// <summary>
    /// Wires the cross-cutting concerns every Localll service shares:
    /// Serilog, JWT bearer auth, Swagger, FluentValidation, OpenTelemetry,
    /// health checks, global exception handling, MassTransit + RabbitMQ and Redis.
    /// </summary>
    public static WebApplicationBuilder AddPlatform(
        this WebApplicationBuilder builder,
        string serviceName,
        Action<IBusRegistrationConfigurator>? configureBus = null)
    {
        builder.Host.UseSerilog((ctx, cfg) => cfg
            .ReadFrom.Configuration(ctx.Configuration)
            .Enrich.WithProperty("Service", serviceName)
            .WriteTo.Console());

        // Serialize enums as their names (e.g. "Assigned") across every service —
        // the SPA and integration events treat these as strings.
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddHealthChecks();
        builder.Services.AddValidatorsFromAssembly(System.Reflection.Assembly.GetCallingAssembly());

        // JWT bearer authentication — tokens are issued by the Identity service.
        var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });
        builder.Services.AddAuthorization();

        // Redis — distributed cache, OTP storage, rate-limit counters.
        var redisConnection = builder.Configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(new ConfigurationOptions
                {
                    EndPoints = { redisConnection },
                    AbortOnConnectFail = false
                }));
            builder.Services.AddSingleton<ICacheService, RedisCacheService>();
        }

        // MassTransit over RabbitMQ — the event bus for all async communication.
        builder.Services.AddMassTransit(bus =>
        {
            bus.SetKebabCaseEndpointNameFormatter();
            configureBus?.Invoke(bus);
            bus.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(builder.Configuration.GetConnectionString("RabbitMq") ?? "rabbitmq://localhost");
                cfg.UseMessageRetry(retry => retry.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)));
                cfg.ConfigureEndpoints(context);
            });
        });

        // OpenTelemetry traces + metrics, exported over OTLP (collector → Prometheus/Grafana).
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddOtlpExporter());

        return builder;
    }

    /// <summary>Standard request pipeline shared by every service.</summary>
    public static WebApplication UsePlatform(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseSerilogRequestLogging();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHealthChecks("/health");

        return app;
    }
}
