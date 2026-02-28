using FabCopilot.Contracts.Models;

namespace FabCopilot.Contracts.Interfaces;

/// <summary>
/// Equipment registry — manages the catalog of registered equipment
/// and links equipment to documentation, adapters, and data tiers.
/// </summary>
public interface IEquipmentRegistry
{
    /// <summary>Registers or updates an equipment entry.</summary>
    Task RegisterAsync(EquipmentRegistration equipment, CancellationToken ct = default);

    /// <summary>Gets equipment by ID.</summary>
    Task<EquipmentRegistration?> GetAsync(string equipmentId, CancellationToken ct = default);

    /// <summary>Lists all registered equipment, optionally filtered.</summary>
    Task<List<EquipmentRegistration>> ListAsync(
        string? fab = null, string? type = null, EquipmentStatus? status = null,
        CancellationToken ct = default);

    /// <summary>Updates equipment operational status.</summary>
    Task UpdateStatusAsync(string equipmentId, EquipmentStatus status, CancellationToken ct = default);

    /// <summary>Removes equipment from the registry.</summary>
    Task RemoveAsync(string equipmentId, CancellationToken ct = default);
}
