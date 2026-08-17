# Entitlements e limites operacionais

As chaves de limite aceitas pelo backend são registradas no catálogo técnico `IEntitlementKeyCatalog`. Valores comerciais continuam configuráveis por plano ou exceção administrativa; o catálogo define somente quais capacidades técnicas existem.

## Comportamento quando o limite não existe

`NotConfigured` é uma decisão **não permitida**. Uma operação protegida nunca deve prosseguir quando o plano e suas exceções não definem a chave correspondente.

Os endpoints devem tratar toda decisão com `Allowed = false` como bloqueio:

- `NotConfigured`: responde `409` com `ENTITLEMENT_LIMIT_NOT_CONFIGURED`;
- `Unavailable`: responde `409` com `ENTITLEMENT_LIMIT_UNAVAILABLE`;
- `Exhausted`: responde `409` com `ENTITLEMENT_LIMIT_EXHAUSTED`.

Limites `Unlimited` e reservas `Available` são permitidos. A criação de agendamentos internos e públicos consome `APPOINTMENTS.MAX`; a saída de um agendamento do estado ativo libera a reserva.
