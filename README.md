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
dotnet run --project src/Shine.Api
```

Valide a API em `GET /api/health`.
