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
| `GET /api/auth/csrf` | Público | Emite o token anti-CSRF do navegador sem criar sessão. |
| `POST /api/auth/register` | Público | Provisiona identidade não verificada, organização, primeira unidade, papéis e plano padrão; responde `202` sem criar sessão. |
| `POST /api/auth/email-verification/resend` | Público | Resposta neutra para reenviar a confirmação quando aplicável. |
| `POST /api/auth/email-verification/confirm` | Público | Confirma o e-mail e cria a primeira sessão contextual em cookie HttpOnly. |
| `POST /api/auth/login` | Público | Autentica e cria sessão. |
| `POST /api/auth/refresh` | Cookie de refresh + anti-CSRF | Rotaciona o cookie; reutilização não cria outra sessão válida. |
| `POST /api/auth/logout` | Cookie de refresh + anti-CSRF | Revoga a sessão correspondente e remove os cookies. |
| `POST /api/auth/logout-all` | Autenticado | Revoga todas as sessões do usuário. |
| `GET /api/auth/sessions` | Autenticado | Lista somente as próprias sessões. |
| `GET /api/auth/sessions/current` | Autenticado | Retorna a sessão atual. |
| `DELETE /api/auth/sessions/{sessionId}` | Autenticado | Revoga somente uma sessão pertencente ao usuário. |
| `POST /api/auth/change-password` | Autenticado | Troca a senha e aplica a política de revogação. |
| `POST /api/auth/password/recovery` | Público | Retorna resposta neutra para evitar enumeração de contas. |
| `POST /api/auth/password/reset` | Token de recuperação | Consome o token e redefine a senha. |

Access tokens são enviados em `Authorization: Bearer <token>` e todo token novo contém a claim padrão `sid`, vinculada ao identificador da sessão que o originou. `GET /api/auth/sessions/current` resolve a sessão por usuário + `sid` e somente retorna uma sessão ativa pertencente ao usuário. Revogar outra sessão não muda essa identificação.

Tokens legados sem `sid` usam temporariamente a sessão ativa mais recente do mesmo usuário e unidade. Essa compatibilidade existe apenas para os tokens já emitidos e desaparece naturalmente após o tempo máximo de vida do access token mais a tolerância de relógio; nenhum token novo é emitido sem `sid`.

No cliente web, refresh tokens ficam exclusivamente no cookie `HttpOnly`; não são retornados em JSON nem acessíveis ao JavaScript. O access token fica somente em memória. O mecanismo de CSRF, recarregamento, múltiplas abas, logout e evolução para BFF está em [`../security/web-session.md`](../security/web-session.md).

## Seleção e criação de unidade

`GET /api/tenants/accessible` retorna apenas vínculos e unidades ativos. `POST /api/tenants` cria uma unidade: quando não existe organização no contexto, também cria a organização e seus papéis padrão; quando existe, inclui a unidade na mesma organização. O criador recebe o vínculo de proprietário e um token contextual.

`POST /api/auth/select-tenant` é a operação canônica. Ela exige vínculo ativo, cookie de refresh e proteção anti-CSRF; revoga a sessão anterior e associa o cookie substituto à unidade selecionada na mesma transação. `POST /api/tenants/select` e `POST /api/tenants/switch` usam exatamente o mesmo serviço por compatibilidade, estão marcadas como deprecated no OpenAPI e serão removidas depois da migração do cliente web.

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

Erros de autenticação usam sempre o envelope abaixo. O frontend deve tomar decisões pelo `code`, não pelo texto, que pode ser traduzido. `field` aparece apenas quando o erro pertence a uma propriedade.

```json
{
  "errors": [
    {
      "code": "password_policy_failed",
      "message": "A senha não atende aos requisitos.",
      "field": "newPassword"
    }
  ]
}
```

| Código | HTTP | Uso |
| --- | --- | --- |
| `validation_error` | 400 | JSON ou campo inválido. |
| `csrf_validation_failed` | 400 | Cookie/header anti-CSRF ausente ou divergente. |
| `password_policy_failed` | 400 | Um ou mais requisitos de senha não foram atendidos. |
| `password_reset_invalid` | 400 | Link de recuperação inválido, usado ou expirado. |
| `invalid_credentials` | 401 | Login, usuário inativo/bloqueado e senha incorreta usam o mesmo código público. |
| `session_invalid` | 401 | Sessão ausente, inválida, revogada ou expirada. |
| `unit_access_denied` | 403 | Usuário não possui acesso à unidade ou operação. |
| `session_not_found` | 404 | Sessão própria solicitada não existe. |
| `email_verification_invalid` | 400 | Link de confirmação inválido, expirado ou já consumido. |
| `organization_name_invalid` | 400 | Nome da organização inválido. |
| `unit_name_invalid` / `unit_name_conflict` | 400 / 409 | Nome da unidade inválido ou já utilizado. |
| `rate_limit_exceeded` | 429 | Limite temporário excedido; respeitar `Retry-After`. |

Recuperação de senha continua retornando a mesma resposta `202` para e-mail existente ou inexistente. Erros internos, credenciais, tokens e detalhes de bloqueio não são expostos.

O documento OpenAPI em `/openapi/v1.json` contém schemas, parâmetros e metadados de acesso de cada operação. A interface `/docs` fica disponível apenas em desenvolvimento.
