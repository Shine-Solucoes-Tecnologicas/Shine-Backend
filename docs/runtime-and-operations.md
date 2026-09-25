# Runtime, integrações e operação

Este documento descreve o comportamento implementado do backend fora do contrato HTTP: módulos carregados pelo host, persistência, eventos, filas, workers, reconciliação, armazenamento e procedimentos operacionais. O código permanece como fonte oficial; mudanças nesses mecanismos devem atualizar este guia e seus testes de cobertura.

## Composição do processo

`host/Shine.Api` executa o monólito modular e compõe quatro áreas persistentes:

| Área | Contexto EF | Ownership principal | Migrations |
| --- | --- | --- | --- |
| Core | `ShineDbContext` | identidade, organizações, autorização, clientes, planos, entitlements, auditoria, notificações, layouts e metadados de arquivos | `platform/Core/src/Shine.Infrastructure/Persistence/Migrations` |
| Scheduling | `SchedulingDbContext` | disponibilidade, políticas de agenda, bloqueios, agendamentos e outbox de agenda | `modules/Scheduling/src/Scheduling.Infrastructure/Migrations` |
| Business Catalog | `BusinessCatalogDbContext` | profissionais, serviços e associações profissional-serviço | `modules/BusinessCatalog/src/BusinessCatalog.Infrastructure/Migrations` |
| Billing | `BillingDbContext` | contratos, assinaturas, ledger financeiro, inbox de webhooks e outbox de assinaturas | `modules/Billing/src/Billing.Infrastructure/Migrations` |

Os contextos usam a mesma connection string, mas nenhum módulo acessa diretamente o `DbContext` de outro módulo como atalho de domínio. Integrações usam contratos de Application, eventos ou serviços explícitos compostos pelo host. Uma transação EF não é uma transação distribuída entre contextos.

Business Catalog assumiu o ownership das tabelas `Professionals`, `Services` e `ProfessionalServices` sem recriá-las. As migrations históricas de Scheduling criam essas tabelas em banco vazio; por isso Scheduling deve ser migrado antes do baseline de Business Catalog.

## Migrations

A aplicação não chama `Database.Migrate` no startup. O deploy deve aplicar migrations como etapa explícita, na ordem abaixo:

```powershell
dotnet ef database update --project platform/Core/src/Shine.Infrastructure --startup-project host/Shine.Api --context ShineDbContext
dotnet ef database update --project modules/Scheduling/src/Scheduling.Infrastructure --startup-project host/Shine.Api --context SchedulingDbContext
dotnet ef database update --project modules/BusinessCatalog/src/BusinessCatalog.Infrastructure --startup-project host/Shine.Api --context BusinessCatalogDbContext
dotnet ef database update --project modules/Billing/src/Billing.Infrastructure --startup-project host/Shine.Api --context BillingDbContext
```

Antes de aplicar em ambiente compartilhado:

1. gere o script idempotente do contexto afetado com `dotnet ef migrations script --idempotent`;
2. revise operações destrutivas, locks e movimentação de ownership;
3. confirme backup/restauração compatíveis com o risco;
4. aplique uma única vez pelo pipeline de deploy;
5. valide `GET /health` e um smoke test do domínio alterado.

Não edite migrations já aplicadas. Uma correção deve ser uma nova migration no projeto proprietário.

## Eventos e entrega assíncrona

### Billing outbox

Alterações de `Subscription` capturam eventos no mesmo `SaveChanges` que persiste o agregado. A tabela `BillingOutbox` armazena `SubscriptionActivated`, `SubscriptionPlanChangeRequested`, `SubscriptionPlanChanged`, `SubscriptionCancellationRequested` e `SubscriptionCanceled`.

O `BillingOutboxProcessor`:

- reivindica até 50 mensagens por ciclo com `FOR UPDATE SKIP LOCKED`;
- usa lease de 5 minutos e `ProcessingId` para impedir conclusão por worker que perdeu o claim;
- recupera itens `Processing` com lease vencido;
- reagenda falhas transitórias para 1 minuto;
- marca falhas permanentes como `Failed`;
- projeta ativação, troca e cancelamento nos entitlements dentro de contexto explícito de cada unidade;
- registra eventos processados para tornar consumidores idempotentes.

### Billing webhook inbox

O adaptador do provedor valida e normaliza o webhook antes da persistência. O corpo bruto é representado apenas por SHA-256; os dados normalizados passam pelo guard contra credenciais financeiras. A unicidade `(ProviderCode, ExternalEventId)` rejeita duplicatas.

O processamento usa advisory lock, `ProcessingId` e lease de 5 minutos. Eventos recém-persistidos são processados imediatamente; o worker recupera `Pending` com mais de 1 minuto, retries vencidos e claims expirados. Falha transitória inicial agenda 1 minuto e retries posteriores, 5 minutos. Falha permanente fica em `Failed`.

### Scheduling outbox e RabbitMQ

Eventos de agendamento e intenções de lembrete são gravados em `OutboxMessages`. Chaves únicas de idempotência evitam produzir duas vezes a mesma intenção lógica. Eventos operacionais usam envelope versionado com tenant, entidade e correlação, sem depender do canal externo.

Quando `RabbitMq:Enabled=true`, o `SchedulingOutboxWorker` publica lotes de até 50 mensagens em exchange `topic`, usando o tipo do evento como routing key. Mensagens são persistentes. O worker tenta adquirir a mensagem por 1 minuto e marca `ProcessedAtUtc` somente após publicação; falhas permanecem pendentes para nova tentativa. Com RabbitMQ desabilitado, o worker não consome a outbox.

O publisher atual garante persistência e reentrega, portanto consumidores devem ser idempotentes. RabbitMQ não substitui a outbox nem participa da transação PostgreSQL.

## Workers

| Worker | Frequência | Lote/lease | Função |
| --- | --- | --- | --- |
| `BillingMaintenanceWorker` | 15 segundos | até 50; lease de 5 minutos | processar Billing outbox e recuperar inbox de webhooks |
| `SchedulingOutboxWorker` | 5 segundos ativo; 30 segundos desabilitado | até 50; lock de 1 minuto | publicar a outbox de Scheduling no RabbitMQ |
| `AppointmentEntitlementReconciliationWorker` | 5 minutos | serializado por advisory lock | reconciliar reservas de `SCHEDULING.ACTIVE_APPOINTMENTS` com agendamentos ativos de cada unidade |
| `StoredFileDeletionWorker` | 1 minuto | até 50 | concluir exclusões pendentes entre metadados e storage físico |

Todos criam escopo DI por ciclo. Processamentos multiempresa entram em contexto explícito de tenant antes de consultar ou alterar dados filtrados. Exceções do ciclo são registradas e não encerram o processo; cancelamento do host encerra o loop cooperativamente.

## Reconciliação de entitlements

A reconciliação de agenda adquire advisory lock transacional global, enumera unidades no Core e, para cada uma, entra em `ITenantExecutionContext`. Agendamentos `Scheduled` ou `Confirmed` são a fonte dos identificadores ativos. O guard substitui o conjunto de reservas correlacionadas, corrigindo resíduos de falhas parciais sem duplicar consumo.

Essa rotina é reparadora, não o caminho primário da operação. Erro recorrente deve ser investigado; reduzir o intervalo não corrige causa de inconsistência.

## Arquivos

Metadados pertencem ao Core e carregam tenant, proprietário, finalidade e permissões. O conteúdo é acessado por `IFileStorage`; a implementação atual, `LocalFileStorage`, grava um identificador opaco sem extensão no diretório configurado.

Defaults:

- raiz: `<diretório da aplicação>/storage`;
- imagem do container: `/var/lib/shine/storage` em volume;
- tamanho máximo: 10 MiB;
- extensões: PDF, PNG, JPG e JPEG;
- content type deve corresponder à extensão.

A exclusão HTTP apenas marca o metadado como `Pending`. O processor remove primeiro o objeto físico e depois o metadado, sob advisory lock por arquivo. Objeto já ausente é considerado efeito concluído. Falhas mantêm o metadado e usam backoff linear de 1 a 30 minutos para nova tentativa.

O volume de storage e o PostgreSQL precisam fazer parte da mesma estratégia de backup. Restaurar somente um deles pode produzir metadados órfãos ou objetos sem referência.

## Configuração essencial

| Chave | Uso | Produção |
| --- | --- | --- |
| `ConnectionStrings:ShineDb` | todos os contextos EF | obrigatória por secret/configuração externa |
| `RabbitMq:Enabled` | ativa publicação da outbox de Scheduling | habilitar somente com broker configurado |
| `RabbitMq:HostName`, `Port`, `UserName`, `Password`, `VirtualHost`, `ExchangeName` | conexão e exchange | credenciais fora do repositório |
| `FileStorage:RootPath` | conteúdo físico dos arquivos | volume persistente e protegido |
| `FileStorage:MaxFileSizeBytes`, `AllowedExtensions`, `AllowedContentTypes` | política técnica de upload | revisar em conjunto; não liberar apenas por MIME |

O Docker Compose publica PostgreSQL em `localhost:5433`, RabbitMQ em `5672` e a interface local do broker em `15672`. Valores do Compose são apenas desenvolvimento.

## Verificação operacional

Após iniciar ou implantar:

```powershell
docker compose up -d
docker compose ps
docker compose exec postgres pg_isready -U shine -d shine
curl.exe http://localhost:8080/health
```

`docker compose exec` não depende do nome gerado para o container. `/health` verifica PostgreSQL e RabbitMQ. `/api/health` oferece o contrato público reduzido e não expõe mensagens internas.

Consultas de diagnóstico devem ser somente leitura e evitar payloads potencialmente sensíveis:

```sql
SELECT "Status", COUNT(*) FROM "BillingOutbox" GROUP BY "Status";
SELECT "Status", COUNT(*) FROM "BillingProviderWebhookInbox" GROUP BY "Status";
SELECT COUNT(*) FROM "OutboxMessages" WHERE "ProcessedAtUtc" IS NULL;
SELECT "DeletionStatus", COUNT(*) FROM "StoredFiles" GROUP BY "DeletionStatus";
```

Sinais para investigação:

- crescimento contínuo de pendências;
- leases vencidos reaparecendo sem conclusão;
- itens `Failed` no Billing;
- repetição de `Billing maintenance cycle failed`, `Scheduling outbox batch failed`, `Appointment entitlement reconciliation failed` ou `Stored file deletion cycle failed`;
- health check degradado após migration ou rotação de configuração.

Não altere status, leases ou payloads diretamente para “destravar” uma fila. Preserve evidência, identifique a causa e use uma correção versionada ou uma operação administrativa auditável.
