# Billing e contratos comerciais

Este guia descreve o contrato técnico atual de assinaturas, checkout, troca de plano, cancelamento, faturas e contratos comerciais. Billing pertence ao `CORE` como capacidade transversal, mas permanece desacoplado dos módulos operacionais. Alterações comerciais geram eventos e projeções; não autorizam funcionários internos a operar em nome do cliente.

## Permissões e isolamento

| Permissão | Escopo |
| --- | --- |
| `subscriptions.read` | Consulta assinatura somente quando o operador possui a permissão em todas as unidades vinculadas. |
| `subscriptions.manage` | Checkout, troca e cancelamento em todas as unidades envolvidas. |
| `billing.read` | Consulta de faturas e contratos visíveis ao cliente. |
| `billing.manage` | Alternativa financeira a `subscriptions.manage` para comandos da assinatura. |
| `billing.commercial.read` | Consulta administrativa de contratos e termos internos. |
| `billing.commercial.manage` | Criação, revisão e encerramento administrativo de contratos. |

O contexto da organização é derivado da unidade presente no token. Usuários internos da plataforma são recusados nos endpoints do cliente. Consultas que receberiam apenas parte de uma assinatura não retornam dados parciais: o operador precisa alcançar todas as unidades vinculadas.

## Estados

Assinaturas seguem `Draft → Active → CancellationPending → Canceled`. Uma assinatura em `Draft` representa checkout preparado, ainda sem ativação confirmada. `CancellationPending` mantém a vigência até a data efetiva. Troca de plano usa `PendingPlanId` até que o evento seja efetivado e projetado.

Faturas usam `Draft`, `Open`, `Paid` e `Voided`. O endpoint do cliente expõe identificador, assinatura, valor, moeda, ciclo, vencimento e estado; tentativas de cobrança, credenciais e dados brutos do provedor não fazem parte dessa resposta.

Contratos comerciais usam `Active` ou `Ended`. Cada alteração cria uma revisão imutável; o histórico anterior não é sobrescrito.

## Consultas do cliente

| Método e rota | Permissão | Resultado |
| --- | --- | --- |
| `GET /api/account/billing/subscriptions` | `subscriptions.read` | Lista assinaturas integralmente visíveis. |
| `GET /api/account/billing/subscriptions/{subscriptionId}` | `subscriptions.read` | Retorna assinatura da organização e do escopo; caso contrário, `404`. |
| `GET /api/account/billing/invoices` | `billing.read` | Lista faturas de assinaturas integralmente visíveis. |
| `GET /api/account/billing/contracts` | `billing.read` | Lista contratos e apenas termos visíveis ao cliente. |

A consulta de contratos do cliente remove `ActorUserId`, justificativas administrativas e termos com `VisibleToCustomer = false`.

## Checkout

`POST /api/account/billing/subscriptions/checkout` exige:

- plano existente;
- ao menos uma unidade, todas pertencentes à organização;
- `subscriptions.manage` ou `billing.manage` em todas as unidades;
- provedor registrado;
- URLs absolutas HTTPS para sucesso e cancelamento;
- chave de idempotência não vazia com até 120 caracteres.

A chave idempotente é escopada à organização. Repetir a mesma chave com plano, intervalo, provedor e unidades equivalentes retorna a assinatura já preparada. Reutilizá-la com dados diferentes retorna `409 IDEMPOTENCY_CONFLICT`. Uma unidade com assinatura efetiva não pode entrar em outra assinatura simultânea.

```json
{
  "planId": "ea225db2-b866-4800-b8ab-a9b103c01c42",
  "unitIds": ["92d3a3f9-dab1-452c-bd8c-116dadf83ff1"],
  "intervalUnit": 1,
  "intervalCount": 1,
  "providerCode": "provider-code",
  "successUrl": "https://app.example.com/billing/success",
  "cancelUrl": "https://app.example.com/billing/cancel",
  "idempotencyKey": "checkout-organization-period-001"
}
```

No contrato JSON atual, `BillingIntervalUnit` é numérico: `0` para dia, `1` para mês e `2` para ano. Consumidores devem gerar seus tipos a partir do OpenAPI para acompanhar uma eventual evolução desse formato.

O sucesso cria ou reutiliza uma assinatura `Draft` e retorna a URL do checkout. A ativação depende da confirmação persistida do provedor e do processamento dos eventos do Billing.

## Troca de plano e cancelamento

`POST /api/account/billing/subscriptions/{subscriptionId}/plan-change` registra `PlanId` e `EffectiveAtUtc`. Plano inexistente retorna `404`; transição incompatível com o estado atual retorna `409 PLAN_CHANGE_NOT_ALLOWED`. A atualização dos acessos ocorre pela projeção idempotente dos eventos, em contexto explícito de cada unidade.

`POST /api/account/billing/subscriptions/{subscriptionId}/cancellation` recebe `AtPeriodEnd`. Quando falso, solicita cancelamento imediato; quando verdadeiro, usa o fim do período vigente. Se houver assinatura externa, o provedor é chamado antes da mudança local. Indisponibilidade transitória retorna `503`, sem declarar sucesso antecipadamente.

## Administração de contratos comerciais

| Método e rota | Permissão | Efeito |
| --- | --- | --- |
| `GET /api/admin/billing/contracts` | `billing.commercial.read` | Lista contratos e termos internos, com filtro opcional por organização. |
| `GET /api/admin/billing/contracts/{contractId}/history` | `billing.commercial.read` | Retorna todas as revisões. |
| `POST /api/admin/billing/contracts` | `billing.commercial.read + billing.commercial.manage` | Cria contrato e primeira revisão. |
| `POST /api/admin/billing/contracts/{contractId}/revisions` | `billing.commercial.read + billing.commercial.manage` | Cria nova revisão sem apagar histórico. |
| `POST /api/admin/billing/contracts/{contractId}/end` | `billing.commercial.read + billing.commercial.manage` | Encerra a vigência sem excluir registros. |

Uma assinatura opcional só pode ser vinculada a contrato da mesma organização. Referências são únicas por organização. Termos são parametrizáveis para permitir negociação humana; o sistema não fixa descontos ou condições comerciais, mas valida e protege o conteúdo persistido contra credenciais e dados financeiros sensíveis.

## Processamento interno e consistência

- Eventos de assinatura são persistidos no outbox junto da alteração de domínio.
- Consumidores são idempotentes e projetam plano, módulos e entitlements no contexto explícito de cada unidade.
- Webhooks usam inbox persistente, lease e retomada de itens `Pending` para impedir efeitos concorrentes ou abandono após interrupção.
- Reconciliação consulta o provedor e converge divergências sem duplicar efeitos.
- Faturas, cobranças, tentativas e pagamentos mantêm transições auditáveis e chaves idempotentes.
- Payloads de webhook, termos, valores e parâmetros são validados para impedir persistência de tokens, credenciais e dados financeiros sensíveis, inclusive dentro de JSON.

Esses fluxos internos ainda não constituem endpoints públicos adicionais. A entrada HTTP de webhooks deve ser documentada quando um adaptador de provedor for exposto no host.

## Erros principais

- `400`: unidades ausentes/fora da organização, callbacks não HTTPS, intervalo ou chave idempotente inválidos.
- `403`: ausência de permissão em alguma unidade ou tentativa de uso por operador interno.
- `404`: assinatura, plano, organização ou contrato não encontrado no escopo.
- `409`: unidade já assinada, reutilização incompatível da chave ou transição de estado inválida.
- `503`: provedor não registrado ou temporariamente indisponível.

O contrato exato de payloads e enums está em `/openapi/v1.json`; a interface `/docs` é habilitada somente em desenvolvimento.
