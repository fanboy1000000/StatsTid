using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Interfaces;

public interface IIntegrationGateway
{
    Task<DeliveryStatus> SendAsync(string destination, object payload, CancellationToken ct = default);
    Task<DeliveryStatus> GetStatusAsync(Guid messageId, CancellationToken ct = default);
}
