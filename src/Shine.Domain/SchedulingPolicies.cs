namespace Shine.Domain;

public enum ConflictMode
{
    Allow,
    WarnAndConfirm,
    Block
}

public sealed class CapacityPolicy
{
    public CapacityPolicy(int maxConcurrent, ConflictMode conflictMode = ConflictMode.WarnAndConfirm) { MaxConcurrent = Validate(maxConcurrent); ConflictMode = conflictMode; }
    public int MaxConcurrent { get; }
    public ConflictMode ConflictMode { get; }

    public bool IsCapacityExceeded(int currentConcurrent) => currentConcurrent >= MaxConcurrent;
    public bool RequiresConfirmation(int currentConcurrent) => currentConcurrent > 0 && ConflictMode == ConflictMode.WarnAndConfirm;
    public bool Blocks(int currentConcurrent) => IsCapacityExceeded(currentConcurrent) || ConflictMode == ConflictMode.Block && currentConcurrent > 0;

    private static int Validate(int value) => value is > 0 and <= 100 ? value : throw new ArgumentOutOfRangeException(nameof(value));
}

public sealed class ConflictPolicy
{
    public ConflictPolicy(ConflictMode mode) => Mode = mode;
    public ConflictMode Mode { get; }
    public bool AllowsConflict(bool confirmed) => Mode == ConflictMode.Allow || Mode == ConflictMode.WarnAndConfirm && confirmed;
    public bool RequiresConfirmation(bool hasConflict) => hasConflict && Mode == ConflictMode.WarnAndConfirm;
    public bool Blocks(bool hasConflict) => hasConflict && Mode == ConflictMode.Block;
}
