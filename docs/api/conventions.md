# Convenções HTTP da API Shine

Este documento define a base transversal da API. Detalhes específicos permanecem no contrato OpenAPI e nos guias de cada domínio.

## Formato e transporte

- JSON é o formato padrão para requests e responses estruturadas.
- Datas e instantes trafegam em ISO 8601; instantes persistidos e contratos com sufixo `Utc` representam UTC.
- Identificadores são UUIDs quando o schema OpenAPI declara `format: uuid`.
- HTTPS é obrigatório fora do desenvolvimento local.

## Autenticação

Endpoints protegidos recebem JWT pelo header:

```http
Authorization: Bearer <access-token>
```

O login pode retornar tenants acessíveis antes da seleção do contexto. Tokens vinculados a tenant não devem ser reutilizados como autorização global ou em outro tenant.

## Autorização e tenant

- Autenticação não implica permissão.
- Permissões, papéis, módulos e entitlements são avaliados separadamente.
- Acesso administrativo global não concede acesso operacional implícito ao tenant.
- Identificadores enviados pelo cliente não substituem o contexto autorizado da requisição.
- `401` indica autenticação ausente, inválida, expirada ou revogada.
- `403` indica identidade autenticada sem permissão, papel, módulo, entitlement ou escopo suficiente.

## Erros

O formato legado atual é:

```json
{
  "errors": [
    {
      "code": "validation_error",
      "message": "The supplied value is invalid."
    }
  ]
}
```

Cada endpoint deve documentar os códigos funcionais adicionais que pode retornar. A evolução para Problem Details deve preservar um período de compatibilidade explícito e não pode ocorrer silenciosamente.

## Códigos HTTP

- `200 OK`: consulta ou comando com representação de retorno.
- `201 Created`: recurso criado; deve informar a representação e, quando possível, sua localização.
- `202 Accepted`: operação aceita para processamento posterior.
- `204 No Content`: comando concluído sem payload.
- `400 Bad Request`: contrato ou regra de domínio inválida.
- `401 Unauthorized`: falha de autenticação.
- `403 Forbidden`: autorização insuficiente.
- `404 Not Found`: recurso não existe no escopo autorizado.
- `409 Conflict`: conflito de estado, duplicidade ou concorrência operacional.
- `412 Precondition Failed`: versão/precondição enviada ficou obsoleta quando o endpoint adotar esse contrato.
- `429 Too Many Requests`: limite de requisições excedido.
- `500 Internal Server Error`: falha inesperada sem exposição de detalhes internos.

## Paginação

Endpoints paginados devem publicar o schema de envelope, parâmetros, limites máximos e semântica de ordenação no OpenAPI. A ordenação precisa ser determinística para evitar itens repetidos ou omitidos entre páginas.

## Idempotência

Operações que aceitam repetição segura devem documentar a chave idempotente, o escopo, o prazo de retenção e o comportamento quando a mesma chave é reutilizada com conteúdo diferente. Repetir uma chave não pode duplicar efeitos.

## Concorrência

Recursos versionados devem declarar a precondição esperada no contrato. Conflitos não podem sobrescrever alterações silenciosamente. O payload de erro deve permitir atualizar o estado e tentar novamente sem expor dados internos.

## Rate limits

Endpoints limitados devem documentar o escopo da limitação e a resposta `429`. O agendamento público atualmente aplica limite por endereço remoto e tenant da rota.

## Compatibilidade

São consideradas incompatíveis, entre outras:

- remover ou renomear rota, parâmetro, propriedade ou valor suportado;
- tornar obrigatório um campo antes opcional;
- mudar tipo, formato ou significado sem transição;
- restringir autenticação/autorização de uma operação existente sem estratégia de migração.

