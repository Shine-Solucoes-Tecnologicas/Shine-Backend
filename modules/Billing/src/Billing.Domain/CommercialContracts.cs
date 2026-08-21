using System.Text.Json;
using Shine.Domain;

namespace Billing.Domain;

public enum CommercialContractStatus { Active, Ended }

public sealed record CommercialTermDefinition(
    string Category,
    string Code,
    string ValueType,
    string Value,
    DateTime ValidFromUtc,
    DateTime? ValidUntilUtc = null,
    bool VisibleToCustomer = true,
    IReadOnlyDictionary<string, string>? Parameters = null);

public sealed class CommercialContract
{
    private readonly List<CommercialContractRevision> revisions = [];
    private CommercialContract() { }

    public CommercialContract(Guid accountId, Guid? subscriptionId, string reference, Guid actorUserId,
        string justification, DateTime validFromUtc, DateTime? validUntilUtc,
        IEnumerable<CommercialTermDefinition> terms, DateTime createdAtUtc)
    {
        if (accountId == Guid.Empty) throw new DomainException("Account is required.");
        if (subscriptionId == Guid.Empty) throw new DomainException("Subscription is invalid.");
        if (actorUserId == Guid.Empty) throw new DomainException("Actor is required.");
        Id = Guid.NewGuid();
        AccountId = accountId;
        SubscriptionId = subscriptionId;
        Reference = Required(reference, "Contract reference is required.", 120);
        Status = CommercialContractStatus.Active;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = CreatedAtUtc;
        AddRevision(actorUserId, justification, validFromUtc, validUntilUtc, terms, Status, CreatedAtUtc);
    }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid? SubscriptionId { get; private set; }
    public string Reference { get; private set; } = null!;
    public CommercialContractStatus Status { get; private set; }
    public int CurrentRevisionNumber { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<CommercialContractRevision> Revisions => revisions.AsReadOnly();

    public void Amend(Guid actorUserId, string justification, DateTime validFromUtc, DateTime? validUntilUtc,
        IEnumerable<CommercialTermDefinition> terms, DateTime changedAtUtc)
    {
        if (Status == CommercialContractStatus.Ended) throw new DomainException("Ended contracts cannot be amended.");
        AddRevision(actorUserId, justification, validFromUtc, validUntilUtc, terms, Status, changedAtUtc);
    }

    public void End(Guid actorUserId, string justification, DateTime endedAtUtc, DateTime changedAtUtc)
    {
        if (Status == CommercialContractStatus.Ended) return;
        var current = revisions.Single(x => x.RevisionNumber == CurrentRevisionNumber);
        var definitions = current.Terms.Select(x => new CommercialTermDefinition(x.Category, x.Code, x.ValueType,
            x.Value, x.ValidFromUtc, x.ValidUntilUtc, x.VisibleToCustomer,
            JsonSerializer.Deserialize<Dictionary<string, string>>(x.ParametersJson) ?? []));
        AddRevision(actorUserId, justification, current.ValidFromUtc, RequireUtc(endedAtUtc, nameof(endedAtUtc)),
            definitions, CommercialContractStatus.Ended, changedAtUtc);
        Status = CommercialContractStatus.Ended;
    }

    private void AddRevision(Guid actorUserId, string justification, DateTime validFromUtc, DateTime? validUntilUtc,
        IEnumerable<CommercialTermDefinition> terms, CommercialContractStatus status, DateTime changedAtUtc)
    {
        if (actorUserId == Guid.Empty) throw new DomainException("Actor is required.");
        var from = RequireUtc(validFromUtc, nameof(validFromUtc));
        DateTime? until = validUntilUtc is null ? null : RequireUtc(validUntilUtc.Value, nameof(validUntilUtc));
        if (until <= from) throw new DomainException("Contract validity end must be after its start.");
        var materialized = terms?.ToArray() ?? throw new DomainException("Commercial terms are required.");
        if (materialized.Length == 0) throw new DomainException("At least one commercial term is required.");
        var duplicate = materialized.GroupBy(x => $"{x.Category.Trim()}:{x.Code.Trim()}", StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null) throw new DomainException("Commercial term codes must be unique within a category.");

        CurrentRevisionNumber++;
        revisions.Add(new CommercialContractRevision(Id, CurrentRevisionNumber, actorUserId,
            Required(justification, "A justification is required.", 1000), status, from, until, materialized,
            RequireUtc(changedAtUtc, nameof(changedAtUtc))));
        UpdatedAtUtc = changedAtUtc;
    }

    private static DateTime RequireUtc(DateTime value, string name) =>
        value.Kind == DateTimeKind.Utc ? value : throw new ArgumentException("Date must be UTC.", name);
    internal static string Required(string value, string message, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException(message);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new DomainException(message);
        return normalized;
    }
}

public sealed class CommercialContractRevision
{
    private readonly List<CommercialTerm> terms = [];
    private CommercialContractRevision() { }
    internal CommercialContractRevision(Guid contractId, int revisionNumber, Guid actorUserId, string justification,
        CommercialContractStatus status, DateTime validFromUtc, DateTime? validUntilUtc,
        IEnumerable<CommercialTermDefinition> definitions, DateTime createdAtUtc)
    {
        Id = Guid.NewGuid(); ContractId = contractId; RevisionNumber = revisionNumber; ActorUserId = actorUserId;
        Justification = justification; Status = status; ValidFromUtc = validFromUtc; ValidUntilUtc = validUntilUtc; CreatedAtUtc = createdAtUtc;
        foreach (var definition in definitions) terms.Add(new CommercialTerm(Id, definition));
    }
    public Guid Id { get; private set; }
    public Guid ContractId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Justification { get; private set; } = null!;
    public CommercialContractStatus Status { get; private set; }
    public DateTime ValidFromUtc { get; private set; }
    public DateTime? ValidUntilUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public CommercialContract Contract { get; private set; } = null!;
    public IReadOnlyCollection<CommercialTerm> Terms => terms.AsReadOnly();
}

public sealed class CommercialTerm
{
    private CommercialTerm() { }
    internal CommercialTerm(Guid revisionId, CommercialTermDefinition definition)
    {
        BillingSensitiveDataGuard.EnsureSafeNames(
            new[] { definition.Category, definition.Code, definition.ValueType }.Concat(definition.Parameters?.Keys ?? []));
        BillingSensitiveDataGuard.EnsureSafeValues([definition.Value]);
        if (definition.Parameters is not null) BillingSensitiveDataGuard.EnsureSafeEntries(definition.Parameters);
        var from = definition.ValidFromUtc.Kind == DateTimeKind.Utc ? definition.ValidFromUtc : throw new ArgumentException("Date must be UTC.");
        var until = definition.ValidUntilUtc is null || definition.ValidUntilUtc.Value.Kind == DateTimeKind.Utc
            ? definition.ValidUntilUtc : throw new ArgumentException("Date must be UTC.");
        if (until <= from) throw new DomainException("Term validity end must be after its start.");
        Id = Guid.NewGuid(); RevisionId = revisionId;
        Category = CommercialContract.Required(definition.Category, "Term category is required.", 80);
        Code = CommercialContract.Required(definition.Code, "Term code is required.", 120);
        ValueType = CommercialContract.Required(definition.ValueType, "Term value type is required.", 40);
        Value = CommercialContract.Required(definition.Value, "Term value is required.", 2000);
        ValidFromUtc = from; ValidUntilUtc = until; VisibleToCustomer = definition.VisibleToCustomer;
        ParametersJson = JsonSerializer.Serialize(definition.Parameters ?? new Dictionary<string, string>());
    }
    public Guid Id { get; private set; }
    public Guid RevisionId { get; private set; }
    public string Category { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public string ValueType { get; private set; } = null!;
    public string Value { get; private set; } = null!;
    public string ParametersJson { get; private set; } = null!;
    public DateTime ValidFromUtc { get; private set; }
    public DateTime? ValidUntilUtc { get; private set; }
    public bool VisibleToCustomer { get; private set; }
    public CommercialContractRevision Revision { get; private set; } = null!;
}
