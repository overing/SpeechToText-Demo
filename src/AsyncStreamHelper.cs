using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace System.Collections.Generic
{
    public static class AsyncStreamHelper
    {
        public static async IAsyncEnumerable<T> WithEnforcedCancellation<T>(this IAsyncEnumerable<T> source, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var enumerator = source.GetAsyncEnumerator(cancellationToken);
            Task<bool>? moveNext = null;

            var untilCanceled = new Task<bool>(() => true, cancellationToken);
            try
            {
                while (
                    await (
                        await Task.WhenAny(
                            (moveNext = enumerator.MoveNextAsync().AsTask()),
                            untilCanceled
                        ).ConfigureAwait(continueOnCapturedContext: false)
                    )
                )
                    yield return enumerator.Current;
            }
            finally
            {
                if (moveNext != null && !moveNext.IsCompleted)
                    _ = moveNext.ContinueWith(async _ => await enumerator.DisposeAsync(), TaskScheduler.Default);
                else if (enumerator != null)
                    await enumerator.DisposeAsync();
            }
        }
    }

}