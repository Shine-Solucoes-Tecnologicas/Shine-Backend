# Segurança de implantação

## Separação de ambientes

As credenciais presentes em `appsettings.Development.json`, `docker-compose.yml`, `.env.example` e no serviço PostgreSQL do CI são exclusivamente locais ou efêmeras. Elas são deliberadamente previsíveis para desenvolvimento e nunca podem ser promovidas para Production.

Em Production, banco de dados, RabbitMQ e chaves JWT devem ser injetados por um secret manager ou mecanismo equivalente da plataforma. Segredos não devem ser enviados como argumento de build, incorporados à imagem, gravados em arquivos versionados ou impressos em logs. A aplicação valida apenas nomes de campos e encerra a inicialização sem incluir os valores rejeitados na mensagem.

## Responsabilidades e rotação

| Credencial | Papel responsável | Rotação planejada | Resposta a incidente |
| --- | --- | --- | --- |
| Chaves JWT | Responsável técnico da plataforma | Key ring conforme janela descrita em `configuration.md`, no máximo a cada 90 dias | Adicionar nova chave, ativá-la, reduzir a janela se necessário e remover a comprometida |
| PostgreSQL | Responsável pela infraestrutura/dados | Conforme política do provedor, no máximo a cada 90 dias | Revogar usuário/senha, emitir credencial nova e auditar conexões |
| RabbitMQ | Responsável pela infraestrutura/mensageria | Conforme política do provedor, no máximo a cada 90 dias | Revogar credencial, emitir uma nova e revisar permissões do virtual host |

Toda exceção à política precisa registrar responsável, justificativa, escopo e data de expiração no item de operação correspondente.

## Checklist de Production

- `ASPNETCORE_ENVIRONMENT=Production`.
- `ConnectionStrings__ShineDb` vem do secret manager e não usa credenciais locais conhecidas.
- `Jwt__ActiveKeyId` identifica uma entrada de `Jwt__SigningKeys`; `Jwt__Secret` não é usado.
- RabbitMQ, quando habilitado, possui usuário dedicado e senha não padrão.
- `PasswordRecovery__MockDelivery=false`.
- CORS contém apenas as origens HTTPS esperadas.
- TLS termina no proxy reverso ou load balancer público; a porta HTTP da API não é exposta diretamente à internet.
- `TrustedProxies__KnownProxies` e/ou `TrustedProxies__KnownNetworks` contêm somente os endereços internos efetivamente usados pela camada de proxy.
- O proxy encaminha `X-Forwarded-For` e `X-Forwarded-Proto`, remove valores recebidos do cliente e testa a aplicação com `X-Forwarded-Proto=https`.
- Logs e artefatos de CI não contêm valores de configuração sensíveis.
- O responsável pela implantação confirmou plano e responsáveis de rotação.

O fail-fast da aplicação é uma última barreira contra configuração acidental; ele não substitui controles do secret manager, revisão de deploy e rotação operacional.

## TLS, proxy reverso e headers HTTP

Em Staging e Production, o proxy reverso ou load balancer é o único ponto público. Ele recebe HTTPS, gerencia o certificado e encaminha a requisição para a rede privada da API. HTTP público deve ser bloqueado ou redirecionado para HTTPS na borda. A aplicação também responde com `308` para HTTP em Production como defesa adicional, depois de processar forwarded headers de proxies explicitamente confiáveis.

O endpoint `/health` pode permanecer em HTTP somente na rede interna para probes do orquestrador. Ele não deve ser publicado pela borda. Development não habilita HSTS nem redirecionamento automático, evitando que o navegador persista uma política incompatível com o ambiente local.

A aplicação é responsável por `Strict-Transport-Security` em respostas HTTPS de Production e pelos headers `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` e `Referrer-Policy: no-referrer` em todos os ambientes. CSP não é aplicada globalmente porque a API não serve uma interface HTML; uma política específica deve acompanhar qualquer UI que venha a ser hospedada pelo processo.

Configuração ilustrativa, substituindo os endereços pelos valores privados do provedor adotado:

```text
TrustedProxies__KnownProxies__0=10.0.0.10
TrustedProxies__KnownNetworks__0=10.20.0.0/16
```

Sem entradas configuradas, a aplicação confia em nenhum proxy. Portanto, um cliente externo não consegue forjar `X-Forwarded-Proto` para contornar HTTPS nem `X-Forwarded-For` para alterar rate limiting.
