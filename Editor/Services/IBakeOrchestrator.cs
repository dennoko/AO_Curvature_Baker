using System.Threading;
using System.Threading.Tasks;

namespace DennokoWorks.Tool.AOBaker
{
    public interface IBakeOrchestrator
    {
        Task ExecuteBakePipelineAsync(BakeState state, CancellationToken cancellationToken = default);
    }
}
