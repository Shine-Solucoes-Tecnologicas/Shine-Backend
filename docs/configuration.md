# Configuração

As configurações técnicas são carregadas por ambiente usando o sistema de configuração do ASP.NET Core.

## Obrigatória

`ConnectionStrings__ShineDb` deve ser fornecida por variável de ambiente ou por um provedor local de segredos. A aplicação falha na inicialização quando esse valor está ausente ou vazio.

Exemplo para desenvolvimento (não use uma senha real no repositório):

## Ambiente local com Docker

O PostgreSQL do projeto roda via Docker Compose na porta `5433` do Windows,
evitando conflito com uma instalação local na porta `5432`.

```powershell
./scripts/Local-Environment.ps1 -Action Prepare
./scripts/Local-Environment.ps1 -Action RunApi
```

`Prepare` verifica a disponibilidade do banco e aplica os quatro contextos na ordem documentada em [`runtime-and-operations.md`](runtime-and-operations.md). O script pode ser executado novamente com segurança; o EF aplica somente migrations pendentes. Use `-Action Check` para diagnosticar a dependência antes das suítes e `-Action Stop` para parar os containers sem remover dados.

O Compose local e o CI usam PostgreSQL 17. O `appsettings.Development.json` usa a porta local `5433`. Uma variável de ambiente continua tendo precedência quando for necessário apontar para outro banco.

O arquivo `.env.example` contém apenas nomes e valores ilustrativos. Segredos locais devem permanecer fora do controle de versão.

## Dataset de demonstração

`scripts/Demo-Data.ps1` prepara dados fictícios somente em `localhost/shine_demo`. A senha das contas é obrigatória como `SecureString` e chega ao processo pela variável temporária `DemoData__Password`; ela nunca possui default versionado. O comando interno também exige `ASPNETCORE_ENVIRONMENT=Development` e `DemoData__Enabled=true`.

O reset exige `-ConfirmReset shine_demo`, remove somente esse banco local e o recria por meio do bootstrap normal. Outros nomes de banco, hosts remotos e ambientes não Development são recusados antes do seed.

## Recuperação de senha em desenvolvimento

Quando `PasswordRecovery__MockDelivery=true`, a mensagem de recuperação é entregue somente ao `InMemoryPasswordRecoveryDelivery`. O conteúdo fica em memória no processo para testes e depuração local e nunca é escrito no pipeline de logs.

Os logs registram apenas que uma entrega foi preparada para o endereço informado. Token, URL e corpo da mensagem são sempre omitidos. O armazenamento em memória é descartado ao reiniciar a aplicação e não deve ser habilitado em produção.

## Rate limiting da autenticação

Login, cadastro, confirmação/reenvio de e-mail, refresh e recuperação de senha possuem limites de janela fixa independentes. Os valores são configurados em `AuthenticationRateLimiting` e podem ser substituídos por ambiente.

Em produção, `WebSession:PublicApiOrigin` deve conter a origem HTTPS pública da API e `WebSession:SiteDomain` o domínio registrável compartilhado com todas as origens de `Cors:AllowedOrigins`. Configurações cross-site falham na inicialização porque os cookies usam `SameSite=Lax`.

```text
AuthenticationRateLimiting__Login__PermitLimit=10
AuthenticationRateLimiting__Login__Window=00:01:00
```

A resposta ao exceder o limite usa HTTP `429`, código funcional `rate_limit_exceeded` e o header `Retry-After`. A partição usa o endereço remoto efetivamente observado pela aplicação. Headers encaminhados pelo cliente não são confiados; quando houver proxy reverso, a habilitação de forwarded headers deve listar explicitamente proxies ou redes confiáveis antes do rate limiter.

## CORS

As origens do frontend são configuradas em `Cors:AllowedOrigins`. Cada entrada deve ser uma origem HTTP(S) absoluta, sem caminho e sem wildcard. Produção falha na inicialização se não houver origem configurada ou se uma origem local/loopback for informada.

```text
Cors__AllowedOrigins__0=https://app.example.com
Cors__AllowCredentials=true
```

Localhost fica restrito ao arquivo de configuração de Development. Credenciais são necessárias para o cookie de sessão web e nunca podem ser combinadas com origem irrestrita. `WebSession__AllowLegacyBodyTransport` deve permanecer `false`; produção rejeita sua ativação.

O contrato completo do cookie, do header anti-CSRF e da futura evolução para BFF está em [`security/web-session.md`](security/web-session.md).

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

## Runtime, filas e armazenamento

As chaves `RabbitMq`, `FileStorage` e os requisitos operacionais dos workers estão descritos em [`runtime-and-operations.md`](runtime-and-operations.md). Credenciais do broker, connection strings e caminhos de storage específicos do ambiente devem ser fornecidos fora do repositório.
