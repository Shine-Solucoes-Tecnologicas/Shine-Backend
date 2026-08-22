using Billing.Domain;
using Shine.Domain;

namespace Shine.UnitTests;

public sealed class CommercialContractTests
{
    private static readonly DateTime Now = new(2026, 8, 17, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Amendments_create_immutable_auditable_revisions()
    {
        var actor = Guid.NewGuid();
        var contract = Contract(actor, Term("discount", "negotiated", "decimal", "10"));

        contract.Amend(actor, "Renegociação aprovada", Now.AddDays(30), null,
            [Term("discount", "negotiated", "decimal", "15")], Now.AddDays(1));

        Assert.Equal(2, contract.CurrentRevisionNumber);
        Assert.Equal("10", contract.Revisions.Single(x => x.RevisionNumber == 1).Terms.Single().Value);
        var current = contract.Revisions.Single(x => x.RevisionNumber == 2);
        Assert.Equal("15", current.Terms.Single().Value);
        Assert.Equal(actor, current.ActorUserId);
        Assert.Equal("Renegociação aprovada", current.Justification);
    }

    [Fact]
    public void Terms_are_parameterized_instead_of_using_fixed_plan_values()
    {
        var term = new CommercialTermDefinition("custom-condition", "grace-period", "integer", "45", Now,
            Parameters: new Dictionary<string, string> { ["unit"] = "days" });
        var contract = Contract(Guid.NewGuid(), term);

        var stored = contract.Revisions.Single().Terms.Single();
        Assert.Equal("custom-condition", stored.Category);
        Assert.Equal("grace-period", stored.Code);
        Assert.Contains("days", stored.ParametersJson);
    }

    [Fact]
    public void Ended_contract_keeps_history_and_cannot_be_amended()
    {
        var actor = Guid.NewGuid();
        var contract = Contract(actor, Term("condition", "payment-window", "integer", "10"));
        contract.End(actor, "Encerrado por solicitação comercial", Now.AddMonths(1), Now.AddDays(2));

        Assert.Equal(CommercialContractStatus.Ended, contract.Status);
        Assert.Equal(2, contract.Revisions.Count);
        Assert.Throws<DomainException>(() => contract.Amend(actor, "Tentativa inválida", Now, null,
            [Term("condition", "payment-window", "integer", "20")], Now.AddDays(3)));
    }

    [Fact]
    public void Revision_requires_a_justification_and_unique_term_codes()
    {
        var actor = Guid.NewGuid();
        Assert.Throws<DomainException>(() => Contract(actor, Term("condition", "same", "text", "A"), Term("CONDITION", "SAME", "text", "B")));
        Assert.Throws<DomainException>(() => new CommercialContract(Guid.NewGuid(), null, "REF", actor, " ", Now, null,
            [Term("condition", "one", "text", "A")], Now));
    }

    [Fact]
    public void Financial_history_rejects_payment_credential_fields()
    {
        var unsafeTerm = new CommercialTermDefinition("condition", "card-token", "text", "redacted", Now);

        Assert.Throws<DomainException>(() => Contract(Guid.NewGuid(), unsafeTerm));
    }

    [Fact]
    public void Financial_history_rejects_credentials_in_term_values_and_generic_parameters()
    {
        var unsafeValue = new CommercialTermDefinition("condition", "custom", "text", "4111111111111111", Now);
        var unsafeParameter = new CommercialTermDefinition("condition", "custom", "text", "approved", Now,
            Parameters: new Dictionary<string, string> { ["metadata"] = "{\"card_token\":\"tok_sensitive_value\"}" });

        Assert.Throws<DomainException>(() => Contract(Guid.NewGuid(), unsafeValue));
        Assert.Throws<DomainException>(() => Contract(Guid.NewGuid(), unsafeParameter));
    }

    private static CommercialContract Contract(Guid actor, params CommercialTermDefinition[] terms) =>
        new(Guid.NewGuid(), null, $"REF-{Guid.NewGuid():N}", actor, "Condição inicial aprovada", Now, null, terms, Now);

    private static CommercialTermDefinition Term(string category, string code, string type, string value) =>
        new(category, code, type, value, Now);
}
