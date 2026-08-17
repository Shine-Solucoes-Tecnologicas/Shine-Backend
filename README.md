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
docker compose up -d
export ConnectionStrings__ShineDb='Host=localhost;Port=5433;Database=shine;Username=shine;Password=shine'
dotnet ef database update --project src/Shine.Infrastructure --startup-project src/Shine.Api
dotnet run --project src/Shine.Api
```

## Executar a imagem da API

```bash
docker build -t shine-backend:local .
docker run --rm -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e 'ConnectionStrings__ShineDb=Host=host.docker.internal;Port=5433;Database=shine;Username=shine;Password=shine' \
  -e Jwt__Secret=Shine.Local.Container.Jwt.Secret.With.More.Than.32.Bytes \
  -e RabbitMq__Enabled=false \
  shine-backend:local
```

A imagem usa runtime sem SDK, executa com usuário não-root, expõe somente a porta `8080`, grava arquivos em `/var/lib/shine/storage` e verifica `GET /health`. Segredos são fornecidos somente na execução; não use `ARG`, `ENV` no Dockerfile ou arquivos versionados para valores reais.

Consulte [docs/configuration.md](docs/configuration.md) para a configuração por ambiente.
O contrato técnico de módulos e limites está em [docs/entitlements.md](docs/entitlements.md).
O checklist de produção e as responsabilidades de rotação estão em [docs/deployment-security.md](docs/deployment-security.md).
As verificações automatizadas e a política de exceções estão em [docs/security-ci.md](docs/security-ci.md).

Valide a API em `GET /health`.
