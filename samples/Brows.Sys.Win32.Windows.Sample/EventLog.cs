using Brows.Sys;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Brows;

internal sealed class EventLog : INotifyPropertyChanged {
    private const int DefaultCapacity = 1000;

    private readonly int Capacity;
    private readonly ObservableCollection<EventLogEntry> Rows = [];

    private State CurrentState { get; set; } = State.Starting;

    private void Notify([CallerMemberName] string propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void SetState(State state) {
        CurrentState = state;
        Notify(nameof(Status));
        Notify(nameof(CanPause));
        Notify(nameof(PauseText));
    }

    internal EventLog(int capacity = DefaultCapacity) {
        if (capacity <= 0) {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        Capacity = capacity;
        Entries = new ReadOnlyObservableCollection<EventLogEntry>(Rows);
    }

    internal void Append(ISystemMessage message, DateTimeOffset timestamp) {
        if (CurrentState != State.Listening) {
            return;
        }
        var entry = EventLogEntry.FromMessage(message, timestamp);
        if (Rows.Count == Capacity) {
            Rows.RemoveAt(0);
        }
        Rows.Add(entry);
        Notify(nameof(Count));
        Notify(nameof(IsEmpty));
    }

    internal void Clear() {
        Rows.Clear();
        Notify(nameof(Count));
        Notify(nameof(IsEmpty));
    }

    internal void TogglePause() {
        if (!CanPause) {
            return;
        }
        SetState(CurrentState == State.Paused ? State.Listening : State.Paused);
    }

    internal void Listening() {
        SetState(State.Listening);
    }

    internal void Stopping() {
        SetState(State.Stopping);
    }

    internal void Stopped() {
        SetState(State.Stopped);
    }

    internal void Fail(Exception exception) {
        Error = exception.Message;
        SetState(State.Error);
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public ReadOnlyObservableCollection<EventLogEntry> Entries { get; }

    public int Count => Rows.Count;

    public bool IsEmpty => Rows.Count == 0;

    public string Status => CurrentState.ToString();

    public bool CanPause => CurrentState is State.Listening or State.Paused;

    public string PauseText => CurrentState == State.Paused ? "Resume" : "Pause";

    public string Error {
        get;
        private set {
            field = value;
            Notify();
        }
    } = string.Empty;

    public bool AutoScroll {
        get;
        set {
            field = value;
            Notify();
        }
    } = true;

    private enum State {
        Starting,
        Listening,
        Paused,
        Stopping,
        Stopped,
        Error
    }
}
