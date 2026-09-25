# Identidade, organizações e acesso

Este guia cobre autenticação, sessões, organizações, unidades, usuários, papéis, módulos, planos e entitlements. No contrato e no código legado, alguns nomes ainda usam `tenant`; em mensagens destinadas ao cliente, prefira **unidade** ou **organização**, conforme o nível tratado.

## Fronteiras de acesso

- Identidade global: credenciais, senha e sessões pertencem ao usuário.
- Organização: reúne uma ou mais unidades e define papéis acumulativos com escopo de unidades e módulos.
- Unidade: contexto operacional presente no token e usado pelas permissões locais.
- Administração interna: usa permissões globais e nunca concede operação em nome do cliente.
- Plano, módulo e entitlement: controles distintos. O módulo `CORE` é comum; módulos adicionais vêm do plano ou de overrides, e entitlements limitam capacidades quantificáveis.

## Autenticação e sessões

| Operação | Acesso | Efeito principal |
| --- | --- | --- |
| `POST /api/auth/register` | Público | Cria identidade, sem conceder unidade. |
| `POST /api/auth/login` | Público | Autentica e cria sessão. |
| `POST /api/auth/refresh` | Refresh token ativo | Rotaciona o token; reutilização não cria outra sessão válida. |
| `POST /api/auth/logout` | Refresh token | Revoga a sessão correspondente. |
| `POST /api/auth/logout-all` | Autenticado | Revoga todas as sessões do usuário. |
| `GET /api/auth/sessions` | Autenticado | Lista somente as próprias sessões. |
| `GET /api/auth/sessions/current` | Autenticado | Retorna a sessão atual. |
| `DELETE /api/auth/sessions/{sessionId}` | Autenticado | Revoga somente uma sessão pertencente ao usuário. |
| `POST /api/auth/change-password` | Autenticado | Troca a senha e aplica a política de revogação. |
| `POST /api/auth/password/recovery` | Público | Retorna resposta neutra para evitar enumeração de contas. |
| `POST /api/auth/password/reset` | Token de recuperação | Consome o token e redefine a senha. |

Access tokens são enviados em `Authorization: Bearer <token>`. Refresh tokens são credenciais sensíveis, devem ser armazenados de forma protegida pelo cliente e nunca registrados em logs.

## Seleção e criação de unidade

`GET /api/tenants/accessible` retorna apenas vínculos e unidades ativos. `POST /api/tenants` cria uma unidade: quando não existe organização no contexto, também cria a organização e seus papéis padrão; quando existe, inclui a unidade na mesma organização. O criador recebe o vínculo de proprietário e um token contextual.

`POST /api/tenants/select`, `POST /api/tenants/switch` e `POST /api/auth/select-tenant` exigem vínculo ativo. As duas rotas `select` e `switch` são aliases compatíveis. Quando um refresh token ativo é informado, ele é rotacionado e o novo token fica associado à unidade selecionada.

## Usuários e papéis da unidade

| Rotas | Permissão |
| --- | --- |
| `GET /api/tenants/users` e `GET /api/tenants/users/{userId}` | `users.read` |
| `POST`, `DELETE`, alteração de status e metadados em `/api/tenants/users` | `users.manage` |
| Leitura e substituição em `/api/tenants/users/{userId}/roles` | `roles.manage` |

Essas operações afetam somente o vínculo local. Desativar ou remover um usuário de uma unidade não bloqueia sua identidade global nem remove seus vínculos com outras unidades.

## Papéis acumulativos da organização

As rotas em `/api/account/access` exigem `account.access.manage`, avaliada no contexto da organização, unidade e módulo do operador. Um papel pode valer para todas ou para uma lista de unidades e, de forma independente, para todos ou para uma lista de módulos.

- O operador não pode conceder ou revogar escopo maior que o próprio.
- Escopo total e lista explícita não podem ser enviados juntos.
- Usuários internos com papel global não podem receber acesso operacional na organização.
- A organização deve preservar ao menos um administrador.
- Alterar papéis sincroniza os vínculos operacionais das unidades alcançadas.

## Módulos, planos e entitlements

`GET /api/modules` lista o catálogo técnico. `GET /api/plans/access/{moduleCode}` consulta o acesso efetivo no contexto da unidade. Os endpoints de escrita em `/api/modules/access` e `/api/plans` são mecanismos técnicos/legados protegidos por `modules.manage` ou `tenant.manage`; a gestão comercial de planos e contratos pertence aos endpoints administrativos e ao Billing.

Entitlements não são papéis. Eles representam limites parametrizáveis, como quantidade de agendamentos ativos, e são consumidos pelas operações reais de cada módulo. Ausência de configuração não equivale a acesso ilimitado: o contrato funcional deve tratar `NotConfigured`, `Unavailable` e `Exhausted` como estados não permitidos.

## Erros relevantes

- `401`: autenticação ou sessão inválida.
- `403`: ausência de vínculo, permissão ou escopo; inclui tentativa de escalada.
- `400`: payload, módulo ou combinação de escopo inválida.
- `404`: identidade, papel, atribuição ou recurso não encontrado no contexto.
- `409`: duplicidade, usuário inativo/interno, último administrador ou conflito de estado.

O documento OpenAPI em `/openapi/v1.json` contém schemas, parâmetros e metadados de acesso de cada operação. A interface `/docs` fica disponível apenas em desenvolvimento.
