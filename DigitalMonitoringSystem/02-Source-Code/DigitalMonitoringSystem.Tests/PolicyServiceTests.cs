using DigitalMonitoringSystem.Api;
using DigitalMonitoringSystem.Shared;

namespace DigitalMonitoringSystem.Tests;

public class PolicyServiceTests
{
    private static PolicyUpsertRequest Baseline() => new()
    {
        Name = "Standard Endpoint Baseline",
        Category = "Compliance",
        Scope = "All Windows devices",
        Status = "Active"
    };

    [Fact]
    public void Create_StoresThePolicy()
    {
        using var context = TestData.CreateInMemory();
        var service = new PolicyService(context);

        var (policy, errors) = service.Create(Baseline());

        Assert.Empty(errors);
        Assert.NotNull(policy);
        Assert.StartsWith("POL-", policy!.Id);
        Assert.Equal("Standard Endpoint Baseline", policy.Name);
        Assert.Equal(0, policy.AssignedDevices);
        Assert.NotEqual(string.Empty, policy.UpdatedAt);
    }

    [Theory]
    [InlineData("", "Compliance", "All devices", "Active", "name")]
    [InlineData("Baseline", "", "All devices", "Active", "category")]
    [InlineData("Baseline", "Compliance", "", "Active", "scope")]
    [InlineData("Baseline", "Compliance", "All devices", "Unknown", "status")]
    public void Create_RejectsInvalidPolicies(string name, string category, string scope, string status, string expectedField)
    {
        using var context = TestData.CreateInMemory();
        var service = new PolicyService(context);

        var (policy, errors) = service.Create(new PolicyUpsertRequest
        {
            Name = name,
            Category = category,
            Scope = scope,
            Status = status
        });

        Assert.Null(policy);
        Assert.Contains(errors, error => error.Field == expectedField);
    }

    [Fact]
    public void Update_ChangesAnExistingPolicy()
    {
        using var context = TestData.CreateInMemory();
        var service = new PolicyService(context);
        var (created, _) = service.Create(Baseline());

        var (updated, errors) = service.Update(created!.Id.ToLowerInvariant(), new PolicyUpsertRequest
        {
            Name = "Renamed baseline",
            Category = "Security",
            Scope = "Branch devices",
            Status = "Draft"
        });

        Assert.Empty(errors);
        Assert.Equal("Renamed baseline", updated!.Name);
        Assert.Equal("Security", updated.Category);
        Assert.Equal("Draft", updated.Status);
    }

    [Fact]
    public void Update_ReportsAnUnknownPolicyOnTheIdField()
    {
        using var context = TestData.CreateInMemory();
        var service = new PolicyService(context);

        var (policy, errors) = service.Update("POL-nope", Baseline());

        Assert.Null(policy);
        Assert.Contains(errors, error => error.Field == "id");
    }

    [Fact]
    public void Delete_RemovesThePolicyAndItsAssignments()
    {
        using var context = TestData.CreateInMemory();
        var service = new PolicyService(context);
        var (policy, _) = service.Create(Baseline());
        context.Devices.Add(TestData.Device());
        context.SaveChanges();

        service.Assign("DEV-001", policy!.Id, "admin");
        Assert.Single(service.GetAssignmentsForDevice("DEV-001"));

        Assert.True(service.Delete(policy.Id));

        Assert.Null(service.GetPolicyById(policy.Id));
        Assert.Empty(service.GetAssignmentsForDevice("DEV-001"));
        Assert.False(service.Delete(policy.Id));
    }

    [Fact]
    public void Assign_AppliesAPolicyToAManagedDevice()
    {
        using var context = TestData.CreateInMemory();
        var service = new PolicyService(context);
        var (policy, _) = service.Create(Baseline());
        context.Devices.Add(TestData.Device(isManaged: true));
        context.SaveChanges();

        var (assignment, message) = service.Assign("dev-001", policy!.Id, "admin");

        Assert.NotNull(assignment);
        Assert.Equal(PolicyService.StateApplied, assignment!.State);
        Assert.Equal("admin", assignment.EnforcedBy);
        Assert.Equal("Policy applied.", message);
        Assert.Equal(1, service.GetPolicyById(policy.Id)!.AssignedDevices);
    }

    [Fact]
    public void Assign_IsIdempotentAndKeepsASingleAssignment()
    {
        using var context = TestData.CreateInMemory();
        var service = new PolicyService(context);
        var (policy, _) = service.Create(Baseline());
        context.Devices.Add(TestData.Device());
        context.SaveChanges();

        var (first, _) = service.Assign("DEV-001", policy!.Id, "admin");
        var (second, _) = service.Assign("DEV-001", policy.Id, "supervisor");

        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal("supervisor", second.EnforcedBy);
        Assert.Single(service.GetAssignmentsForDevice("DEV-001"));
        Assert.Equal(1, service.GetPolicyById(policy.Id)!.AssignedDevices);
    }

    [Fact]
    public void Assign_RefusesAnUnmanagedDevice()
    {
        using var context = TestData.CreateInMemory();
        var service = new PolicyService(context);
        var (policy, _) = service.Create(Baseline());
        context.Devices.Add(TestData.Device(isManaged: false));
        context.SaveChanges();

        var (assignment, message) = service.Assign("DEV-001", policy!.Id, "admin");

        Assert.Null(assignment);
        Assert.Contains("unmanaged", message);
    }

    [Fact]
    public void Assign_ExplainsAnUnknownDeviceOrPolicy()
    {
        using var context = TestData.CreateInMemory();
        var service = new PolicyService(context);
        var (policy, _) = service.Create(Baseline());
        context.Devices.Add(TestData.Device());
        context.SaveChanges();

        var (missingDevice, deviceMessage) = service.Assign("DEV-404", policy!.Id, "admin");
        var (missingPolicy, policyMessage) = service.Assign("DEV-001", "POL-404", "admin");

        Assert.Null(missingDevice);
        Assert.Contains("was not found", deviceMessage);
        Assert.Null(missingPolicy);
        Assert.Contains("was not found", policyMessage);
    }

    [Fact]
    public void Assign_ActivatesADraftPolicy()
    {
        using var context = TestData.CreateInMemory();
        var service = new PolicyService(context);
        var (policy, _) = service.Create(new PolicyUpsertRequest
        {
            Name = "Draft baseline",
            Category = "Compliance",
            Scope = "Lab",
            Status = "Draft"
        });
        context.Devices.Add(TestData.Device());
        context.SaveChanges();

        service.Assign("DEV-001", policy!.Id, "admin");

        Assert.Equal("Active", service.GetPolicyById(policy.Id)!.Status);
    }

    [Fact]
    public void Unassign_RemovesTheAssignmentAndRecountsThePolicy()
    {
        using var context = TestData.CreateInMemory();
        var service = new PolicyService(context);
        var (policy, _) = service.Create(Baseline());
        context.Devices.AddRange(TestData.Device(id: "DEV-001", hostname: "node-01"), TestData.Device(id: "DEV-002", hostname: "node-02"));
        context.SaveChanges();

        service.Assign("DEV-001", policy!.Id, "admin");
        service.Assign("DEV-002", policy.Id, "admin");
        Assert.Equal(2, service.GetPolicyById(policy.Id)!.AssignedDevices);

        var (removed, message) = service.Unassign("DEV-001", policy.Id);

        Assert.NotNull(removed);
        Assert.Contains("removed from", message);
        Assert.Equal(1, service.GetPolicyById(policy.Id)!.AssignedDevices);
        Assert.Equal(policy.Id, Assert.Single(service.GetAssignmentsForDevice("DEV-002")).PolicyId);
    }

    [Fact]
    public void Unassign_ReportsWhenNothingWasAssigned()
    {
        using var context = TestData.CreateInMemory();
        var service = new PolicyService(context);
        var (policy, _) = service.Create(Baseline());
        context.Devices.Add(TestData.Device());
        context.SaveChanges();

        var (removed, message) = service.Unassign("DEV-001", policy!.Id);

        Assert.Null(removed);
        Assert.Contains("not assigned", message);
    }

    [Fact]
    public void GetAssignmentsForPolicy_ReturnsNothingForAnUnknownPolicy()
    {
        using var context = TestData.CreateInMemory();
        var service = new PolicyService(context);

        Assert.Empty(service.GetAssignmentsForPolicy("POL-404"));
    }
}
