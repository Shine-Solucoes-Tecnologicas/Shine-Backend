# Configuração

As configurações técnicas são carregadas por ambiente usando o sistema de configuração do ASP.NET Core.

## Obrigatória

`ConnectionStrings__ShineDb` deve ser fornecida por variável de ambiente ou por um provedor local de segredos. A aplicação falha na inicialização quando esse valor está ausente ou vazio.

Exemplo para desenvolvimento (não use uma senha real no repositório):

## Ambiente local com Docker

O PostgreSQL do projeto roda via Docker Compose na porta `5433` do Windows,
evitando conflito com uma instalaÃ§Ã£o local na porta `5432`.

```powershell
docker compose up -d
docker compose ps
docker exec shine-backend-postgres-1 pg_isready -U shine -d shine
$env:ConnectionStrings__ShineDb = 'Host=localhost;Port=5433;Database=shine;Username=shine;Password=shine'
dotnet ef database update --project platform/Core/src/Shine.Infrastructure --startup-project host/Shine.Api
dotnet run --project host/Shine.Api
```

O arquivo `.env.example` contém apenas nomes e valores ilustrativos. Segredos locais devem permanecer fora do controle de versão.

## Recuperação de senha em desenvolvimento

Quando `PasswordRecovery__MockDelivery=true`, a mensagem de recuperação é entregue somente ao `InMemoryPasswordRecoveryDelivery`. O conteúdo fica em memória no processo para testes e depuração local e nunca é escrito no pipeline de logs.

Os logs registram apenas que uma entrega foi preparada para o endereço informado. Token, URL e corpo da mensagem são sempre omitidos. O armazenamento em memória é descartado ao reiniciar a aplicação e não deve ser habilitado em produção.

## Verificação de e-mail

O cadastro cria a conta do cliente e sua primeira unidade, mas responde com `202 Accepted` sem emitir access token ou refresh token. O usuário não consegue autenticar até consumir um desafio de verificação.

```text
EmailVerification__BaseUrl=https://app.example.com
EmailVerification__TokenLifetimeMinutes=30
EmailVerification__MockDelivery=false
```

Os endpoints públicos são:

* `POST /api/auth/email-verification/resend`, que sempre responde `202` para não revelar se o endereço está cadastrado;
* `POST /api/auth/email-verification/confirm`, que consome atomicamente um token de uso único.

Tokens brutos existem somente durante a composição da mensagem. O banco armazena SHA-256, e os logs registram apenas o identificador interno do usuário. Reenvio invalida desafios anteriores. Em Development, `MockDelivery=true` mantém a mensagem somente em memória; Production exige que o mock esteja desabilitado e que uma implementação externa de `IEmailVerificationDelivery` seja conectada ao provedor transacional escolhido.

Alterar o endereço pelo método de domínio remove imediatamente a confirmação. A exposição futura dessa operação por API também deverá revogar ou versionar access tokens já emitidos antes de liberar o endpoint.

## Rate limiting da autenticação

Login, cadastro, refresh e recuperação de senha possuem limites de janela fixa independentes. Os valores são configurados em `AuthenticationRateLimiting` e podem ser substituídos por ambiente, por exemplo:

```text
AuthenticationRateLimiting__Login__PermitLimit=10
AuthenticationRateLimiting__Login__Window=00:01:00
```

A resposta ao exceder o limite usa HTTP `429`, código funcional `rate_limit_exceeded` e o header `Retry-After`. A partição usa o endereço remoto efetivamente observado pela aplicação. Headers encaminhados pelo cliente não são confiados; quando houver proxy reverso, a habilitação de forwarded headers deve listar explicitamente proxies ou redes confiáveis antes do rate limiter.

## CORS

As origens do frontend são configuradas em `Cors:AllowedOrigins`. Cada entrada deve ser uma origem HTTP(S) absoluta, sem caminho e sem wildcard. Produção falha na inicialização se não houver origem configurada ou se uma origem local/loopback for informada.

```text
Cors__AllowedOrigins__0=https://app.example.com
Cors__AllowCredentials=false
```

Localhost fica restrito ao arquivo de configuração de Development. Credenciais permanecem desabilitadas por padrão e nunca podem ser combinadas com origem irrestrita.

## JWT e rotação de chaves

A API emite e valida access tokens com os componentes oficiais do ASP.NET Core e IdentityModel. Issuer, audience, expiração e tolerância de relógio são explícitos em `Jwt`. Somente HS256 é aceito.

Produção deve configurar um key ring e indicar a chave ativa:

```text
Jwt__Issuer=Shine
Jwt__Audience=Shine.Api
Jwt__ClockSkewSeconds=30
Jwt__ActiveKeyId=2026-08-primary
Jwt__SigningKeys__0__Id=2026-08-primary
Jwt__SigningKeys__0__Secret=<segredo com pelo menos 32 bytes>
```

Para rotacionar sem invalidar sessões imediatamente:

1. adicione a nova chave ao array `SigningKeys`, mantendo a anterior;
2. altere `ActiveKeyId` para a nova chave e publique a configuração;
3. aguarde pelo menos o maior tempo de vida de access token mais o `ClockSkewSeconds`;
4. remova a chave anterior e publique novamente.

Tokens novos carregam `kid` e são assinados somente pela chave ativa. A validação seleciona a chave correspondente e rejeita `kid` desconhecido. Tokens antigos sem `kid` podem ser validados contra o key ring durante a janela de migração. O campo legado `Jwt:Secret` continua aceito apenas para compatibilidade de desenvolvimento e gera `kid=legacy`; novos ambientes devem usar `SigningKeys`.
