# Business Catalog

O Business Catalog mantém profissionais, serviços e seus vínculos como cadastros independentes do Scheduling. A agenda consome essas referências por contratos de leitura, mas o catálogo não depende da agenda. Assim, profissionais e serviços podem ser reutilizados por módulos futuros sem carregar regras de agendamento.

## Rotas e compatibilidade

A base canônica é `/api/v1/business-catalog`. A base antiga `/api/business-catalog` continua disponível temporariamente para compatibilidade, mas todas as suas operações são marcadas como descontinuadas no OpenAPI e retornam:

```http
Deprecation: true
Link: </api/v1/business-catalog>; rel="successor-version"
```

Novos consumidores devem usar somente a rota versionada. As duas bases publicam atualmente as mesmas 15 operações e os mesmos payloads; mudanças incompatíveis futuras devem ocorrer em nova versão explícita.

## Autorização e isolamento

- Todas as operações exigem autenticação e contexto de unidade no token.
- Leitura exige `business-catalog.read`.
- Criação, alteração, ativação e vínculos exigem `business-catalog.manage`.
- Identificadores são sempre combinados com a unidade atual; um recurso de outra unidade é tratado como não encontrado.
- O Business Catalog integra o `CORE` e atualmente não possui limite de entitlement próprio. O uso posterior pelo Scheduling continua sujeito aos limites daquele módulo.

## Profissionais

| Método e rota relativa | Permissão | Efeito |
| --- | --- | --- |
| `GET /professionals` | `business-catalog.read` | Lista paginada, filtrável por `search` e `isActive`. |
| `GET /professionals/{professionalId}` | `business-catalog.read` | Consulta um profissional da unidade. |
| `POST /professionals` | `business-catalog.manage` | Cria profissional ativo. |
| `PUT /professionals/{professionalId}` | `business-catalog.manage` | Atualiza nome e usuário opcional. |
| `PUT /professionals/{professionalId}/activation` | `business-catalog.manage` | Ativa ou desativa explicitamente. |
| `DELETE /professionals/{professionalId}` | `business-catalog.manage` | Desativa de forma idempotente. |

O campo `userId` é opcional. Quando informado, precisa identificar um usuário ativo na mesma unidade; ele não transforma o profissional em papel de acesso e não concede permissões. Nomes são únicos por unidade. `DELETE` não remove fisicamente o cadastro, preservando referências históricas.

## Serviços

| Método e rota relativa | Permissão | Efeito |
| --- | --- | --- |
| `GET /services` | `business-catalog.read` | Lista paginada, filtrável por `search` e `isActive`. |
| `GET /services/{serviceId}` | `business-catalog.read` | Consulta um serviço da unidade. |
| `POST /services` | `business-catalog.manage` | Cria serviço ativo com duração em minutos. |
| `PUT /services/{serviceId}` | `business-catalog.manage` | Atualiza nome e duração. |
| `PUT /services/{serviceId}/activation` | `business-catalog.manage` | Ativa ou desativa explicitamente. |
| `DELETE /services/{serviceId}` | `business-catalog.manage` | Desativa de forma idempotente. |

A duração é expressa em minutos e deve ser positiva. Nomes são únicos por unidade. Desativação impede novos usos operacionais, mas não apaga o serviço nem invalida registros históricos de outros módulos.

## Vínculos profissional–serviço

| Método e rota relativa | Permissão | Efeito |
| --- | --- | --- |
| `GET /professionals/{professionalId}/services` | `business-catalog.read` | Lista vínculos ativos e inativos. |
| `PUT /professionals/{professionalId}/services/{serviceId}` | `business-catalog.manage` | Cria ou reativa o vínculo idempotentemente. |
| `DELETE /professionals/{professionalId}/services/{serviceId}` | `business-catalog.manage` | Desativa o vínculo idempotentemente. |

Profissional e serviço devem pertencer à mesma unidade do token. Repetir `PUT` não duplica o vínculo, inclusive sob concorrência; repetir `DELETE` continua retornando sucesso sem criar novo efeito.

## Paginação e filtros

As listagens usam o envelope transversal `PagedResponse<T>`, com `page` e `pageSize`. A ordenação é determinística por nome. `search` remove espaços externos, aceita no máximo 160 caracteres e faz comparação sem distinção de maiúsculas/minúsculas. `isActive` permite consultar ativos, inativos ou ambos quando omitido.

## Erros principais

- `400 INVALID_FILTER`: filtro de busca maior que 160 caracteres.
- `400 USER_NOT_AVAILABLE_FOR_UNIT`: usuário opcional ausente ou inativo na unidade.
- `400 INVALID_PROFESSIONAL` ou `INVALID_SERVICE`: nome ou duração inválida.
- `404 PROFESSIONAL_NOT_FOUND` ou `SERVICE_NOT_FOUND`: recurso inexistente no contexto autorizado.
- `409 PROFESSIONAL_ALREADY_EXISTS` ou `SERVICE_ALREADY_EXISTS`: nome duplicado na unidade.

`401` representa autenticação inválida e `403`, falta da permissão local. O contrato exato de schemas e parâmetros está em `/openapi/v1.json`; `/docs` fica disponível somente em desenvolvimento.
