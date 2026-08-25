# Documentação da API

Esta pasta organiza a documentação de consumo da API Shine. O contrato OpenAPI gerado pela aplicação é a fonte oficial para rotas, parâmetros, corpos, schemas e códigos de resposta. Os documentos Markdown explicam convenções e fluxos que não devem ser repetidos em cada endpoint.

## Onde consultar

- Contrato JSON: `GET /openapi/v1.json`.
- Interface interativa em desenvolvimento: `/docs`.
- Política de compatibilidade e validação: [`../openapi.md`](../openapi.md).
- Convenções transversais: [`conventions.md`](conventions.md).
- Arquitetura modular: [`../architecture.md`](../architecture.md).
- Autorização administrativa: [`../administrative-authorization.md`](../administrative-authorization.md).
- Módulos, entitlements e limites: [`../entitlements.md`](../entitlements.md).

## Fontes de verdade

| Informação | Fonte oficial |
| --- | --- |
| Método, rota e parâmetros | Atributos dos controllers e OpenAPI gerado |
| Request e response payloads | Tipos .NET publicados no OpenAPI |
| Autenticação e autorização | Atributos/policies do endpoint e filtros OpenAPI |
| Erros funcionais | Contratos tipados e middleware HTTP |
| Fluxos de negócio | Guias do domínio em `docs/` |
| Decisões arquiteturais | Documentação técnica versionada e ADRs |
| Visão navegável | Portal técnico no Confluence, com links para estas fontes |

Não copie schemas manualmente para o Confluence. Exemplos podem ser publicados para explicar um fluxo, mas devem informar a versão do contrato e apontar para o OpenAPI.

## Inventário inicial

Em 25 de agosto de 2026, o host possui 28 controllers e 122 operações HTTP declaradas. A cobertura será acompanhada por domínio:

| Grupo | Controllers | Responsabilidade |
| --- | ---: | --- |
| Identidade e tenancy | 5 | Registro, login, sessões, seleção de tenant e usuários |
| Administração global | 7 | Acesso, tenants, usuários, planos, auditoria, logs e dashboard |
| Billing | 3 | Assinaturas, faturas e contratos comerciais |
| Business Catalog | 1 | Profissionais, serviços e vínculos |
| Scheduling | 2 | Agenda autenticada e agendamento público |
| Clientes e acesso da conta | 2 | Cadastro de clientes e papéis/escopos da organização |
| Plataforma e auxiliares | 8 | Dashboard, arquivos, notificações, módulos, planos, flags, settings e health |

## Critério de documentação de um endpoint

Um endpoint está documentado quando o contrato gerado informa corretamente:

- identificador e resumo da operação;
- método, rota, parâmetros e headers relevantes;
- autenticação, permissões e contexto de tenant;
- request body, obrigatoriedade, tipos, formatos e validações;
- response de sucesso e respostas de erro esperadas;
- paginação, idempotência, concorrência ou rate limit quando aplicáveis;
- exemplo seguro para operações ou payloads não triviais;
- compatibilidade ou versão da rota.

## Processo de mudança

1. Altere o controller e os tipos do contrato.
2. Atualize metadados OpenAPI, responses e exemplos afetados.
3. Gere/inspecione `/openapi/v1.json` e `/docs`.
4. Execute `OpenApiContractTests`.
5. Atualize o guia do domínio quando o significado ou fluxo mudar.
6. Trate remoções, renomes e novas obrigatoriedades como alterações incompatíveis.

