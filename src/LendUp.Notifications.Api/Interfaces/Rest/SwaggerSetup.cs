using Microsoft.OpenApi;

namespace LendUp.Notifications.Api.Interfaces.Rest;

/// <summary>OpenAPI con Swashbuckle (deshabilitado por defecto, igual que springdoc en los servicios Java).</summary>
public static class SwaggerSetup
{
    public static IServiceCollection AddNotificationsSwagger(this IServiceCollection services) =>
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "LendUp Notifications API", Version = "1.0.0", Description = "Notificaciones en la plataforma y recordatorios. JSON snake_case." });
            c.AddSecurityDefinition("firebaseBearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "Firebase ID token",
                Description = "Firebase ID token del estudiante",
            });
            c.AddSecurityRequirement(document => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("firebaseBearer", document)] = [] });
        });
}
