# Scheduling público e autenticado

O Scheduling administra disponibilidade, exceções, bloqueios, slots e agendamentos. Profissionais e serviços permanecem no Business Catalog: a agenda apenas consulta contratos de leitura e mantém configurações específicas de agendamento por identificador.

## Acesso e isolamento

As 17 operações em `/api/scheduling` exigem token no contexto da unidade e acesso ao módulo `SCHEDULING`. As concessões usam escopo `own` ou `all`: `own` força o profissional vinculado ao usuário no servidor; `all` alcança todos os profissionais, mas somente na unidade autorizada. Um recurso fora desse alcance retorna `404` sem confirmar sua existência. Usuário com escopo `own` sem vínculo profissional ativo recebe `403 PROFESSIONAL_CONTEXT_REQUIRED`.

| Papel padrão | Concessões de Scheduling |
| --- | --- |
| Profissional | `scheduling.read own`, `scheduling.manage own` |
| Recepção | `scheduling.read all`, `scheduling.manage all` |
| Gestor | `scheduling.read all`, `scheduling.manage all`, `scheduling.configure all` |

Leituras usam `scheduling.read`; operação cotidiana de agendamentos, exceções e bloqueios usa `scheduling.manage`; settings, capacidade e políticas de duração usam `scheduling.configure all`. Disponibilidade recorrente pode ser alterada pelo profissional em sua própria agenda ou por quem possui `configure all`. As permissões não se implicam: um papel `configure-only` não recebe leitura ou operação, e `manage all` não recebe configuração.

As duas operações em `/api/public/scheduling/{tenantId}` são anônimas. Elas retornam `404` quando o módulo não está disponível, evitando revelar configuração interna, e nunca retornam nomes ou contatos de outros agendamentos.

## Configuração de slots e capacidade

`GET|PUT /api/scheduling/settings` consulta ou altera:

- intervalo entre inícios de slots, de 1 a 120 minutos;
- buffers anteriores e posteriores não negativos;
- fuso horário válido, padrão `America/Sao_Paulo`;
- política de conflito `Allow`, `WarnAndConfirm` ou `Block`;
- capacidade simultânea padrão, de 1 a 100.

`PUT /professionals/{professionalId}/capacity` sobrescreve a capacidade padrão para um profissional. A capacidade é avaliada sob lock transacional por unidade e profissional para impedir que requisições concorrentes ultrapassem o limite.

## Duração do serviço

A duração base pertence ao serviço no Business Catalog. O Scheduling pode aplicar:

1. override específico do vínculo profissional–serviço;
2. política variável do serviço baseada em um atributo versionado;
3. duração fixa do catálogo, quando não existe override ou política.

`PUT /services/{serviceId}/duration-policy` recebe a política variável; corpo nulo restaura duração fixa. `PUT /professionals/{professionalId}/services/{serviceId}/duration-override` define ou remove o override. O vínculo no catálogo precisa estar ativo.

## Disponibilidade, exceções e bloqueios

| Operação relativa a `/api/scheduling` | Efeito |
| --- | --- |
| `GET|POST /professionals/{professionalId}/availability` | Lista ou cria regras semanais recorrentes. |
| `DELETE /availability/{ruleId}` | Desativa uma regra recorrente. |
| `GET|POST /professionals/{professionalId}/availability-exceptions` | Lista ou cria exceções por data. |
| `DELETE /availability-exceptions/{exceptionId}` | Remove uma exceção. |
| `POST /professionals/{professionalId}/blocks` | Bloqueia intervalo UTC com justificativa. |

Regras, exceções e bloqueios do mesmo tipo não podem se sobrepor. Exceção sem início e fim bloqueia a data inteira; quando um deles é informado, ambos são obrigatórios e devem formar um período válido.

## Cálculo de slots

`GET /api/scheduling/availability/slots` exige `professionalId`, `serviceId` e `date`. A resposta usa UTC e combina:

- fuso da unidade;
- duração do serviço;
- intervalo e buffers;
- regras semanais e exceções;
- bloqueios;
- agendamentos não cancelados;
- capacidade e política de conflito.

`GET /api/public/scheduling/{tenantId}/availability/slots` aplica o mesmo cálculo, aceita atributos para duração variável e inclui `durationMinutes` em cada slot.

## Agendamentos autenticados

`GET /api/scheduling/appointments` lista, com paginação, os itens que intersectam `[fromUtc, toUtc)`. A ordenação é por início. A resposta inclui `Version`, usada no reagendamento otimista.

`POST /api/scheduling/appointments` valida profissional e serviço ativos, vínculo entre ambos, cliente opcional da mesma unidade, duração, bloqueios, sobreposição, capacidade e entitlement. `AllowConflict` só confirma sobreposição quando a política é `WarnAndConfirm`; não ignora bloqueios nem capacidade máxima.

`PUT /api/scheduling/appointments/{appointmentId}/reschedule` exige `ExpectedVersion`. Versão obsoleta retorna `409 STALE_APPOINTMENT` com a versão atual. Agendamentos `Cancelled`, `Completed` ou `NoShow` não podem ser reagendados.

## Estados e entitlement

Estados: `Scheduled`, `Confirmed`, `Completed`, `Cancelled` e `NoShow`.

Transições válidas:

- `Scheduled → Confirmed | Cancelled | NoShow`;
- `Confirmed → Completed | Cancelled | NoShow`;
- `Cancelled → Scheduled`.

Somente `Scheduled` e `Confirmed` consomem o entitlement `APPOINTMENTS.MAX`. Criação e reativação reservam o entitlement; cancelamento, conclusão e ausência liberam a reserva. `NotConfigured`, `Unavailable` e `Exhausted` bloqueiam a operação com `409`, nunca são tratados como ilimitados. Falhas após reserva executam compensação, e a reconciliação automática corrige divergências persistidas.

## Agendamento público

`POST /api/public/scheduling/{tenantId}/appointments` exige:

- horários UTC e início futuro;
- nome do cliente com até 160 caracteres e contato com até 200;
- profissional, serviço e vínculo ativos;
- fim igual ao início mais a duração estimada;
- slot ainda disponível no momento da gravação;
- capacidade e entitlement disponíveis.

A operação revalida disponibilidade dentro da transação e usa lock por profissional. Conflitos retornam `APPOINTMENT_UNAVAILABLE`, `CAPACITY_EXCEEDED`, `APPOINTMENT_CONFLICT` ou um dos códigos de entitlement.

## Rate limit e proxies

Todo o controller público usa janela fixa de **30 requisições por minuto**, sem fila, particionada por **IP remoto efetivo + unidade da rota**. Ao exceder, retorna `429`.

Em produção, `TrustedProxies` deve conter apenas proxies e redes conhecidos. `X-Forwarded-For` só altera o IP efetivo quando veio de infraestrutura confiável; sem essa configuração, o endereço da conexão é usado. Nunca habilite confiança irrestrita em headers encaminhados.

## Eventos e efeitos assíncronos

Criação, reagendamento e mudança de estado persistem eventos no outbox junto da operação. Esses eventos alimentam logs operacionais, invalidação/criação de lembretes e integrações assíncronas de forma idempotente. A resposta HTTP confirma a persistência local, não a entrega final de notificações externas.

## Erros principais

- `400`: UTC, duração, atributos, período, capacidade ou configuração inválida.
- `403`: módulo, permissão ou escopo local insuficiente; `PROFESSIONAL_CONTEXT_REQUIRED` indica ausência de vínculo profissional ativo no fluxo `own`.
- `404`: recurso fora da unidade, cadastro inativo ou módulo público indisponível.
- `409`: sobreposição, bloqueio, capacidade, confirmação ausente, entitlement ou versão concorrente.
- `429`: limite do endpoint público excedido.

O contrato exato de parâmetros, schemas e enums está em `/openapi/v1.json`; `/docs` é habilitado somente em desenvolvimento.
