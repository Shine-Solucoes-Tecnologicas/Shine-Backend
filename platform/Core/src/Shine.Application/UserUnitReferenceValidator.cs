namespace Shine.Application;

public interface IUserUnitReferenceValidator
{
    Task<bool> IsActiveInUnitAsync(Guid unitId, Guid userId, CancellationToken cancellationToken = default);
}
