# Known issues
1. Gracefully fail when 32-bit dump is opened.

# Test cases
- Support value task.
- Support for TaskSourceSlim (a wrapper around TCS).
- Support IAsyncEnumerable
- Support ITaskSource
- Parallel.For

# Features
- Add overall stats
- Console-based output
- Tests (move all the dumps to a nuget package/azure blob and download on the fly?)


For instance when Task.ContinueWith relationship is unclear

Add web-based UI (with an ability to drill down into a taks's state)



Add an adge name for visualization purposes!
Add a name for a link. For instance, ContinueWith or 'Sets the result' (for the task that potentially sets the result of TCS)

Task.Run -> Runs
task.ContinueWith -> ContinueWith
References tcs -> Potentially sets the result
Async state machine -> awaits
