using System.Collections.Concurrent;

namespace CaMgr.Api.CaInterop;

/// <summary>
/// Runs all AD CS COM calls on one dedicated STA thread.
/// The certadm/certcli COM servers misbehave (E_UNEXPECTED) when invoked from
/// thread-pool MTA threads inside ASP.NET Core; a dedicated STA thread is the
/// reliable pattern for these apartment-threaded servers, and it also serializes
/// COM access across concurrent requests.
/// </summary>
public sealed class StaComScheduler : IDisposable
{
    private readonly BlockingCollection<Action> _queue = [];
    private readonly Thread _thread;

    public StaComScheduler()
    {
        _thread = new Thread(() =>
        {
            foreach (var work in _queue.GetConsumingEnumerable())
                work();
        })
        { IsBackground = true, Name = "camgr-com-sta" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    public Task InvokeAsync(Action act) =>
        InvokeAsync<object?>(() => { act(); return null; });

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}
