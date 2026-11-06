# Decisão técnica: sessão do cliente web

## Decisão atual

O cliente web usa o access token somente em memória e recebe o refresh token em cookie host-only. Em ambientes não locais, o cookie se chama `__Host-Shine.Refresh` e usa `HttpOnly`, `Secure`, `SameSite=Lax` e `Path=/`. O refresh token não faz parte dos corpos JSON de login, cadastro, renovação ou seleção de unidade.

O domínio de sessões e a persistência dos hashes não dependem desse transporte HTTP. Isso preserva a opção de introduzir um BFF para o site sem remodelar as entidades de sessão. Um aplicativo nativo futuro terá contrato próprio e armazenará a credencial no Keychain/Keystore; ele não deve copiar o contrato de cookies do navegador.

## Contrato do navegador

1. Login e cadastro devolvem o access token no JSON e gravam os cookies de refresh e anti-CSRF.
2. O frontend usa `credentials: "include"` e mantém o access token somente em memória.
3. `POST /api/auth/refresh`, `POST /api/auth/logout` e as operações de troca de unidade enviam o valor do cookie legível `XSRF-TOKEN` no header `X-XSRF-TOKEN`.
4. `GET /api/auth/csrf` reemite o token anti-CSRF depois de recarregar ou reabrir o navegador, sem criar uma sessão.
5. Refresh e troca de unidade revogam a sessão anterior e persistem a substituta na mesma transação; somente depois do commit a resposta substitui o cookie.
6. Logout revoga a sessão reconhecida e expira os cookies. Logout global, troca de senha e outros fluxos que revogam a sessão também removem o cookie local.

Uma requisição que usa cookie de refresh sem o par cookie/header anti-CSRF recebe `400`. Token ausente, expirado, revogado ou perdido em uma corrida recebe `401`.

## Recarregamento e múltiplas abas

Ao recarregar, a SPA chama `GET /api/auth/csrf` e depois `POST /api/auth/refresh`; não persiste o access token. Abas compartilham o cookie. O frontend deve eleger uma única renovação por vez, por exemplo com `BroadcastChannel` ou Web Locks, e distribuir somente o novo access token em memória. Se duas renovações consumirem a mesma sessão, o lock transacional permite uma única rotação; a outra recebe `401` e deve sincronizar com a aba vencedora ou exigir novo login.

Se a conexão cair depois do commit e antes do navegador receber o cookie substituto, o token anterior já estará revogado. A recuperação segura é um novo login; o backend não mantém uma janela de reutilização de refresh tokens.

## CORS, implantação e segredos

`Cors:AllowedOrigins` aceita somente origens explícitas e `Cors:AllowCredentials` deve permanecer habilitado. Wildcards não são aceitos. O proxy confiável deve preservar HTTPS para que cookies `Secure` funcionem corretamente.

O Data Protection key ring usado pelo antiforgery precisa ser persistente e compartilhado entre todas as instâncias da API. Rotação ou perda dessas chaves invalida tokens anti-CSRF, mas não deve expor nem registrar refresh tokens. Cookies, headers de autenticação, corpos de autenticação e URLs nunca devem ser copiados para logs ou telemetria.

`WebSession:AllowLegacyBodyTransport` existe exclusivamente para testes legados em Development, fica desabilitado por padrão e é rejeitado fora desse ambiente.

## Evolução para BFF

A migração para BFF será uma nova decisão arquitetural quando hospedagem do site, topologia de domínios e requisitos do aplicativo estiverem definidos. O BFF poderá concentrar access e refresh tokens e entregar ao navegador apenas um cookie de sessão opaco. Essa mudança altera a borda HTTP, não o modelo central de usuário, sessão, unidade ou rotação.
# Topologia obrigatória

Em produção, a sessão web usa cookies host-only, `Secure` e `SameSite=Lax`. Portanto, frontend e API devem estar sob o mesmo site registrável (por exemplo, `app.example.com` e `api.example.com`). Configure `WebSession:PublicApiOrigin` com a origem HTTPS pública da API e `WebSession:SiteDomain` com o domínio comum (`example.com`). Todas as origens de `Cors:AllowedOrigins` devem pertencer a esse site; a aplicação falha na inicialização quando a topologia é incompatível. Uma implantação cross-site exige a futura adoção do BFF ou uma revisão explícita da política de cookies.
