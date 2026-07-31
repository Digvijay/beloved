using System.IO;
using System.Threading.Tasks;
using Beloved.ControlPlane.Models;

namespace Beloved.ControlPlane.Services
{
    public interface IModuleVerificationService
    {
        Task<(bool success, string message, CommunityModule? module)> VerifyAndPublishAsync(Stream zipStream, Tenant publisherTenant, string authorEmail = "");
    }
}
