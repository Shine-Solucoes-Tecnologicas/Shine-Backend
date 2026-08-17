using System.Reflection;
using Shine.Api;
using Shine.Api.Controllers;

namespace Shine.UnitTests;

public sealed class BillingAuthorizationContractTests
{
    [Fact]
    public void Customer_contract_endpoint_requires_financial_read_permission()
    {
        var attribute = typeof(CustomerCommercialContractsController).GetCustomAttribute<RequiresPermissionAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("billing.read", attribute.PermissionCode);
    }

    [Fact]
    public void Commercial_mutations_require_the_dedicated_global_permission()
    {
        var controller = typeof(AdministrativeCommercialContractsController);
        Assert.Equal("billing.commercial.read", controller.GetCustomAttribute<RequiresGlobalPermissionAttribute>()?.PermissionCode);

        foreach (var methodName in new[] { "Create", "Amend", "End" })
        {
            var method = controller.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(method);
            Assert.Equal("billing.commercial.manage", method.GetCustomAttribute<RequiresGlobalPermissionAttribute>()?.PermissionCode);
            Assert.Null(method.GetCustomAttribute<RequiresPermissionAttribute>());
        }
    }
}
