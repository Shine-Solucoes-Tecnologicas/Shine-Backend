# Shine Backend

Base da solução .NET do Shine, preparada para arquitetura modular.

## Estrutura

- `host/Shine.Api`: composição HTTP e endpoints da API.
- `platform/Core`: identidade, autorização, clientes e capacidades centrais atuais.
- `platform/Shared`: tipos compartilhados mínimos entre plataforma e módulos.
- `modules/Billing`: domínio, aplicação, infraestrutura e testes de Billing.
- `modules/Scheduling`: domínio, aplicação, infraestrutura e testes de Agenda.
- `tests/Shine.TestKit`: infraestrutura reutilizável para testes de integração.
- `tests/Shine.ArchitectureTests`: limites automatizados entre módulos e camadas.

As regras completas estão em [`docs/architecture.md`](docs/architecture.md).

Nenhum módulo funcional foi criado nesta etapa.

## Executar

```bash
docker compose up -d
export ConnectionStrings__ShineDb='Host=localhost;Port=5433;Database=shine;Username=shine;Password=shine'
dotnet ef database update --project platform/Core/src/Shine.Infrastructure --startup-project host/Shine.Api
dotnet run --project host/Shine.Api
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

## Contribuição e licença

O fluxo de branches, Pull Requests, revisão e validação está em [CONTRIBUTING.md](CONTRIBUTING.md).

Este é um projeto proprietário da Shine Soluções Tecnológicas. Consulte [LICENSE](LICENSE) para os termos aplicáveis.

Valide a API em `GET /health`.
