using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using DigitalMonitoringSystem.Shared;

namespace DigitalMonitoringSystem.Api;

public sealed class PolicyService
{
    public const string StateApplied = "Applied";
    public const string StatePending = "Pending";
    public const string StateRejected = "Rejected";

    public static readonly string[] SupportedStatuses = { "Active", "Scheduled", "Draft", "Retired" };

    private readonly MdmDbContext _context;

    public PolicyService(MdmDbContext context)
    {
        _context = context;
    }

    public IReadOnlyList<PolicyDefinition> GetPolicies() => _context.Policies.AsNoTracking().ToList().AsReadOnly();

    public PolicyDefinition? GetPolicyById(string id) => Lookup(_context.Policies, id);

    public IReadOnlyList<AlertRuleValidationError> Validate(PolicyUpsertRequest request)
    {
        var errors = new List<AlertRuleValidationError>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add(new AlertRuleValidationError { Field = "name", Message = "Policy name is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Category))
        {
            errors.Add(new AlertRuleValidationError { Field = "category", Message = "Policy category is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Scope))
        {
            errors.Add(new AlertRuleValidationError { Field = "scope", Message = "Policy scope is required." });
        }

        if (!SupportedStatuses.Contains(request.Status, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(new AlertRuleValidationError
            {
                Field = "status",
                Message = $"Status must be one of {string.Join(", ", SupportedStatuses)}."
            });
        }

        return errors;
    }

    public (PolicyDefinition? Policy, IReadOnlyList<AlertRuleValidationError> Errors) Create(PolicyUpsertRequest request)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return (null, errors);
        }

        var policy = new PolicyDefinition
        {
            Id = $"POL-{Guid.NewGuid():N}".ToUpperInvariant().Substring(0, 13),
            AssignedDevices = 0,
            UpdatedAt = MetricsService.FormatTimestamp(DateTimeOffset.UtcNow)
        };

        ApplyRequest(policy, request);
        _context.Policies.Add(policy);
        _context.SaveChanges();

        return (policy, errors);
    }

    public (PolicyDefinition? Policy, IReadOnlyList<AlertRuleValidationError> Errors) Update(string id, PolicyUpsertRequest request)
    {
        var policy = GetPolicyById(id);
        if (policy is null)
        {
            return (null, NotFound(id));
        }

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return (null, errors);
        }

        ApplyRequest(policy, request);
        _context.SaveChanges();
        return (policy, errors);
    }

    /// <summary>
    /// Removes a policy along with its device assignments. Devices stay in the fleet.
    /// </summary>
    public bool Delete(string id)
    {
        var policy = GetPolicyById(id);
        if (policy is null)
        {
            return false;
        }

        var assignments = _context.PolicyAssignments.Where(assignment => assignment.PolicyId == policy.Id).ToList();
        _context.PolicyAssignments.RemoveRange(assignments);
        _context.Policies.Remove(policy);
        _context.SaveChanges();
        return true;
    }

    public IReadOnlyList<DevicePolicyAssignment> GetAssignmentsForDevice(string deviceId)
    {
        return _context.PolicyAssignments.AsNoTracking()
            .Where(assignment => assignment.DeviceId == deviceId)
            .OrderBy(assignment => assignment.PolicyId)
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<DevicePolicyAssignment> GetAssignmentsForPolicy(string policyId)
    {
        var policy = GetPolicyById(policyId);
        if (policy is null)
        {
            return Array.Empty<DevicePolicyAssignment>();
        }

        return _context.PolicyAssignments.AsNoTracking()
            .Where(assignment => assignment.PolicyId == policy.Id)
            .OrderBy(assignment => assignment.DeviceId)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Assigns a policy to a device. Returns the assignment plus a message describing why an
    /// unmanaged or unknown device was rejected.
    /// </summary>
    public (DevicePolicyAssignment? Assignment, string Message) Assign(string deviceId, string policyId, string enforcedBy)
    {
        var device = Lookup(_context.Devices, deviceId);
        var policy = GetPolicyById(policyId);

        if (device is null)
        {
            return (null, $"Device '{deviceId}' was not found.");
        }

        if (policy is null)
        {
            return (null, $"Policy '{policyId}' was not found.");
        }

        if (!device.IsManaged)
        {
            return (null, $"Device '{device.Hostname}' is unmanaged and cannot receive policies.");
        }

        var existing = _context.PolicyAssignments
            .FirstOrDefault(assignment => assignment.PolicyId == policy.Id && assignment.DeviceId == device.Id);

        if (existing is not null)
        {
            existing.State = StateApplied;
            existing.EnforcedBy = enforcedBy;
            existing.EnforcedAt = MetricsService.FormatTimestamp(DateTimeOffset.UtcNow);
            existing.Message = "Policy re-applied.";
            _context.SaveChanges();
            RecountAssignments(policy);
            return (existing, existing.Message);
        }

        var assignment = new DevicePolicyAssignment
        {
            Id = $"ASG-{Guid.NewGuid():N}".ToUpperInvariant().Substring(0, 12),
            DeviceId = device.Id,
            PolicyId = policy.Id,
            State = StateApplied,
            EnforcedBy = enforcedBy,
            EnforcedAt = MetricsService.FormatTimestamp(DateTimeOffset.UtcNow),
            Message = "Policy applied."
        };

        _context.PolicyAssignments.Add(assignment);
        policy.Status = "Active";
        policy.UpdatedAt = MetricsService.FormatTimestamp(DateTimeOffset.UtcNow);
        _context.SaveChanges();

        RecountAssignments(policy);
        return (assignment, assignment.Message);
    }

    public (DevicePolicyAssignment? Removed, string Message) Unassign(string deviceId, string policyId)
    {
        var device = Lookup(_context.Devices, deviceId);
        var policy = GetPolicyById(policyId);

        if (device is null)
        {
            return (null, $"Device '{deviceId}' was not found.");
        }

        if (policy is null)
        {
            return (null, $"Policy '{policyId}' was not found.");
        }

        var existing = _context.PolicyAssignments
            .FirstOrDefault(assignment => assignment.PolicyId == policy.Id && assignment.DeviceId == device.Id);

        if (existing is null)
        {
            return (null, $"Policy '{policy.Id}' is not assigned to '{device.Hostname}'.");
        }

        _context.PolicyAssignments.Remove(existing);
        _context.SaveChanges();

        RecountAssignments(policy);
        return (existing, $"Policy '{policy.Id}' removed from '{device.Hostname}'.");
    }

    private void RecountAssignments(PolicyDefinition policy)
    {
        policy.AssignedDevices = _context.PolicyAssignments
            .Count(assignment => assignment.PolicyId == policy.Id);
        _context.SaveChanges();
    }

    private static void ApplyRequest(PolicyDefinition policy, PolicyUpsertRequest request)
    {
        policy.Name = request.Name.Trim();
        policy.Category = request.Category.Trim();
        policy.Scope = request.Scope.Trim();
        policy.Status = string.IsNullOrWhiteSpace(request.Status) ? "Draft" : request.Status.Trim();
        policy.UpdatedAt = MetricsService.FormatTimestamp(DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<AlertRuleValidationError> NotFound(string id)
    {
        return new List<AlertRuleValidationError>
        {
            new() { Field = "id", Message = $"Policy '{id}' was not found." }
        };
    }

    private static TEntity? Lookup<TEntity>(DbSet<TEntity> set, string id) where TEntity : class
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var candidate = id.Trim();
        var normalized = candidate.ToLowerInvariant();

        return set.FirstOrDefault(entity => EF.Property<string>(entity, "Id") == candidate)
            ?? set.FirstOrDefault(entity => EF.Property<string>(entity, "Id").ToLower() == normalized);
    }
}
