namespace MemoryIndexer.Intelligence.Security.MultiTenant;

/// <summary>
/// Service for Role-Based Access Control (RBAC) authorization.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Checks if the current user has the specified permission.
    /// </summary>
    /// <param name="permission">The permission to check.</param>
    /// <returns>True if the user has the permission.</returns>
    bool HasPermission(Permission permission);

    /// <summary>
    /// Checks if the current user has any of the specified permissions.
    /// </summary>
    /// <param name="permissions">The permissions to check.</param>
    /// <returns>True if the user has any of the permissions.</returns>
    bool HasAnyPermission(params Permission[] permissions);

    /// <summary>
    /// Checks if the current user has all of the specified permissions.
    /// </summary>
    /// <param name="permissions">The permissions to check.</param>
    /// <returns>True if the user has all of the permissions.</returns>
    bool HasAllPermissions(params Permission[] permissions);

    /// <summary>
    /// Checks if the current user has the specified role.
    /// </summary>
    /// <param name="role">The role to check.</param>
    /// <returns>True if the user has the role.</returns>
    bool HasRole(string role);

    /// <summary>
    /// Authorizes access to a specific memory.
    /// </summary>
    /// <param name="memoryUserId">The user ID who owns the memory.</param>
    /// <param name="requiredPermission">The required permission.</param>
    /// <returns>Authorization result.</returns>
    AuthorizationResult AuthorizeMemoryAccess(string memoryUserId, Permission requiredPermission);

    /// <summary>
    /// Gets the permissions for a role.
    /// </summary>
    /// <param name="role">The role name.</param>
    /// <returns>Set of permissions for the role.</returns>
    IReadOnlySet<Permission> GetRolePermissions(string role);
}

/// <summary>
/// Permissions for memory operations.
/// </summary>
[Flags]
public enum Permission
{
    /// <summary>
    /// No permissions.
    /// </summary>
    None = 0,

    /// <summary>
    /// Can read own memories.
    /// </summary>
    ReadOwn = 1 << 0,

    /// <summary>
    /// Can write own memories.
    /// </summary>
    WriteOwn = 1 << 1,

    /// <summary>
    /// Can delete own memories.
    /// </summary>
    DeleteOwn = 1 << 2,

    /// <summary>
    /// Can read all memories in tenant.
    /// </summary>
    ReadAll = 1 << 3,

    /// <summary>
    /// Can write all memories in tenant.
    /// </summary>
    WriteAll = 1 << 4,

    /// <summary>
    /// Can delete all memories in tenant.
    /// </summary>
    DeleteAll = 1 << 5,

    /// <summary>
    /// Can manage users in tenant.
    /// </summary>
    ManageUsers = 1 << 6,

    /// <summary>
    /// Can view audit logs.
    /// </summary>
    ViewAuditLogs = 1 << 7,

    /// <summary>
    /// Can manage tenant settings.
    /// </summary>
    ManageTenant = 1 << 8,

    /// <summary>
    /// Can export data.
    /// </summary>
    ExportData = 1 << 9,

    /// <summary>
    /// Can import data.
    /// </summary>
    ImportData = 1 << 10,

    /// <summary>
    /// Can manage API keys.
    /// </summary>
    ManageApiKeys = 1 << 11,

    /// <summary>
    /// System administrator (cross-tenant).
    /// </summary>
    SystemAdmin = 1 << 30
}

/// <summary>
/// Result of an authorization check.
/// </summary>
public sealed class AuthorizationResult
{
    /// <summary>
    /// Whether the authorization was successful.
    /// </summary>
    public bool IsAuthorized { get; init; }

    /// <summary>
    /// Reason for denial (if not authorized).
    /// </summary>
    public string? DenialReason { get; init; }

    /// <summary>
    /// The permission that was checked.
    /// </summary>
    public Permission CheckedPermission { get; init; }

    /// <summary>
    /// Creates an authorized result.
    /// </summary>
    public static AuthorizationResult Authorized(Permission permission) =>
        new() { IsAuthorized = true, CheckedPermission = permission };

    /// <summary>
    /// Creates a denied result.
    /// </summary>
    public static AuthorizationResult Denied(Permission permission, string reason) =>
        new() { IsAuthorized = false, CheckedPermission = permission, DenialReason = reason };
}

/// <summary>
/// Built-in role definitions.
/// </summary>
public static class BuiltInRoles
{
    /// <summary>
    /// Regular user with access to own memories.
    /// </summary>
    public const string User = "User";

    /// <summary>
    /// Power user with extended capabilities.
    /// </summary>
    public const string PowerUser = "PowerUser";

    /// <summary>
    /// Tenant administrator.
    /// </summary>
    public const string TenantAdmin = "TenantAdmin";

    /// <summary>
    /// Read-only auditor.
    /// </summary>
    public const string Auditor = "Auditor";

    /// <summary>
    /// System administrator.
    /// </summary>
    public const string SystemAdmin = "SystemAdmin";

    /// <summary>
    /// Gets the default permissions for a role.
    /// </summary>
    public static Permission GetDefaultPermissions(string role) => role switch
    {
        User => Permission.ReadOwn | Permission.WriteOwn | Permission.DeleteOwn,
        PowerUser => Permission.ReadOwn | Permission.WriteOwn | Permission.DeleteOwn | Permission.ExportData | Permission.ImportData,
        TenantAdmin => Permission.ReadOwn | Permission.WriteOwn | Permission.DeleteOwn |
                       Permission.ReadAll | Permission.WriteAll | Permission.DeleteAll |
                       Permission.ManageUsers | Permission.ViewAuditLogs | Permission.ManageTenant |
                       Permission.ExportData | Permission.ImportData | Permission.ManageApiKeys,
        Auditor => Permission.ReadAll | Permission.ViewAuditLogs | Permission.ExportData,
        SystemAdmin => (Permission)int.MaxValue, // All permissions
        _ => Permission.ReadOwn
    };
}

/// <summary>
/// Default implementation of authorization service.
/// </summary>
public sealed class DefaultAuthorizationService : IAuthorizationService
{
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public DefaultAuthorizationService(ITenantContextAccessor tenantContextAccessor)
    {
        _tenantContextAccessor = tenantContextAccessor;
    }

    public bool HasPermission(Permission permission)
    {
        if (_tenantContextAccessor.Context?.Permissions is null)
        {
            return false;
        }

        // Check for system admin (has all permissions)
        if (_tenantContextAccessor.Context.Permissions.Contains(Permission.SystemAdmin))
        {
            return true;
        }

        return _tenantContextAccessor.Context.Permissions.Contains(permission);
    }

    public bool HasAnyPermission(params Permission[] permissions)
    {
        return permissions.Any(HasPermission);
    }

    public bool HasAllPermissions(params Permission[] permissions)
    {
        return permissions.All(HasPermission);
    }

    public bool HasRole(string role)
    {
        return _tenantContextAccessor.Context?.Roles.Contains(role) ?? false;
    }

    public AuthorizationResult AuthorizeMemoryAccess(string memoryUserId, Permission requiredPermission)
    {
        var currentUserId = _tenantContextAccessor.Context?.UserId;

        if (currentUserId is null)
        {
            return AuthorizationResult.Denied(requiredPermission, "No authenticated user");
        }

        // Check if accessing own memory
        var isOwnMemory = string.Equals(currentUserId, memoryUserId, StringComparison.OrdinalIgnoreCase);

        // Map required permission to "Own" or "All" variant
        var effectivePermission = requiredPermission;
        if (isOwnMemory)
        {
            effectivePermission = requiredPermission switch
            {
                Permission.ReadAll => Permission.ReadOwn,
                Permission.WriteAll => Permission.WriteOwn,
                Permission.DeleteAll => Permission.DeleteOwn,
                _ => requiredPermission
            };
        }

        if (HasPermission(effectivePermission))
        {
            return AuthorizationResult.Authorized(effectivePermission);
        }

        // Also check the "All" permission as fallback
        if (isOwnMemory)
        {
            var allPermission = requiredPermission switch
            {
                Permission.ReadOwn => Permission.ReadAll,
                Permission.WriteOwn => Permission.WriteAll,
                Permission.DeleteOwn => Permission.DeleteAll,
                _ => requiredPermission
            };

            if (HasPermission(allPermission))
            {
                return AuthorizationResult.Authorized(allPermission);
            }
        }

        return AuthorizationResult.Denied(
            requiredPermission,
            $"User '{currentUserId}' does not have permission '{requiredPermission}' for memory owned by '{memoryUserId}'");
    }

    public IReadOnlySet<Permission> GetRolePermissions(string role)
    {
        var permissions = BuiltInRoles.GetDefaultPermissions(role);
        var result = new HashSet<Permission>();

        // Decompose flags into individual permissions
        foreach (Permission p in Enum.GetValues<Permission>())
        {
            if (p != Permission.None && permissions.HasFlag(p))
            {
                result.Add(p);
            }
        }

        return result;
    }

}
