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
- `EmailVerification__MockDelivery=false` e o adaptador do provedor transacional está registrado.
- CORS contém apenas as origens HTTPS esperadas.
- Logs e artefatos de CI não contêm valores de configuração sensíveis.
- O responsável pela implantação confirmou plano e responsáveis de rotação.

O fail-fast da aplicação é uma última barreira contra configuração acidental; ele não substitui controles do secret manager, revisão de deploy e rotação operacional.
