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
            options.OperationFilter<AdministrativeOpenApiOperationFilter>();
            options.OperationFilter<IdentityAccessOpenApiOperationFilter>();
            options.OperationFilter<BillingOpenApiOperationFilter>();
            options.OperationFilter<BusinessCatalogOpenApiOperationFilter>();
            options.OperationFilter<SchedulingOpenApiOperationFilter>();
            options.OperationFilter<AuxiliaryCapabilitiesOpenApiOperationFilter>();
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

public sealed class AuxiliaryCapabilitiesOpenApiOperationFilter : IOperationFilter
{
    private static readonly IReadOnlyDictionary<string, AuxiliaryOperationDocumentation> Operations =
        new Dictionary<string, AuxiliaryOperationDocumentation>(StringComparer.Ordinal)
        {
            ["CustomersController.List"] = new("Listar clientes", "Retorna clientes da unidade com paginação, busca, ativação e ordenação.", ["400"]),
            ["CustomersController.Create"] = new("Criar cliente", "Cria cliente ativo e aplica unicidade das referências normalizadas na unidade.", ["400", "409"]),
            ["CustomersController.Get"] = new("Consultar cliente", "Retorna cliente somente dentro da unidade autorizada.", ["404"]),
            ["CustomersController.History"] = new("Consultar histórico do cliente", "Combina o cliente com agendamentos paginados sem atravessar unidades.", ["404"]),
            ["CustomersController.Update"] = new("Atualizar cliente", "Altera dados do cliente preservando identidade e histórico.", ["400", "404", "409"]),
            ["CustomersController.Deactivate"] = new("Desativar cliente", "Desativa o cliente sem remover seus registros históricos.", ["404"]),
            ["CustomersController.Reactivate"] = new("Reativar cliente", "Reativa o cliente quando não existe conflito com referências ativas.", ["404", "409"]),
            ["FilesController.Upload"] = new("Enviar arquivo", "Persiste o conteúdo e metadados da unidade; falha de metadados compensa removendo o arquivo físico.", ["400"]),
            ["FilesController.Download"] = new("Baixar arquivo", "Entrega arquivo ativo somente quando metadado, unidade e permissão de leitura coincidem.", ["404"]),
            ["FilesController.Delete"] = new("Excluir arquivo", "Marca exclusão e tenta remover o conteúdo; retorna 202 quando a reconciliação assíncrona ainda é necessária.", ["404"]),
            ["NotificationsController.List"] = new("Listar notificações", "Retorna notificações pessoais e gerais visíveis ao usuário, com estado de leitura individual."),
            ["NotificationsController.Create"] = new("Criar notificação", "Cria notificação geral ou para usuário ativo da mesma unidade.", ["400"]),
            ["NotificationsController.MarkAsRead"] = new("Marcar notificação como lida", "Registra leitura individual de uma notificação visível ao usuário.", ["404"]),
            ["NotificationsController.MarkAllAsRead"] = new("Marcar todas como lidas", "Registra de forma idempotente a leitura de todas as notificações visíveis."),
            ["DashboardController.GetWidgets"] = new("Listar widgets disponíveis", "Resolve widgets conforme módulos e permissões efetivas do usuário."),
            ["DashboardController.GetWidgetData"] = new("Consultar dados do widget", "Retorna dados somente se o widget estiver disponível ao usuário no período válido.", ["400", "404"]),
            ["DashboardController.GetLayout"] = new("Consultar layout do dashboard", "Resolve o layout salvo contra o catálogo de widgets atualmente disponível.", ["404"]),
            ["DashboardController.SaveLayout"] = new("Salvar layout do dashboard", "Valida e persiste o layout do usuário na unidade.", ["400"]),
            ["FunctionalSettingsController.Get"] = new("Consultar configuração funcional", "Resolve a configuração efetiva da unidade sem alterar valores.", ["404"]),
            ["FunctionalSettingsController.Set"] = new("Definir configuração funcional", "Define valor parametrizado para a unidade atual."),
            ["FeatureFlagsController.Get"] = new("Consultar Feature Flags", "Retorna flags efetivas no contexto da unidade."),
            ["FeatureFlagsController.Catalog"] = new("Consultar catálogo de Feature Flags", "Lista chaves registradas tecnicamente, sem seus valores por unidade."),
            ["FeatureFlagsController.Set"] = new("Definir Feature Flag", "Ativa ou desativa uma chave registrada para a unidade atual."),
            ["HealthController.Get"] = new("Consultar saúde da API", "Executa verificações de PostgreSQL e RabbitMQ sem expor mensagens internas de exceção.", ["503"])
        };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var controller = context.MethodInfo.DeclaringType?.Name;
        if (controller is null || !Operations.TryGetValue($"{controller}.{context.MethodInfo.Name}", out var documentation)) return;

        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        var isPublic = metadata.OfType<IAllowAnonymous>().Any();
        var permissions = metadata.OfType<RequiresPermissionAttribute>()
            .Select(attribute => attribute.PermissionCode)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(permission => permission, StringComparer.Ordinal)
            .ToArray();
        var modules = metadata.OfType<RequiresModuleAttribute>()
            .Select(attribute => attribute.ModuleCode)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(module => module, StringComparer.Ordinal)
            .ToArray();
        var access = isPublic
            ? "Público"
            : permissions.Length > 0
                ? $"Token da unidade; permissões {string.Join(" + ", permissions.Select(permission => $"`{permission}`"))}" +
                    (modules.Length > 0 ? $"; módulos {string.Join(" + ", modules.Select(module => $"`{module}`"))}" : string.Empty)
                : "Usuário autenticado no contexto da unidade; acesso limitado aos próprios recursos visíveis";

        operation.OperationId = $"Auxiliary_{controller.Replace("Controller", string.Empty)}_{context.MethodInfo.Name}";
        operation.Summary = documentation.Summary;
        operation.Description = $"Acesso exigido: {access}.\n\nEfeito operacional: {documentation.OperationalEffect}";
        operation.Responses ??= new OpenApiResponses();
        foreach (var status in documentation.AdditionalResponses)
            operation.Responses.TryAdd(status, new OpenApiResponse { Description = ResponseDescription(status) });
    }

    private static string ResponseDescription(string status) => status switch
    {
        "400" => "Contrato, filtro, período, destinatário, arquivo ou layout inválido.",
        "404" => "Recurso não encontrado no contexto autorizado.",
        "409" => "Conflito de unicidade ou estado do recurso.",
        "503" => "Uma ou mais dependências necessárias não estão saudáveis.",
        _ => "Resposta de erro documentada."
    };

    private sealed record AuxiliaryOperationDocumentation(
        string Summary,
        string OperationalEffect,
        IReadOnlyCollection<string> AdditionalResponses)
    {
        public AuxiliaryOperationDocumentation(string summary, string operationalEffect)
            : this(summary, operationalEffect, []) { }
    }
}

public sealed class SchedulingOpenApiOperationFilter : IOperationFilter
{
    private static readonly IReadOnlyDictionary<string, SchedulingOperationDocumentation> Operations =
        new Dictionary<string, SchedulingOperationDocumentation>(StringComparer.Ordinal)
        {
            ["UpdateProfessionalCapacity"] = new("Configurar capacidade do profissional", "Define o máximo de agendamentos simultâneos do profissional, entre 1 e 100.", ["400", "404"]),
            ["UpdateServiceDurationPolicy"] = new("Configurar duração variável do serviço", "Define regra versionada por atributo ou restaura a duração fixa do Business Catalog.", ["400", "404"]),
            ["UpdateProfessionalServiceDuration"] = new("Configurar duração específica do profissional", "Define ou remove override de duração para um vínculo profissional–serviço ativo.", ["400", "404"]),
            ["Availability"] = new("Listar disponibilidade recorrente", "Retorna regras ativas do profissional na unidade."),
            ["AddAvailability"] = new("Adicionar disponibilidade recorrente", "Cria regra semanal quando não há sobreposição com outra regra ativa.", ["404", "409"]),
            ["RemoveAvailability"] = new("Remover disponibilidade recorrente", "Desativa a regra, preservando seu registro.", ["404"]),
            ["AddBlock"] = new("Adicionar bloqueio de agenda", "Bloqueia um período UTC do profissional quando não há outro bloqueio sobreposto.", ["404", "409"]),
            ["Exceptions"] = new("Listar exceções de disponibilidade", "Retorna exceções integrais ou parciais, com filtros opcionais por data."),
            ["AddException"] = new("Adicionar exceção de disponibilidade", "Cria indisponibilidade integral ou janela excepcional sem sobreposição na mesma data.", ["404", "409"]),
            ["RemoveException"] = new("Remover exceção de disponibilidade", "Exclui a exceção identificada dentro da unidade.", ["404"]),
            ["GetSettings"] = new("Consultar configurações da agenda", "Retorna intervalo dos slots, buffers, fuso, política de conflito e capacidade padrão."),
            ["UpdateSettings"] = new("Atualizar configurações da agenda", "Valida e persiste intervalo, buffers, fuso, conflito e capacidade padrão.", ["400"]),
            ["Slots"] = new("Consultar slots disponíveis", "Calcula slots em UTC a partir de catálogo, fuso, duração, regras, exceções, bloqueios, ocupação e capacidade.", ["404"]),
            ["Appointments"] = new("Listar agendamentos", "Retorna página ordenada de agendamentos que intersectam o período UTC informado."),
            ["CreateAppointment"] = new("Criar agendamento", "Valida catálogo, cliente, duração, bloqueios, conflitos, capacidade e reserva do entitlement de agendamentos ativos.", ["400", "404", "409"]),
            ["ChangeAppointmentStatus"] = new("Alterar estado do agendamento", "Aplica transição válida, publica evento e reserva ou libera o entitlement conforme o novo estado.", ["404", "409"]),
            ["RescheduleAppointment"] = new("Reagendar atendimento", "Usa ExpectedVersion, lock por profissional e valida conflitos antes de publicar o reagendamento.", ["404", "409"]),
            ["CreateAppointmentPublic"] = new("Criar agendamento público", "Revalida o slot sob lock, limita capacidade e reserva entitlement antes de persistir e publicar o evento.", ["400", "404", "409", "429"]),
            ["SlotsPublic"] = new("Consultar slots públicos", "Calcula slots para profissional e serviço ativos sem expor dados de outros agendamentos.", ["400", "404", "429"])
        };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var controller = context.MethodInfo.DeclaringType?.Name;
        if (controller is not ("SchedulingController" or "PublicSchedulingController")) return;

        var publicOperation = controller == "PublicSchedulingController";
        var documentationKey = publicOperation ? $"{context.MethodInfo.Name}Public" : context.MethodInfo.Name;
        if (!Operations.TryGetValue(documentationKey, out var documentation)) return;

        var permissions = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<RequiresPermissionAttribute>()
            .Select(attribute => attribute.PermissionCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var scopedAccess = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<SchedulingAccessAttribute>()
            .Select(attribute => attribute.Requirement)
            .SingleOrDefault();
        var access = publicOperation
            ? "Público, com módulo `SCHEDULING` ativo na unidade da rota e rate limit por IP efetivo + unidade"
            : scopedAccess is not null
                ? $"Token da unidade, módulo `SCHEDULING` e concessão `{scopedAccess}`"
                : $"Token da unidade, módulo `SCHEDULING` e permissão `{permissions.Single()}`";

        operation.OperationId = $"Scheduling_{(publicOperation ? "Public" : "Authenticated")}_{context.MethodInfo.Name}";
        operation.Summary = documentation.Summary;
        operation.Description = $"Acesso exigido: {access}.\n\nEfeito operacional: {documentation.OperationalEffect}";
        operation.Responses ??= new OpenApiResponses();
        foreach (var status in documentation.AdditionalResponses)
            operation.Responses.TryAdd(status, new OpenApiResponse { Description = ResponseDescription(status) });
    }

    private static string ResponseDescription(string status) => status switch
    {
        "400" => "Período, duração, configuração, referência ou atributos inválidos.",
        "404" => "Recurso ausente na unidade ou módulo de Scheduling indisponível.",
        "409" => "Sobreposição, capacidade, conflito, entitlement ou versão concorrente impede a operação.",
        "429" => "Limite público de 30 requisições por minuto para o IP efetivo e a unidade excedido.",
        _ => "Resposta de erro documentada."
    };

    private sealed record SchedulingOperationDocumentation(
        string Summary,
        string OperationalEffect,
        IReadOnlyCollection<string> AdditionalResponses)
    {
        public SchedulingOperationDocumentation(string summary, string operationalEffect)
            : this(summary, operationalEffect, []) { }
    }
}

public sealed class BusinessCatalogOpenApiOperationFilter : IOperationFilter
{
    private static readonly IReadOnlyDictionary<string, CatalogOperationDocumentation> Operations =
        new Dictionary<string, CatalogOperationDocumentation>(StringComparer.Ordinal)
        {
            ["Professionals"] = new("Listar profissionais", "Retorna profissionais da unidade com paginação e filtros por nome e ativação.", ["400"]),
            ["Professional"] = new("Consultar profissional", "Retorna um profissional da unidade atual.", ["404"]),
            ["CreateProfessional"] = new("Criar profissional", "Cria um profissional desacoplado da agenda; o usuário opcional precisa estar ativo na unidade.", ["400", "409"]),
            ["UpdateProfessional"] = new("Atualizar profissional", "Altera nome e vínculo opcional com usuário ativo na mesma unidade.", ["400", "404", "409"]),
            ["SetProfessionalActivation"] = new("Alterar ativação do profissional", "Ativa ou desativa o profissional sem apagar seu histórico.", ["404"]),
            ["DeleteProfessional"] = new("Desativar profissional", "Desativa de forma idempotente; a operação não remove fisicamente o cadastro."),
            ["Services"] = new("Listar serviços", "Retorna serviços da unidade com paginação e filtros por nome e ativação.", ["400"]),
            ["Service"] = new("Consultar serviço", "Retorna um serviço da unidade atual.", ["404"]),
            ["CreateService"] = new("Criar serviço", "Cria serviço com duração positiva e nome único na unidade.", ["400", "409"]),
            ["UpdateService"] = new("Atualizar serviço", "Altera nome e duração preservando a identidade do serviço.", ["400", "404", "409"]),
            ["SetServiceActivation"] = new("Alterar ativação do serviço", "Ativa ou desativa o serviço sem apagar seu histórico.", ["404"]),
            ["DeleteService"] = new("Desativar serviço", "Desativa de forma idempotente; a operação não remove fisicamente o cadastro."),
            ["ProfessionalServices"] = new("Listar serviços do profissional", "Retorna vínculos ativos e inativos do profissional dentro da unidade.", ["404"]),
            ["AssociateService"] = new("Vincular serviço ao profissional", "Cria ou reativa o vínculo de forma idempotente, sem criar dependência do Business Catalog com o Scheduling.", ["404"]),
            ["DisassociateService"] = new("Desvincular serviço do profissional", "Desativa o vínculo de forma idempotente e preserva referências históricas.")
        };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType?.Name != "BusinessCatalogController" ||
            !Operations.TryGetValue(context.MethodInfo.Name, out var documentation)) return;

        var path = context.ApiDescription.RelativePath?.Split('?', 2)[0] ?? string.Empty;
        var legacy = path.StartsWith("api/business-catalog", StringComparison.OrdinalIgnoreCase);
        var permission = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<RequiresPermissionAttribute>()
            .Select(attribute => attribute.PermissionCode)
            .Single();

        operation.OperationId = $"BusinessCatalog_{(legacy ? "Legacy" : "V1")}_{context.MethodInfo.Name}";
        operation.Summary = documentation.Summary;
        operation.Deprecated = legacy;
        operation.Description = $"Permissão exigida: `{permission}` na unidade atual.\n\nEfeito operacional: {documentation.OperationalEffect}\n\n" +
            (legacy
                ? "Compatibilidade: rota legada descontinuada; a resposta informa `Deprecation: true` e aponta `/api/v1/business-catalog` no header `Link`."
                : "Compatibilidade: rota canônica versionada da API.");
        operation.Responses ??= new OpenApiResponses();
        foreach (var status in documentation.AdditionalResponses)
            operation.Responses.TryAdd(status, new OpenApiResponse { Description = ResponseDescription(status) });
    }

    private static string ResponseDescription(string status) => status switch
    {
        "400" => "Filtro, profissional, serviço ou referência de usuário inválida.",
        "404" => "Profissional ou serviço não encontrado na unidade autorizada.",
        "409" => "Nome de profissional ou serviço já existente na unidade.",
        _ => "Resposta de erro documentada."
    };

    private sealed record CatalogOperationDocumentation(
        string Summary,
        string OperationalEffect,
        IReadOnlyCollection<string> AdditionalResponses)
    {
        public CatalogOperationDocumentation(string summary, string operationalEffect)
            : this(summary, operationalEffect, []) { }
    }
}

public sealed class BillingOpenApiOperationFilter : IOperationFilter
{
    private static readonly IReadOnlyDictionary<string, BillingOperationDocumentation> Operations =
        new Dictionary<string, BillingOperationDocumentation>(StringComparer.OrdinalIgnoreCase)
        {
            ["GET api/account/billing/subscriptions"] = new("Listar assinaturas da organização", "Permissão `subscriptions.read` em todas as unidades de cada assinatura", "Retorna somente assinaturas integralmente visíveis ao operador."),
            ["GET api/account/billing/subscriptions/{subscriptionId}"] = new("Consultar assinatura", "Permissão `subscriptions.read` em todas as unidades da assinatura", "Retorna 404 quando a assinatura não pertence à organização ou está fora do escopo do operador.", ["404"]),
            ["GET api/account/billing/invoices"] = new("Listar faturas da organização", "Permissão `billing.read` em todas as unidades da assinatura", "Retorna somente faturas de assinaturas integralmente visíveis; não expõe registros financeiros de outras organizações."),
            ["POST api/account/billing/subscriptions/checkout"] = new("Iniciar checkout de assinatura", "Permissão `subscriptions.manage` ou `billing.manage` em todas as unidades solicitadas", "Cria uma assinatura em Draft e inicia checkout HTTPS no provedor; a chave de idempotência evita duplicação.", ["400", "404", "409", "503"]),
            ["POST api/account/billing/subscriptions/{subscriptionId}/plan-change"] = new("Solicitar troca de plano", "Permissão `subscriptions.manage` ou `billing.manage` em todas as unidades da assinatura", "Registra o plano pendente e sua vigência; a projeção posterior atualiza módulos e entitlements.", ["404", "409"]),
            ["POST api/account/billing/subscriptions/{subscriptionId}/cancellation"] = new("Solicitar cancelamento", "Permissão `subscriptions.manage` ou `billing.manage` em todas as unidades da assinatura", "Solicita cancelamento imediato ou ao fim do período e sincroniza a solicitação com o provedor configurado.", ["404", "409", "503"]),
            ["GET api/account/billing/contracts"] = new("Listar contratos comerciais visíveis", "Permissão `billing.read` no contexto da organização", "Retorna contratos da organização, ocultando autoria, justificativas e termos marcados como internos.")
        };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var method = context.ApiDescription.HttpMethod;
        var path = NormalizePath(context.ApiDescription.RelativePath);
        if (method is null || path is null || !Operations.TryGetValue($"{method} {path}", out var documentation)) return;

        operation.OperationId = $"Billing_{context.MethodInfo.DeclaringType?.Name.Replace("Controller", string.Empty)}_{context.MethodInfo.Name}_{method}";
        operation.Summary = documentation.Summary;
        operation.Description = $"Acesso exigido: {documentation.Access}.\n\nEfeito operacional: {documentation.OperationalEffect}";
        operation.Responses ??= new OpenApiResponses();
        foreach (var status in documentation.AdditionalResponses)
            operation.Responses.TryAdd(status, new OpenApiResponse { Description = ResponseDescription(status) });
    }

    private static string? NormalizePath(string? relativePath)
    {
        if (relativePath is null) return null;
        var path = relativePath.Split('?', 2)[0];
        return System.Text.RegularExpressions.Regex.Replace(path, @":(guid|int|long|bool|datetime)", string.Empty);
    }

    private static string ResponseDescription(string status) => status switch
    {
        "400" => "Dados da assinatura, escopo, callback ou idempotência inválidos.",
        "404" => "Assinatura, plano ou recurso não encontrado no escopo autorizado.",
        "409" => "Conflito de estado, unidade já assinada ou reutilização incompatível da chave idempotente.",
        "503" => "Provedor de Billing ausente ou temporariamente indisponível.",
        _ => "Resposta de erro documentada."
    };

    private sealed record BillingOperationDocumentation(
        string Summary,
        string Access,
        string OperationalEffect,
        IReadOnlyCollection<string> AdditionalResponses)
    {
        public BillingOperationDocumentation(string summary, string access, string operationalEffect)
            : this(summary, access, operationalEffect, []) { }
    }
}

public sealed class IdentityAccessOpenApiOperationFilter : IOperationFilter
{
    private static readonly IReadOnlyDictionary<string, IdentityAccessOperationDocumentation> Operations =
        new Dictionary<string, IdentityAccessOperationDocumentation>(StringComparer.OrdinalIgnoreCase)
        {
            ["POST api/auth/register"] = new("Registrar usuário", "Público", "Cria uma identidade sem conceder acesso a unidades."),
            ["POST api/auth/login"] = new("Autenticar usuário", "Público", "Valida credenciais e inicia uma sessão; o contexto de unidade depende dos vínculos acessíveis."),
            ["POST api/auth/refresh"] = new("Renovar sessão", "Público, mediante refresh token ativo", "Rotaciona o refresh token e invalida o token reutilizado."),
            ["POST api/auth/logout"] = new("Encerrar sessão", "Público, mediante refresh token", "Revoga a sessão correspondente quando o token é reconhecido."),
            ["POST api/auth/logout-all"] = new("Encerrar todas as sessões", "Usuário autenticado", "Revoga todas as sessões ativas do usuário."),
            ["GET api/auth/sessions"] = new("Listar sessões", "Usuário autenticado", "Lista somente as sessões pertencentes ao usuário autenticado."),
            ["GET api/auth/sessions/current"] = new("Consultar sessão atual", "Usuário autenticado", "Retorna a sessão identificada pelo token atual."),
            ["DELETE api/auth/sessions/{sessionId}"] = new("Revogar sessão", "Usuário autenticado", "Revoga uma sessão do próprio usuário; não permite atuar sobre sessões alheias."),
            ["POST api/auth/select-tenant"] = new("Selecionar unidade na sessão", "Usuário autenticado e vínculo ativo com a unidade", "Emite token limitado à unidade selecionada e pode rotacionar o refresh token."),
            ["POST api/auth/change-password"] = new("Alterar senha", "Usuário autenticado", "Troca a senha e revoga as demais sessões conforme a política de identidade."),
            ["POST api/auth/password/recovery"] = new("Solicitar recuperação de senha", "Público", "Produz resposta neutra para evitar enumeração de usuários e inicia a recuperação quando aplicável."),
            ["POST api/auth/password/reset"] = new("Redefinir senha", "Público, mediante token de recuperação válido", "Consome o token, redefine a senha e invalida sessões conforme a política de identidade."),

            ["GET api/tenants/accessible"] = new("Listar unidades acessíveis", "Usuário autenticado", "Retorna apenas vínculos ativos com unidades ativas."),
            ["POST api/tenants"] = new("Criar unidade", "Usuário autenticado", "Cria a unidade, vincula-a à organização atual ou a uma nova organização e emite um token para o proprietário."),
            ["POST api/tenants/select"] = new("Selecionar unidade", "Usuário autenticado e vínculo ativo com a unidade", "Emite token no contexto da unidade e rotaciona o refresh token quando informado."),
            ["POST api/tenants/switch"] = new("Trocar de unidade", "Usuário autenticado e vínculo ativo com a unidade", "Alias compatível da seleção de unidade; aplica as mesmas validações e rotação."),

            ["GET api/tenants/users"] = new("Listar usuários da unidade", "Permissão `users.read` na unidade atual", "Retorna somente usuários vinculados ao contexto autorizado."),
            ["POST api/tenants/users"] = new("Adicionar usuário à unidade", "Permissão `users.manage` na unidade atual", "Cria ou reativa o vínculo do usuário com a unidade."),
            ["DELETE api/tenants/users/{userId}"] = new("Remover usuário da unidade", "Permissão `users.manage` na unidade atual", "Desativa o vínculo operacional apenas nesta unidade."),
            ["GET api/tenants/users/{userId}"] = new("Consultar usuário da unidade", "Permissão `users.read` na unidade atual", "Retorna o usuário somente dentro do contexto autorizado."),
            ["PUT api/tenants/users/{userId}/status"] = new("Alterar status do usuário na unidade", "Permissão `users.manage` na unidade atual", "Ativa ou desativa o vínculo do usuário nesta unidade."),
            ["PUT api/tenants/users/{userId}/metadata"] = new("Atualizar metadados do usuário", "Permissão `users.manage` na unidade atual", "Altera metadados do vínculo sem modificar a identidade global."),
            ["GET api/tenants/users/{userId}/roles"] = new("Listar papéis do usuário na unidade", "Permissão `roles.manage` na unidade atual", "Consulta atribuições locais; não inclui papéis globais da plataforma."),
            ["PUT api/tenants/users/{userId}/roles"] = new("Definir papéis do usuário na unidade", "Permissão `roles.manage` na unidade atual", "Substitui atribuições locais sem conceder administração global."),

            ["GET api/account/access/roles"] = new("Listar papéis da organização", "Permissão contextual `account.access.manage`", "Lista os papéis acumulativos da organização atual."),
            ["GET api/account/access/users"] = new("Listar usuários da organização", "Permissão contextual `account.access.manage`", "Lista usuários, papéis e escopos de unidades e módulos da organização."),
            ["POST api/account/access/users"] = new("Adicionar usuário à organização", "Permissão contextual `account.access.manage`", "Vincula uma identidade existente; usuários internos da plataforma não podem receber acesso operacional."),
            ["PUT api/account/access/users/{userId}/roles/{roleId}"] = new("Atribuir papel organizacional", "Permissão contextual `account.access.manage` no escopo concedido", "Atribui papel sobre uma ou mais unidades e módulos, impedindo escalada além do escopo do operador."),
            ["DELETE api/account/access/users/{userId}/roles/{roleId}"] = new("Revogar papel organizacional", "Permissão contextual `account.access.manage` no escopo concedido", "Remove a atribuição, sincroniza vínculos de unidade e preserva ao menos um administrador."),

            ["GET api/modules"] = new("Listar módulos registrados", "Usuário autenticado", "Retorna o catálogo técnico de módulos sem alterar acesso."),
            ["PUT api/modules/access/{moduleCode}"] = new("Configurar acesso manual ao módulo", "Permissão `modules.manage` na unidade atual", "Altera o acesso manual legado da unidade; planos e entitlements permanecem controles independentes."),
            ["GET api/plans/access/{moduleCode}"] = new("Consultar acesso ao módulo", "Usuário autenticado no contexto de uma unidade", "Resolve o acesso efetivo considerando plano e override da unidade."),
            ["PUT api/plans/plan"] = new("Definir plano da unidade", "Permissão `tenant.manage` na unidade atual", "Altera o plano associado à unidade pelo mecanismo legado; o Billing continua responsável pelo ciclo comercial."),
            ["PUT api/plans/override/{moduleCode}"] = new("Definir override de módulo", "Permissão `modules.manage` na unidade atual", "Sobrescreve o acesso ao módulo para a unidade sem modificar o catálogo do plano.")
        };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var method = context.ApiDescription.HttpMethod;
        var path = NormalizePath(context.ApiDescription.RelativePath);
        if (method is null || path is null || !Operations.TryGetValue($"{method} {path}", out var documentation)) return;

        operation.OperationId = $"Access_{context.MethodInfo.DeclaringType?.Name.Replace("Controller", string.Empty)}_{context.MethodInfo.Name}_{method}";
        operation.Summary = documentation.Summary;
        operation.Description = $"Acesso exigido: {documentation.Access}.\n\nEfeito operacional: {documentation.OperationalEffect}";
    }

    private static string? NormalizePath(string? relativePath)
    {
        if (relativePath is null) return null;
        var path = relativePath.Split('?', 2)[0];
        return System.Text.RegularExpressions.Regex.Replace(path, @":(guid|int|long|bool|datetime)", string.Empty);
    }

    private sealed record IdentityAccessOperationDocumentation(string Summary, string Access, string OperationalEffect);
}

public sealed class AdministrativeOpenApiOperationFilter : IOperationFilter
{
    private static readonly IReadOnlyDictionary<string, AdministrativeOperationDocumentation> Operations =
        new Dictionary<string, AdministrativeOperationDocumentation>(StringComparer.OrdinalIgnoreCase)
        {
            ["GET api/admin/access/roles"] = new("Listar papéis globais", "Consulta os papéis globais e suas permissões.", "Somente leitura; não altera acessos."),
            ["GET api/admin/access/users/{userId}/roles"] = new("Consultar papéis globais de um usuário", "Retorna os papéis globais atribuídos ao usuário informado.", "Somente leitura; retorna 404 quando o usuário não existe.", ["404"]),
            ["PUT api/admin/access/users/{userId}/roles/{roleName}"] = new("Atribuir papel global", "Atribui um papel global existente a um usuário existente de forma idempotente.", "Pode ampliar o acesso administrativo global do usuário; não concede acesso operacional às unidades.", ["404"]),
            ["DELETE api/admin/access/users/{userId}/roles/{roleName}"] = new("Remover papel global", "Remove do usuário a atribuição do papel global informado.", "Revoga capacidades administrativas derivadas do papel; retorna 404 quando a atribuição não existe.", ["404"]),

            ["GET api/admin/tenants"] = new("Listar unidades administradas", "Lista unidades com paginação e filtros por nome e estado.", "Somente leitura; não troca o contexto operacional do administrador."),
            ["GET api/admin/tenants/{tenantId}"] = new("Consultar unidade", "Retorna estado e quantidade de usuários ativos da unidade.", "Somente leitura; não concede acesso aos dados operacionais da unidade.", ["404"]),
            ["PUT api/admin/tenants/{tenantId}/suspend"] = new("Suspender unidade", "Suspende a unidade mediante justificativa obrigatória.", "Bloqueia a unidade e revoga as sessões ativas vinculadas a ela, preservando sessões do mesmo usuário em outras unidades.", ["400", "404"]),
            ["PUT api/admin/tenants/{tenantId}/reactivate"] = new("Reativar unidade", "Remove a suspensão administrativa da unidade.", "Restaura o estado ativo, mas não recria nem reativa sessões previamente revogadas.", ["404"]),

            ["GET api/admin/users"] = new("Listar usuários da plataforma", "Lista usuários com paginação e filtros por email, estado e unidade.", "Somente leitura; apresenta apenas vínculos ativos com unidades."),
            ["GET api/admin/users/{userId}"] = new("Consultar usuário da plataforma", "Retorna estado, bloqueio, unidades ativas e papéis globais do usuário.", "Somente leitura; não assume a identidade nem o contexto operacional do usuário.", ["404"]),
            ["PUT api/admin/users/{userId}/block"] = new("Bloquear usuário", "Bloqueia um usuário mediante justificativa obrigatória.", "Revoga todas as sessões ativas do usuário em toda a plataforma.", ["400", "404"]),
            ["PUT api/admin/users/{userId}/unblock"] = new("Desbloquear usuário", "Remove o bloqueio administrativo do usuário.", "Permite nova autenticação, mas não restaura sessões previamente revogadas.", ["404"]),

            ["GET api/admin/plans"] = new("Listar planos da plataforma", "Retorna planos com módulos adicionais e entitlements configurados.", "Somente leitura; o módulo CORE é base comum e não aparece como adicional do plano."),
            ["POST api/admin/plans"] = new("Criar plano da plataforma", "Cria um plano parametrizado com módulos registrados e entitlements conhecidos.", "Disponibiliza o plano para uso comercial; não altera automaticamente assinaturas existentes.", ["400", "409"]),

            ["GET api/admin/billing/contracts"] = new("Listar contratos comerciais", "Lista contratos comerciais, opcionalmente filtrados por organização, incluindo revisões e termos internos.", "Somente leitura de condições comerciais sensíveis; não altera assinatura, plano ou operação do cliente."),
            ["GET api/admin/billing/contracts/{contractId}/history"] = new("Consultar histórico do contrato comercial", "Retorna todas as revisões e termos internos do contrato comercial.", "Somente leitura do histórico comercial e de seus responsáveis; não reativa revisões anteriores.", ["404"]),
            ["POST api/admin/billing/contracts"] = new("Criar contrato comercial", "Cria um contrato parametrizado para uma organização e, opcionalmente, o vincula a uma assinatura da mesma organização.", "Registra condições comerciais vigentes e sua autoria; não executa cobrança nem troca o plano da assinatura.", ["400", "404", "409"]),
            ["POST api/admin/billing/contracts/{contractId}/revisions"] = new("Revisar contrato comercial", "Cria uma nova revisão imutável de termos e vigência para o contrato informado.", "Substitui a revisão comercial corrente, preservando o histórico; não executa cobrança retroativa.", ["400", "404"]),
            ["POST api/admin/billing/contracts/{contractId}/end"] = new("Encerrar contrato comercial", "Encerra o contrato mediante justificativa e data informadas.", "Finaliza a vigência comercial sem excluir o histórico e sem cancelar automaticamente a assinatura.", ["400", "404"]),

            ["GET api/admin/audit"] = new("Pesquisar auditoria global", "Lista eventos de auditoria com paginação e filtros por entidade, ação, usuário, unidade e período.", "Somente leitura de dados sensíveis de auditoria; não concede acesso operacional à unidade."),
            ["GET api/admin/audit/{auditId}"] = new("Consultar evento de auditoria", "Retorna o evento de auditoria e seus valores anteriores e posteriores persistidos.", "Somente leitura; o conteúdo deve ser tratado como dado administrativo sensível.", ["404"]),
            ["GET api/admin/operational-logs"] = new("Pesquisar logs operacionais", "Lista logs com paginação e filtros por nível, categoria, correlação e período.", "Somente leitura para diagnóstico; mensagens e exceções não devem ser expostas a usuários operacionais."),
            ["GET api/admin/dashboard/summary"] = new("Consultar resumo administrativo", "Retorna indicadores de unidades, usuários, auditoria e logs no período solicitado, limitado a 90 dias.", "Somente leitura agregada; não executa ações nas unidades.", ["400"])
        };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var method = context.ApiDescription.HttpMethod;
        var path = NormalizePath(context.ApiDescription.RelativePath);
        if (method is null || path is null || !Operations.TryGetValue($"{method} {path}", out var documentation))
            return;

        var permissions = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<RequiresGlobalPermissionAttribute>()
            .Select(attribute => attribute.PermissionCode)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(permission => permission, StringComparer.Ordinal)
            .ToArray();

        operation.OperationId = $"Admin_{context.MethodInfo.DeclaringType?.Name.Replace("Controller", string.Empty)}_{context.MethodInfo.Name}";
        operation.Summary = documentation.Summary;
        operation.Description = $"{documentation.Description}\n\nPermissões globais: {string.Join(" + ", permissions.Select(permission => $"`{permission}`"))}.\n\nEfeito operacional: {documentation.OperationalEffect}";
        operation.Responses ??= new OpenApiResponses();
        foreach (var status in documentation.AdditionalResponses)
            operation.Responses.TryAdd(status, new OpenApiResponse { Description = ResponseDescription(status) });
    }

    private static string? NormalizePath(string? relativePath)
    {
        if (relativePath is null) return null;
        var path = relativePath.Split('?', 2)[0];
        return System.Text.RegularExpressions.Regex.Replace(path, @":(guid|int|long|bool|datetime)", string.Empty);
    }

    private static string ResponseDescription(string status) => status switch
    {
        "400" => "Requisição inválida ou regra administrativa não atendida.",
        "404" => "Recurso não encontrado.",
        "409" => "Conflito com o estado ou identificador de um recurso existente.",
        _ => "Resposta de erro documentada."
    };

    private sealed record AdministrativeOperationDocumentation(
        string Summary,
        string Description,
        string OperationalEffect,
        IReadOnlyCollection<string> AdditionalResponses)
    {
        public AdministrativeOperationDocumentation(string summary, string description, string operationalEffect)
            : this(summary, description, operationalEffect, []) { }
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
