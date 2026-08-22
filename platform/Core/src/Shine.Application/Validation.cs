using System.ComponentModel.DataAnnotations;

namespace Shine.Application;

public interface IValidationService
{
    IReadOnlyCollection<Error> Validate<T>(T instance);
}

public sealed class DataAnnotationsValidationService : IValidationService
{
    public IReadOnlyCollection<Error> Validate<T>(T instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance!, new ValidationContext(instance!), results, true);
        return results.Select(result => new Error("validation_error", result.ErrorMessage ?? "The supplied value is invalid.", "Validation")).ToArray();
    }
}
