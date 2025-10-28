using Polly;
using Polly.Retry;

namespace ComplaintProcessor.Worker.Policies
{
    /// <summary>
    /// Wrapper para injetar a política de resiliência do Banco de Dados via DI.
    /// </summary>
    public class DbResiliencePolicy
    {
        public AsyncRetryPolicy Policy { get; }
        public DbResiliencePolicy(AsyncRetryPolicy policy) => Policy = policy;
    }

    /// <summary>
    /// Wrapper para injetar a política de resiliência do SNS via DI.
    /// </summary>
    public class SnsResiliencePolicy
    {
        public AsyncRetryPolicy Policy { get; }
        public SnsResiliencePolicy(AsyncRetryPolicy policy) => Policy = policy;
    }
}