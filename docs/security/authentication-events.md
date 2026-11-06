# Eventos de segurança da autenticação

Os fluxos de autenticação usam um envelope único e versionado para produzir evidência investigável sem armazenar credenciais ou dados pessoais completos. A infraestrutura recebe somente `AuthenticationSecurityEventInput`; comandos, entidades e payloads HTTP nunca são serializados automaticamente.

## Envelope

O schema v1 contém nome allowlisted, instante UTC, resultado, motivo categórico, correlation ID e os IDs internos conhecidos e autorizados. Identificadores desconhecidos e endereço de rede são opcionais e transformados por HMAC-SHA-256. Metadados do cliente são reduzidos a família, versão principal, sistema operacional e classe de dispositivo.

São proibidos nos dois destinos: senha ou hash de senha, token bruto ou hash reutilizável, secret, link/código de recuperação, e-mail completo, IP bruto, user-agent bruto, corpo de mensagem e campos livres fornecidos pelo cliente.

## Roteamento e consistência

Todo evento é enfileirado no log operacional estruturado. Login aceito, lockout, reuse de token, revogação, mudança/reset de senha, seleção de unidade investigável e comportamento suspeito consolidado também geram `AuditEntry`. Login negado isolado, refresh comum e solicitação de recuperação não inundam a auditoria.

O emissor adiciona ambos os registros ao `ShineDbContext` sem chamar `SaveChanges`. Assim, a instrumentação de uma mutação sensível persiste estado e evidência na mesma transação. O payload normalizado gravado nos dois destinos é idêntico.

## Configuração e rotação

`SecurityEvents:Fingerprinting` exige `ActiveKeyVersion` e uma lista `Keys` com versão única e segredo de pelo menos 32 caracteres. Em produção, os segredos não podem ser placeholders nem reutilizar chaves JWT.

Para rotacionar:

1. adicionar a nova chave mantendo a anterior durante a janela operacional;
2. alterar `ActiveKeyVersion` para a nova versão;
3. implantar e confirmar que novos eventos registram a nova versão;
4. retirar a chave anterior somente após o período de correlação e retenção aprovado.

A rotação muda intencionalmente os fingerprints. A versão registrada permite interpretar essa quebra de correlação sem revelar a chave ou o valor original.
