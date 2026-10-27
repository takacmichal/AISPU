using SolarOps.Api.Features.UserRoles;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Tests.Unit.UserRoles;

public sealed class SetUserRolesRequestTests
{
    [Fact]
    public void Valid_HasNoErrors()
    {
        var request = new SetUserRolesRequest { Roles = [Roles.Sales, Roles.Designer] };

        Assert.Empty(DataAnnotationsValidator.Validate(request));
    }

    [Fact]
    public void EmptyList_IsRejected()
    {
        var errors = DataAnnotationsValidator.Validate(new SetUserRolesRequest { Roles = [] });

        Assert.Contains(nameof(SetUserRolesRequest.Roles), errors.Keys);
    }

    [Fact]
    public void Null_IsRejected()
    {
        var errors = DataAnnotationsValidator.Validate(new SetUserRolesRequest { Roles = null! });

        Assert.Contains(nameof(SetUserRolesRequest.Roles), errors.Keys);
    }

    [Fact]
    public void TooManyItems_IsRejected()
    {
        var request = new SetUserRolesRequest { Roles = [.. Enumerable.Repeat("Sales", Roles.All.Count + 1)] };

        Assert.Contains(nameof(SetUserRolesRequest.Roles), DataAnnotationsValidator.Validate(request).Keys);
    }
}
