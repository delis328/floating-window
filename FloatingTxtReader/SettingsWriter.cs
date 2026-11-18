using System.Text.Json;

namespace FloatingTxtReader;

internal sealed class SettingsWriter(Action<string> writeSnapshot)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private string? _pendingJson;
    private TaskCompletionSource? _completion;

    internal Task SaveAsync(AppState state)
    {
        // Capture mutable UI state before handing the immutable snapshot to the worker.
        var json = JsonSerializer.Serialize(state, JsonOptions);
        lock (_gate)
        {
            _pendingJson = json;
            if (_completion is null)
            {
                _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = Task.Run(WritePending);
            }
            return _completion.Task;
        }
    }

    private void WritePending()
    {
        Exception? error = null;
        while (true)
        {
            string json;
            lock (_gate)
            {
                if (_pendingJson is null)
                {
                    var completion = _completion!;
                    _completion = null;
                    if (error is null) completion.SetResult();
                    else completion.SetException(error);
                    return;
                }
                json = _pendingJson;
                _pendingJson = null;
            }
            try
            {
                writeSnapshot(json);
                error = null;
            }
            catch (Exception ex)
            {
                // Propagate a failed final write through the task, without stranding later saves.
                error = ex;
            }
        }
    }
}
