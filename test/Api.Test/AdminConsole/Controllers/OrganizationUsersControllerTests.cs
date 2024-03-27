﻿using System.Security.Claims;
using Bit.Api.AdminConsole.Controllers;
using Bit.Api.AdminConsole.Models.Request.Organizations;
using Bit.Core;
using Bit.Core.AdminConsole.Entities;
using Bit.Core.AdminConsole.Enums;
using Bit.Core.AdminConsole.Models.Data.Organizations.Policies;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Context;
using Bit.Core.Entities;
using Bit.Core.Enums;
using Bit.Core.Models.Data;
using Bit.Core.Models.Data.Organizations;
using Bit.Core.Models.Data.Organizations.OrganizationUsers;
using Bit.Core.OrganizationFeatures.OrganizationUsers.Interfaces;
using Bit.Core.Repositories;
using Bit.Core.Services;
using Bit.Core.Utilities;
using Bit.Test.Common.AutoFixture;
using Bit.Test.Common.AutoFixture.Attributes;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;
using Xunit;

namespace Bit.Api.Test.AdminConsole.Controllers;

[ControllerCustomize(typeof(OrganizationUsersController))]
[SutProviderCustomize]
public class OrganizationUsersControllerTests
{
    [Theory]
    [BitAutoData]
    public async Task PutResetPasswordEnrollment_InivitedUser_AcceptsInvite(Guid orgId, Guid userId, OrganizationUserResetPasswordEnrollmentRequestModel model,
        User user, OrganizationUser orgUser, SutProvider<OrganizationUsersController> sutProvider)
    {
        orgUser.Status = Core.Enums.OrganizationUserStatusType.Invited;
        sutProvider.GetDependency<IUserService>().GetUserByPrincipalAsync(default).ReturnsForAnyArgs(user);
        sutProvider.GetDependency<IOrganizationUserRepository>().GetByOrganizationAsync(default, default).ReturnsForAnyArgs(orgUser);

        await sutProvider.Sut.PutResetPasswordEnrollment(orgId, userId, model);

        await sutProvider.GetDependency<IAcceptOrgUserCommand>().Received(1).AcceptOrgUserByOrgIdAsync(orgId, user, sutProvider.GetDependency<IUserService>());
    }

    [Theory]
    [BitAutoData]
    public async Task PutResetPasswordEnrollment_ConfirmedUser_AcceptsInvite(Guid orgId, Guid userId, OrganizationUserResetPasswordEnrollmentRequestModel model,
        User user, OrganizationUser orgUser, SutProvider<OrganizationUsersController> sutProvider)
    {
        orgUser.Status = Core.Enums.OrganizationUserStatusType.Confirmed;
        sutProvider.GetDependency<IUserService>().GetUserByPrincipalAsync(default).ReturnsForAnyArgs(user);
        sutProvider.GetDependency<IOrganizationUserRepository>().GetByOrganizationAsync(default, default).ReturnsForAnyArgs(orgUser);

        await sutProvider.Sut.PutResetPasswordEnrollment(orgId, userId, model);

        await sutProvider.GetDependency<IAcceptOrgUserCommand>().Received(0).AcceptOrgUserByOrgIdAsync(orgId, user, sutProvider.GetDependency<IUserService>());
    }

    [Theory]
    [BitAutoData]
    public async Task Accept_RequiresKnownUser(Guid orgId, Guid orgUserId, OrganizationUserAcceptRequestModel model,
        SutProvider<OrganizationUsersController> sutProvider)
    {
        sutProvider.GetDependency<IUserService>().GetUserByPrincipalAsync(default).ReturnsForAnyArgs((User)null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sutProvider.Sut.Accept(orgId, orgUserId, model));
    }

    [Theory]
    [BitAutoData]
    public async Task Accept_NoMasterPasswordReset(Guid orgId, Guid orgUserId,
        OrganizationUserAcceptRequestModel model, User user, SutProvider<OrganizationUsersController> sutProvider)
    {
        sutProvider.GetDependency<IUserService>().GetUserByPrincipalAsync(default).ReturnsForAnyArgs(user);

        await sutProvider.Sut.Accept(orgId, orgUserId, model);

        await sutProvider.GetDependency<IAcceptOrgUserCommand>().Received(1)
            .AcceptOrgUserByEmailTokenAsync(orgUserId, user, model.Token, sutProvider.GetDependency<IUserService>());
        await sutProvider.GetDependency<IOrganizationService>().DidNotReceiveWithAnyArgs()
            .UpdateUserResetPasswordEnrollmentAsync(default, default, default, default);
    }

    [Theory]
    [BitAutoData]
    public async Task Accept_RequireMasterPasswordReset(Guid orgId, Guid orgUserId,
        OrganizationUserAcceptRequestModel model, User user, SutProvider<OrganizationUsersController> sutProvider)
    {
        var policy = new Policy
        {
            Enabled = true,
            Data = CoreHelpers.ClassToJsonData(new ResetPasswordDataModel { AutoEnrollEnabled = true, }),
        };
        sutProvider.GetDependency<IUserService>().GetUserByPrincipalAsync(default).ReturnsForAnyArgs(user);
        sutProvider.GetDependency<IPolicyRepository>().GetByOrganizationIdTypeAsync(orgId,
            PolicyType.ResetPassword).Returns(policy);

        await sutProvider.Sut.Accept(orgId, orgUserId, model);

        await sutProvider.GetDependency<IAcceptOrgUserCommand>().Received(1)
            .AcceptOrgUserByEmailTokenAsync(orgUserId, user, model.Token, sutProvider.GetDependency<IUserService>());
        await sutProvider.GetDependency<IOrganizationService>().Received(1)
            .UpdateUserResetPasswordEnrollmentAsync(orgId, user.Id, model.ResetPasswordKey, user.Id);
    }

    [Theory]
    [BitAutoData]
    public async Task Put_Success(OrganizationUserUpdateRequestModel model,
        OrganizationUser organizationUser, OrganizationAbility organizationAbility,
        SutProvider<OrganizationUsersController> sutProvider, Guid savingUserId)
    {
        var orgId = organizationAbility.Id = organizationUser.OrganizationId;
        sutProvider.GetDependency<ICurrentContext>().ManageUsers(orgId).Returns(true);
        sutProvider.GetDependency<IOrganizationUserRepository>().GetByIdAsync(organizationUser.Id).Returns(organizationUser);
        sutProvider.GetDependency<IApplicationCacheService>().GetOrganizationAbilityAsync(orgId)
            .Returns(organizationAbility);
        sutProvider.GetDependency<IUserService>().GetProperUserId(Arg.Any<ClaimsPrincipal>()).Returns(savingUserId);

        var orgUserId = organizationUser.Id;
        var orgUserEmail = organizationUser.Email;

        await sutProvider.Sut.Put(orgId, organizationUser.Id, model);

        sutProvider.GetDependency<IOrganizationService>().Received(1).SaveUserAsync(Arg.Is<OrganizationUser>(ou =>
            ou.Type == model.Type &&
            ou.Permissions == CoreHelpers.ClassToJsonData(model.Permissions) &&
            ou.AccessSecretsManager == model.AccessSecretsManager &&
            ou.Id == orgUserId &&
            ou.Email == orgUserEmail),
            savingUserId,
            Arg.Is<List<CollectionAccessSelection>>(cas =>
                cas.All(c => model.Collections.Any(m => m.Id == c.Id))),
            model.Groups);
    }

    [Theory]
    [BitAutoData]
    public async Task Put_UpdateSelf_WithoutAllowAdminAccessToAllCollectionItems_DoesNotUpdateGroups(OrganizationUserUpdateRequestModel model,
        OrganizationUser organizationUser, OrganizationAbility organizationAbility,
        SutProvider<OrganizationUsersController> sutProvider, Guid savingUserId)
    {
        // Updating self
        organizationUser.UserId = savingUserId;
        organizationAbility.FlexibleCollections = true;
        organizationAbility.AllowAdminAccessToAllCollectionItems = false;
        sutProvider.GetDependency<IFeatureService>().IsEnabled(FeatureFlagKeys.FlexibleCollectionsV1).Returns(true);

        var orgId = organizationAbility.Id = organizationUser.OrganizationId;
        sutProvider.GetDependency<ICurrentContext>().ManageUsers(orgId).Returns(true);
        sutProvider.GetDependency<IOrganizationUserRepository>().GetByIdAsync(organizationUser.Id).Returns(organizationUser);
        sutProvider.GetDependency<IApplicationCacheService>().GetOrganizationAbilityAsync(orgId)
            .Returns(organizationAbility);
        sutProvider.GetDependency<IUserService>().GetProperUserId(Arg.Any<ClaimsPrincipal>()).Returns(savingUserId);

        var orgUserId = organizationUser.Id;
        var orgUserEmail = organizationUser.Email;

        await sutProvider.Sut.Put(orgId, organizationUser.Id, model);

        sutProvider.GetDependency<IOrganizationService>().Received(1).SaveUserAsync(Arg.Is<OrganizationUser>(ou =>
            ou.Type == model.Type &&
            ou.Permissions == CoreHelpers.ClassToJsonData(model.Permissions) &&
            ou.AccessSecretsManager == model.AccessSecretsManager &&
            ou.Id == orgUserId &&
            ou.Email == orgUserEmail),
            savingUserId,
            Arg.Is<List<CollectionAccessSelection>>(cas =>
                cas.All(c => model.Collections.Any(m => m.Id == c.Id))),
            null);
    }

    [Theory]
    [BitAutoData]
    public async Task Put_UpdateSelf_WithAllowAdminAccessToAllCollectionItems_DoesUpdateGroups(OrganizationUserUpdateRequestModel model,
        OrganizationUser organizationUser, OrganizationAbility organizationAbility,
        SutProvider<OrganizationUsersController> sutProvider, Guid savingUserId)
    {
        // Updating self
        organizationUser.UserId = savingUserId;
        organizationAbility.FlexibleCollections = true;
        organizationAbility.AllowAdminAccessToAllCollectionItems = true;
        sutProvider.GetDependency<IFeatureService>().IsEnabled(FeatureFlagKeys.FlexibleCollectionsV1).Returns(true);

        var orgId = organizationAbility.Id = organizationUser.OrganizationId;
        sutProvider.GetDependency<ICurrentContext>().ManageUsers(orgId).Returns(true);
        sutProvider.GetDependency<IOrganizationUserRepository>().GetByIdAsync(organizationUser.Id).Returns(organizationUser);
        sutProvider.GetDependency<IApplicationCacheService>().GetOrganizationAbilityAsync(orgId)
            .Returns(organizationAbility);
        sutProvider.GetDependency<IUserService>().GetProperUserId(Arg.Any<ClaimsPrincipal>()).Returns(savingUserId);

        var orgUserId = organizationUser.Id;
        var orgUserEmail = organizationUser.Email;

        await sutProvider.Sut.Put(orgId, organizationUser.Id, model);

        sutProvider.GetDependency<IOrganizationService>().Received(1).SaveUserAsync(Arg.Is<OrganizationUser>(ou =>
            ou.Type == model.Type &&
            ou.Permissions == CoreHelpers.ClassToJsonData(model.Permissions) &&
            ou.AccessSecretsManager == model.AccessSecretsManager &&
            ou.Id == orgUserId &&
            ou.Email == orgUserEmail),
            savingUserId,
            Arg.Is<List<CollectionAccessSelection>>(cas =>
                cas.All(c => model.Collections.Any(m => m.Id == c.Id))),
            model.Groups);
    }

    [Theory]
    [BitAutoData]
    public async Task Get_WithFlexibleCollections_ReturnsUsers(
        ICollection<OrganizationUserUserDetails> organizationUsers, OrganizationAbility organizationAbility,
        SutProvider<OrganizationUsersController> sutProvider)
    {
        Get_Setup(organizationAbility, organizationUsers, sutProvider);
        var response = await sutProvider.Sut.Get(organizationAbility.Id);

        Assert.True(response.Data.All(r => organizationUsers.Any(ou => ou.Id == r.Id)));
    }

    [Theory]
    [BitAutoData]
    public async Task Get_WithFlexibleCollections_HandlesNullPermissionsObject(
        ICollection<OrganizationUserUserDetails> organizationUsers, OrganizationAbility organizationAbility,
        SutProvider<OrganizationUsersController> sutProvider)
    {
        Get_Setup(organizationAbility, organizationUsers, sutProvider);
        organizationUsers.First().Permissions = "null";
        var response = await sutProvider.Sut.Get(organizationAbility.Id);

        Assert.True(response.Data.All(r => organizationUsers.Any(ou => ou.Id == r.Id)));
    }

    [Theory]
    [BitAutoData]
    public async Task Get_WithFlexibleCollections_SetsDeprecatedCustomPermissionstoFalse(
        ICollection<OrganizationUserUserDetails> organizationUsers, OrganizationAbility organizationAbility,
        SutProvider<OrganizationUsersController> sutProvider)
    {
        Get_Setup(organizationAbility, organizationUsers, sutProvider);

        var customUser = organizationUsers.First();
        customUser.Type = OrganizationUserType.Custom;
        customUser.Permissions = CoreHelpers.ClassToJsonData(new Permissions
        {
            AccessReports = true,
            EditAssignedCollections = true,
            DeleteAssignedCollections = true,
            AccessEventLogs = true
        });

        var response = await sutProvider.Sut.Get(organizationAbility.Id);

        var customUserResponse = response.Data.First(r => r.Id == organizationUsers.First().Id);
        Assert.Equal(OrganizationUserType.Custom, customUserResponse.Type);
        Assert.True(customUserResponse.Permissions.AccessReports);
        Assert.True(customUserResponse.Permissions.AccessEventLogs);
        Assert.False(customUserResponse.Permissions.EditAssignedCollections);
        Assert.False(customUserResponse.Permissions.DeleteAssignedCollections);
    }

    [Theory]
    [BitAutoData]
    public async Task Get_WithFlexibleCollections_DowngradesCustomUsersWithDeprecatedPermissions(
        ICollection<OrganizationUserUserDetails> organizationUsers, OrganizationAbility organizationAbility,
        SutProvider<OrganizationUsersController> sutProvider)
    {
        Get_Setup(organizationAbility, organizationUsers, sutProvider);

        var customUser = organizationUsers.First();
        customUser.Type = OrganizationUserType.Custom;
        customUser.Permissions = CoreHelpers.ClassToJsonData(new Permissions
        {
            EditAssignedCollections = true,
            DeleteAssignedCollections = true,
        });

        var response = await sutProvider.Sut.Get(organizationAbility.Id);

        var customUserResponse = response.Data.First(r => r.Id == organizationUsers.First().Id);
        Assert.Equal(OrganizationUserType.User, customUserResponse.Type);
        Assert.False(customUserResponse.Permissions.EditAssignedCollections);
        Assert.False(customUserResponse.Permissions.DeleteAssignedCollections);
    }

    private void Get_Setup(OrganizationAbility organizationAbility,
        ICollection<OrganizationUserUserDetails> organizationUsers,
        SutProvider<OrganizationUsersController> sutProvider)
    {
        organizationAbility.FlexibleCollections = true;
        foreach (var orgUser in organizationUsers)
        {
            orgUser.Permissions = null;
        }
        sutProvider.GetDependency<IApplicationCacheService>().GetOrganizationAbilityAsync(organizationAbility.Id)
            .Returns(organizationAbility);

        sutProvider.GetDependency<IAuthorizationService>().AuthorizeAsync(
            user: Arg.Any<ClaimsPrincipal>(),
            resource: Arg.Any<Object>(),
            requirements: Arg.Any<IEnumerable<IAuthorizationRequirement>>())
            .Returns(AuthorizationResult.Success());

        sutProvider.GetDependency<IOrganizationUserRepository>()
            .GetManyDetailsByOrganizationAsync(organizationAbility.Id, Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(organizationUsers);
    }
}
