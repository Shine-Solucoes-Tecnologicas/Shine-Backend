# Shine Backend

Base da solução .NET do Shine, preparada para arquitetura modular.

## Estrutura

- `src/Shine.Api`: composição HTTP e endpoints da API.
- `src/Shine.Application`: casos de uso e contratos da aplicação.
- `src/Shine.Domain`: regras e abstrações de domínio.
- `src/Shine.Infrastructure`: integrações e implementações técnicas.
- `src/Shine.Shared`: tipos compartilhados entre camadas.

Nenhum módulo funcional foi criado nesta etapa.

## Executar

```bash
export ConnectionStrings__ShineDb='Host=localhost;Port=5432;Database=shine;Username=shine;Password=change-me'
dotnet run --project src/Shine.Api
```

Consulte [docs/configuration.md](docs/configuration.md) para a configuração por ambiente.

Valide a API em `GET /api/health`.
