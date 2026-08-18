using OctoHD.App.Mvvm;

namespace OctoHD.App.Tests;

public sealed class MvvmTests
{
    [Fact]
    public void Observable_object_only_notifies_for_changes()
    {
        var subject = new TestObservable();
        var changes = new List<string?>();
        subject.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        subject.Value = 7;
        subject.Value = 7;
        subject.RaiseExplicitly();

        Assert.Equal([nameof(TestObservable.Value), "Explicit"], changes);
    }

    [Fact]
    public async Task Async_command_blocks_reentry_and_updates_availability()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;
        var notifications = 0;
        AsyncRelayCommand? command = null;
        command = new AsyncRelayCommand(async () =>
        {
            executions++;
            started.TrySetResult();
            await release.Task;
        });
        command.CanExecuteChanged += (_, _) =>
        {
            notifications++;
            if (command.CanExecute(null))
            {
                completed.TrySetResult();
            }
        };

        command.Execute(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(command.CanExecute(null));

        command.Execute(null);
        Assert.Equal(1, executions);

        release.SetResult();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(command.CanExecute(null));
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void Disabled_async_command_does_not_execute()
    {
        var executed = false;
        var command = new AsyncRelayCommand(
            () =>
            {
                executed = true;
                return Task.CompletedTask;
            },
            () => false);

        command.Execute(null);

        Assert.False(executed);
        Assert.False(command.CanExecute(null));
    }

    private sealed class TestObservable : ObservableObject
    {
        private int _value;

        public int Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        public void RaiseExplicitly() => OnPropertyChanged("Explicit");
    }
}
