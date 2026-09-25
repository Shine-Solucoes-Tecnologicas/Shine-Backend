## Jira

- Item: DEV-000

## Contexto e solução

Descreva o problema, a solução adotada e as decisões relevantes.

## Impactos

- [ ] Contratos ou endpoints
- [ ] Banco de dados ou migrations
- [ ] Autorização ou isolamento organizacional
- [ ] Billing ou dados financeiros
- [ ] Configuração ou infraestrutura
- [ ] Sem impactos adicionais

Detalhes dos impactos e da compatibilidade:

## Validação

Comandos executados e resultados:

```text
dotnet build Shine.Backend.slnx --no-restore --configuration Release
dotnet test Shine.Backend.slnx --no-build --configuration Release
```

## Checklist de revisão

- [ ] O escopo corresponde ao item do Jira.
- [ ] Não há segredos, credenciais ou dados pessoais reais no diff ou histórico.
- [ ] Testes cobrem o comportamento alterado e falhas relevantes.
- [ ] Documentação e contratos foram atualizados quando necessário.
- [ ] Migrations e snapshots estão consistentes quando aplicável.
- [ ] Os checks aplicáveis concluíram com sucesso.
- [ ] Riscos, limitações ou validações não executadas estão registrados acima.
- [ ] A branch pode ser removida após o merge.
