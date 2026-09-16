using Microsoft.OpenApi;

namespace PMT.Api.Extensions;
public static class SwaggerExtensions
{
    public static IServiceCollection AddPmtSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            const string schemeName = "Bearer";

            options.AddSecurityDefinition(schemeName, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Enter the JWT access token. The 'Bearer' prefix is added automatically."
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(schemeName, document)] = []
            });
        });
        return services;
    }
}
