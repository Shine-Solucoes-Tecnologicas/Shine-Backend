# Contrato OpenAPI

A API publica seu contrato OpenAPI em `GET /openapi/v1.json`. O documento é gerado diretamente dos controllers e modelos da aplicação e não exige autenticação para ser consumido.

A interface interativa fica disponível em `/docs` somente no ambiente `Development`. Ela não é registrada em Staging ou Production.

## Autenticação e autorização

O esquema `Bearer` descreve o JWT enviado pelo cabeçalho `Authorization`. Operações com metadados de autorização herdam esse requisito e documentam respostas `401` e `403`. Operações explicitamente anônimas substituem o requisito global com uma lista de segurança vazia.

Os atributos de autorização do código continuam sendo a fonte de verdade. O documento não concede acesso e não substitui o pipeline HTTP.

## Compatibilidade

O documento `v1` aceita alterações aditivas compatíveis, como novas rotas, respostas opcionais e propriedades opcionais. As alterações abaixo são incompatíveis e exigem nova versão principal ou período de transição explícito:

- remover ou renomear rota, operação, parâmetro, propriedade ou código de resposta suportado;
- tornar obrigatório um campo anteriormente opcional;
- restringir valores, formatos ou tipos aceitos;
- alterar sem transição o significado de um campo;
- ampliar requisitos de autenticação ou autorização de uma operação existente.

A suíte de integração gera e interpreta o documento no pipeline, verifica rotas e schemas reutilizáveis e falha quando o contrato deixa de ser válido ou perde garantias estruturais. Mudanças do JSON devem ser tratadas como alterações de contrato durante a revisão do Pull Request.

## Validação

Execute:

```bash
dotnet test platform/Core/tests/Shine.Core.IntegrationTests/Shine.Core.IntegrationTests.csproj --filter OpenApiContractTests
```
