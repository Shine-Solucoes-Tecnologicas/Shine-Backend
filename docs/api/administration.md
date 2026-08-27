# API de administração da plataforma

Este guia descreve o backoffice interno da Shine em `/api/admin`. Essas operações exigem permissões globais e não concedem ao operador acesso operacional às unidades dos clientes. Um administrador interno pode gerir a plataforma, contratos e suporte técnico autorizado, mas não agenda, remarca ou executa ações de negócio em nome do cliente.

O contrato exato de parâmetros, corpos e schemas permanece em `GET /openapi/v1.json`. A interface `/docs` permite explorar o contrato no ambiente de desenvolvimento.

## Autorização global

| Permissão | Uso |
| --- | --- |
| `admin.read` | Consultar unidades, usuários, planos, logs e indicadores administrativos. |
| `admin.manage` | Gerir papéis globais, suspender/reativar unidades, bloquear/desbloquear usuários e criar planos. |
| `admin.audit` | Consultar trilhas de auditoria globais e valores persistidos. |
| `billing.commercial.read` | Consultar contratos, revisões e termos comerciais internos. |
| `billing.commercial.manage` | Criar, revisar e encerrar contratos comerciais. |

Quando uma operação declara `admin.read + admin.manage`, ambas são avaliadas pelo pipeline. Papéis globais e permissões dentro de uma unidade são fronteiras independentes. Receber uma permissão global não cria vínculo com uma unidade nem permite acessar seus endpoints operacionais.

## Acesso administrativo

| Método e rota | Permissão | Resultado e efeito |
| --- | --- | --- |
| `GET /api/admin/access/roles` | `admin.manage` | Lista papéis globais e permissões. |
| `GET /api/admin/access/users/{userId}/roles` | `admin.manage` | Lista os papéis globais do usuário; retorna `404` se o usuário não existir. |
| `PUT /api/admin/access/users/{userId}/roles/{roleName}` | `admin.manage` | Atribui o papel de forma idempotente e pode ampliar capacidades administrativas. Não concede acesso operacional. |
| `DELETE /api/admin/access/users/{userId}/roles/{roleName}` | `admin.manage` | Remove a atribuição; retorna `404` quando ela não existe. |

## Unidades

| Método e rota | Permissão | Resultado e efeito |
| --- | --- | --- |
| `GET /api/admin/tenants` | `admin.read` | Lista unidades com paginação e filtros `search` e `active`. |
| `GET /api/admin/tenants/{tenantId}` | `admin.read` | Retorna estado e quantidade de usuários ativos, sem entrar no contexto operacional. |
| `PUT /api/admin/tenants/{tenantId}/suspend` | `admin.read + admin.manage` | Exige `reason`, suspende a unidade e revoga sessões vinculadas somente a ela. |
| `PUT /api/admin/tenants/{tenantId}/reactivate` | `admin.read + admin.manage` | Reativa a unidade, mas não restaura sessões revogadas. |

Exemplo de suspensão:

```http
PUT /api/admin/tenants/8e557e6f-5240-4a21-91ba-fd3af24aa8c1/suspend
Authorization: Bearer <token-administrativo>
Content-Type: application/json

{
  "reason": "Suspensão administrativa autorizada"
}
```

A suspensão não exclui dados. Sessões do mesmo usuário vinculadas a outras unidades não são revogadas por essa operação.

## Usuários da plataforma

| Método e rota | Permissão | Resultado e efeito |
| --- | --- | --- |
| `GET /api/admin/users` | `admin.read` | Lista usuários com paginação e filtros `search`, `active` e `tenantId`. |
| `GET /api/admin/users/{userId}` | `admin.read` | Retorna bloqueio, unidades ativas e papéis globais. |
| `PUT /api/admin/users/{userId}/block` | `admin.read + admin.manage` | Exige `reason`, bloqueia o usuário e revoga todas as suas sessões ativas. |
| `PUT /api/admin/users/{userId}/unblock` | `admin.read + admin.manage` | Remove o bloqueio; novas autenticações são permitidas, mas sessões revogadas não retornam. |

Bloquear um usuário é global e difere de remover seu acesso a uma única unidade. O endpoint não deve ser usado como substituto da gestão de papéis operacionais do cliente.

## Planos da plataforma

| Método e rota | Permissão | Resultado e efeito |
| --- | --- | --- |
| `GET /api/admin/plans` | `admin.read` | Lista planos, módulos adicionais e entitlements. |
| `POST /api/admin/plans` | `admin.read + admin.manage` | Cria plano parametrizado; não altera assinaturas já existentes. |

Na criação, `CORE` não pode ser informado como módulo adicional porque é a base comum. Módulos e chaves de entitlement precisam existir nos catálogos técnicos. Código duplicado retorna `409`; módulo ou entitlement desconhecido retorna `400`.

## Contratos comerciais

| Método e rota | Permissão | Resultado e efeito |
| --- | --- | --- |
| `GET /api/admin/billing/contracts` | `billing.commercial.read` | Lista contratos, revisões e termos internos; aceita filtro por `accountId`. |
| `GET /api/admin/billing/contracts/{contractId}/history` | `billing.commercial.read` | Retorna o histórico imutável completo do contrato. |
| `POST /api/admin/billing/contracts` | `billing.commercial.read + billing.commercial.manage` | Cria o contrato e sua primeira revisão; não executa cobrança ou troca de plano. |
| `POST /api/admin/billing/contracts/{contractId}/revisions` | `billing.commercial.read + billing.commercial.manage` | Cria nova revisão e preserva todo o histórico anterior. |
| `POST /api/admin/billing/contracts/{contractId}/end` | `billing.commercial.read + billing.commercial.manage` | Encerra a vigência sem excluir o histórico nem cancelar automaticamente a assinatura. |

A organização deve existir. Quando `subscriptionId` for informado, a assinatura também precisa existir e pertencer à mesma organização; divergências retornam `409`. Referências são únicas por organização. Termos e parâmetros são dados comerciais sensíveis e passam pelas proteções de persistência do Billing.

## Auditoria, logs e dashboard

| Método e rota | Permissão | Resultado e efeito |
| --- | --- | --- |
| `GET /api/admin/audit` | `admin.audit` | Pesquisa auditoria por entidade, ação, usuário, unidade e intervalo UTC. |
| `GET /api/admin/audit/{auditId}` | `admin.audit` | Retorna um evento e os valores anteriores/posteriores persistidos. |
| `GET /api/admin/operational-logs` | `admin.read` | Pesquisa logs por nível, categoria, correlação e intervalo UTC. |
| `GET /api/admin/dashboard/summary` | `admin.read` | Agrega unidades, usuários, auditoria e logs; aceita período máximo de 90 dias. |

Auditoria e logs podem conter informação administrativa sensível. O consumidor não deve repassar `OldValuesJson`, `NewValuesJson`, mensagens ou exceções a usuários operacionais. O dashboard limita eventos recentes e não substitui consultas paginadas para investigação.

## Respostas e segurança operacional

- `401`: token ausente, inválido, expirado ou revogado.
- `403`: falta de uma ou mais permissões globais exigidas.
- `400`: justificativa ausente, período inválido ou configuração de plano inválida.
- `404`: usuário, unidade, papel, atribuição, evento, organização, assinatura ou contrato não encontrado.
- `409`: identificador já existente ou assinatura vinculada a outra organização.

Operações administrativas devem produzir auditoria conforme a capacidade implementada pelo domínio. Erros não podem expor segredos, tokens, dados financeiros sensíveis ou detalhes internos de exceções. Todas as datas de filtro são tratadas como UTC no contrato.
