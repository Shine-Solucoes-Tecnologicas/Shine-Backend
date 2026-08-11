# Autorização administrativa

## Contextos

O Shine possui dois contextos de autorização independentes:

- **Global**: operações sobre a plataforma, como consultar empresas, gerenciar usuários globais e consultar auditoria.
- **Tenant**: operações restritas à empresa ativa, como usuários, configurações e recursos da empresa.

Um papel global não concede automaticamente permissões de tenant. O tenant ativo é obtido do contexto autenticado e qualquer troca precisa ser validada pelo servidor.

## Papéis globais do MVP

| Papel | Responsabilidade |
| --- | --- |
| `PlatformAdmin` | Acesso total às operações administrativas autorizadas. |
| `Support` | Consultas e ações operacionais explicitamente permitidas. |
| `Auditor` | Consulta de dados administrativos e auditoria, sem alterações. |

As permissões são granulares e ficam associadas aos papéis globais. Atualmente a seed mantém permissões como `admin.read`, `admin.manage` e `admin.audit`.

## Proteção de endpoints

Endpoints administrativos usam `RequiresGlobalPermission`, por exemplo:

```csharp
[RequiresGlobalPermission("admin.read")]
```

Endpoints de tenant usam `RequiresPermission` e exigem usuário, tenant e vínculo ativos. A ausência do contexto completo ou da permissão resulta em acesso negado.

## Isolamento

Consultas de entidades multiempresa usam filtros por tenant. O bypass só pode ser aberto explicitamente por um usuário com papel `admin` ou `system` e deve possuir escopo limitado. Operações administrativas globais não devem reutilizar implicitamente o contexto de tenant.

Tentativas de usar uma entidade de outro tenant são rejeitadas pelo `ShineDbContext` e por `TenantIsolationExtensions.EnsureTenant`.

## Auditoria

Alterações administrativas registram entidade, ação, usuário responsável, tenant quando aplicável, data UTC, correlação e valores antes/depois com dados sensíveis mascarados.

## Suspensão e bloqueio

Suspender uma empresa registra motivo, administrador e data, revoga suas sessões e impede login ou renovação de tokens vinculados ao tenant. O desbloqueio de usuário é independente da suspensão da empresa e possui seus próprios metadados de auditoria.
