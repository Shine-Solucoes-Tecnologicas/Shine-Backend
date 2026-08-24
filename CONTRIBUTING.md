# Contribuindo com o Shine Backend

Este documento define o fluxo de versionamento e revisão do backend. Ele se aplica a alterações de código, infraestrutura, banco de dados e documentação.

## Branch principal

`main` representa a versão integrada e deve permanecer compilável. Alterações entram por Pull Request; não faça push direto nem merge local em `main`.

Enquanto a proteção de branch não estiver disponível para o repositório privado, essas regras são aplicadas pelo responsável pelo merge. A limitação e sua resolução são acompanhadas no Jira pela DEV-181.

## Criando uma branch

Comece sempre na versão mais recente de `main`:

```bash
git switch main
git pull --ff-only
git switch -c feature/DEV-123-descricao-curta
```

Use nomes curtos, em minúsculas e separados por hífen:

- `feature/DEV-123-descricao`: funcionalidade ou evolução planejada;
- `fix/DEV-123-descricao`: correção de defeito;
- `chore/DEV-123-descricao`: manutenção de dependências, CI ou ferramentas;
- `docs/DEV-123-descricao`: alteração exclusivamente documental.

Quando não existir item no Jira, omita a chave somente para manutenções pequenas e registre o motivo no Pull Request.

## Commits

Prefira commits pequenos e coerentes. Use o formato Conventional Commits:

```text
feat: add subscription cancellation policy
fix: prevent concurrent webhook delivery
docs: document repository contribution flow
```

Não inclua segredos, credenciais, dados pessoais reais ou valores sensíveis em código, fixtures, documentação, histórico Git ou mensagens de commit.

## Pull Requests

Todo Pull Request deve:

- ter `main` como destino e uma descrição do problema e da solução;
- referenciar o item correspondente do Jira;
- listar decisões, riscos, migrações e impactos de compatibilidade;
- informar os testes executados e seus resultados;
- manter o escopo pequeno o suficiente para revisão;
- atualizar documentação e contratos quando o comportamento público mudar;
- passar pelos checks aplicáveis antes do merge.

Pull Requests em rascunho podem ter checks incompletos. Um Pull Request pronto para revisão não pode ser mesclado com check obrigatório falhando ou sem resultado.

## Validação local

Antes de solicitar revisão, execute:

```bash
dotnet restore Shine.Backend.slnx
dotnet build Shine.Backend.slnx --no-restore --configuration Release
dotnet test Shine.Backend.slnx --no-build --configuration Release
docker build -t shine-backend:local .
```

Testes de integração precisam de PostgreSQL conforme a documentação do projeto. Se algum comando não se aplicar ou estiver bloqueado pelo ambiente, registre isso no Pull Request.

## Revisão e merge

- O autor resolve comentários e mantém a branch atualizada quando necessário.
- Quando houver outro mantenedor disponível, a aprovação deve vir de alguém diferente do autor.
- Enquanto houver um único mantenedor, o autor realiza e registra a auto-revisão usando o checklist do Pull Request.
- O responsável pelo merge confirma manualmente os checks enquanto a DEV-181 estiver pendente.
- Use o método de merge configurado no repositório e remova a branch após a integração.
- Não faça merge para contornar uma falha de CI. Exceções de segurança seguem a política em `docs/security-ci.md`.

## Banco de dados e segurança

Alterações de persistência devem incluir migration, atualização do snapshot aplicável e testes de integração. Mudanças de autorização, isolamento organizacional, Billing ou dados sensíveis precisam de testes que exerçam o pipeline real sempre que possível.

As políticas complementares estão em:

- `docs/architecture.md`;
- `docs/testing/integration-database-isolation.md`;
- `docs/deployment-security.md`;
- `docs/security-ci.md`.
