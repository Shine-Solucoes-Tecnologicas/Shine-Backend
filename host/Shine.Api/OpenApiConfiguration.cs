using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Shine.Api;

public static class OpenApiConfiguration
{
    public const string DocumentName = "v1";
    public const string DocumentPath = "/openapi/v1.json";

    public static IServiceCollection AddShineOpenApi(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(DocumentName, new OpenApiInfo
            {
                Title = "Shine Backend API",
                Version = DocumentName,
                Description = "Contrato HTTP da plataforma Shine."
            });
            options.CustomSchemaIds(SchemaId);
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "JWT enviado no cabeçalho Authorization usando o esquema Bearer."
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
            options.OperationFilter<AuthorizationOpenApiOperationFilter>();
        });
        return services;
    }

    public static WebApplication UseShineOpenApi(this WebApplication app)
    {
        app.UseSwagger(options => options.RouteTemplate = "openapi/{documentName}.json");
        if (app.Environment.IsDevelopment())
        {
            app.UseSwaggerUI(options =>
            {
                options.RoutePrefix = "docs";
                options.DocumentTitle = "Shine Backend API";
                options.SwaggerEndpoint(DocumentPath, "Shine Backend API v1");
            });
        }
        return app;
    }

    private static string SchemaId(Type type)
    {
        if (!type.IsGenericType)
            return (type.FullName ?? type.Name).Replace('+', '.');

        var genericName = type.Name[..type.Name.IndexOf('`')];
        return $"{string.Join("And", type.GetGenericArguments().Select(SchemaId))}.{genericName}";
    }
}

public sealed class AuthorizationOpenApiOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        var allowsAnonymous = metadata.OfType<IAllowAnonymous>().Any();
        var requiresAuthorization = metadata.OfType<IAuthorizeData>().Any();

        if (allowsAnonymous || !requiresAuthorization)
        {
            operation.Security = [];
            return;
        }

        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", new OpenApiResponse
        {
            Description = "Token ausente, inválido, expirado ou revogado."
        });
        operation.Responses.TryAdd("403", new OpenApiResponse
        {
            Description = "Usuário autenticado sem a permissão, papel, módulo ou escopo exigido."
        });
    }
}
