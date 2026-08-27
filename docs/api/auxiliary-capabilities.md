# Capacidades auxiliares

Este guia cobre clientes, arquivos, notificações, dashboard, configurações funcionais, Feature Flags e health checks. São capacidades transversais do backend, mas continuam isoladas por unidade e por permissões específicas.

## Clientes

A base versionada é `/api/v1/customers`.

| Método e rota | Permissão | Efeito |
| --- | --- | --- |
| `GET /api/v1/customers` | `customers.read` | Lista paginada com busca, estado e ordenação. |
| `POST /api/v1/customers` | `customers.manage` | Cria cliente ativo. |
| `GET /api/v1/customers/{customerId}` | `customers.read` | Consulta cliente da unidade. |
| `PUT /api/v1/customers/{customerId}` | `customers.manage` | Atualiza dados preservando identidade. |
| `DELETE /api/v1/customers/{customerId}` | `customers.manage` | Desativa sem apagar histórico. |
| `POST /api/v1/customers/{customerId}/reactivate` | `customers.manage` | Reativa quando não existe conflito. |
| `GET /api/v1/customers/{customerId}/history` | `customers.read + scheduling.read`, módulo `SCHEDULING` | Retorna histórico paginado de agendamentos. |

Nome é obrigatório e limitado a 200 caracteres; email, telefone e identificador fiscal são normalizados e validados. O histórico tem versão de contrato própria (`1.0`) e não atravessa unidades.

## Arquivos

As rotas em `/api/files` exigem o módulo `CORE`.

- `POST /api/files`, com `files.manage`, aceita `multipart/form-data` e limita a requisição a 10 MiB. Arquivo vazio ou inválido retorna `400`.
- `GET /api/files/{id}`, com `files.read`, suporta range e devolve o nome original e Content-Type persistido.
- `DELETE /api/files/{id}`, com `files.manage`, pode retornar `204` quando storage e metadados convergiram ou `202 deletion_pending` quando o worker precisa concluir/reconciliar a remoção.

Metadados são consultados pela combinação **id + unidade** antes da verificação de permissão. Uma referência conhecida de outra unidade retorna `404`, tanto para download quanto para exclusão. Se o arquivo físico for salvo e a persistência do metadado falhar, o upload executa compensação removendo o conteúdo.

## Notificações

As rotas em `/api/notifications` exigem `CORE` e contexto autenticado.

`GET` lista notificações destinadas ao usuário e notificações gerais da unidade. Para notificações gerais, a leitura é registrada por usuário; para notificações pessoais, o estado fica no próprio item. `POST`, protegido por `notifications.manage`, só aceita destinatário com vínculo ativo na mesma unidade.

`POST /{id}/read` retorna `404` quando a notificação não é visível ao usuário. `POST /read-all` é idempotente e não altera o estado de outros usuários.

## Dashboard

| Rota | Permissão | Semântica |
| --- | --- | --- |
| `GET /api/dashboard/widgets` | `dashboard.read` | Resolve widgets conforme módulos e permissões atuais. |
| `GET /api/dashboard/widgets/{widgetKey}/data` | `dashboard.read` | Busca dados somente de widget disponível e período válido. |
| `GET /api/dashboard/layout` | `dashboard.read` | Resolve layout salvo contra o catálogo disponível. |
| `PUT /api/dashboard/layout` | `dashboard.manage` | Valida e salva o layout do usuário. |

O layout não concede acesso ao widget. A cada leitura, widgets indisponíveis por módulo ou permissão são removidos/resolvidos de acordo com o catálogo vigente.

## Configurações funcionais e Feature Flags

`GET /api/settings/{key}` resolve uma configuração da unidade e retorna `404` quando não existe. `PUT`, protegido por `tenant.manage`, define um valor parametrizado.

`GET /api/feature-flags` retorna os valores efetivos da unidade; `GET /catalog` retorna apenas as chaves registradas. `PUT /{key}`, com `tenant.manage`, altera o valor da unidade. Feature Flag controla liberação técnica e não substitui plano, módulo, entitlement ou permissão.

As leituras são permitidas a usuários autenticados no contexto da unidade porque seus valores influenciam o comportamento do cliente. Escritas continuam restritas à administração do cliente.

## Health checks

`GET /api/health` é público e retorna JSON agregado:

```json
{
  "status": "healthy",
  "checks": {
    "postgresql": { "status": "healthy", "duration": "00:00:00.001", "error": null },
    "rabbitmq": { "status": "healthy", "duration": "00:00:00.002", "error": null }
  }
}
```

Quando uma dependência falha, a API retorna `503` e usa somente o marcador estável `dependency_unavailable`; mensagens internas de exceção não são expostas anonimamente. A rota `/health` também existe para integração direta com health checks do ASP.NET, mas não faz parte do documento OpenAPI dos controllers. Infraestrutura deve preferir a rota adequada ao formato esperado pelo orquestrador.

## Erros e segurança

- `400`: payload, filtro, período, destinatário, arquivo ou layout inválido.
- `401`: autenticação ou contexto incompleto.
- `403`: permissão, módulo ou recurso pessoal insuficiente.
- `404`: recurso ausente dentro do escopo autorizado; também protege referências de outras unidades.
- `409`: conflito de unicidade ou estado.
- `503`: dependência de infraestrutura indisponível.

O contrato exato está em `/openapi/v1.json`; `/docs` é habilitado somente em desenvolvimento.
