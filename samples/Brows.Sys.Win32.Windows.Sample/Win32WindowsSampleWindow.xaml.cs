using Brows.Composition;
using Brows.Sys;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace Brows;

internal sealed partial class Win32WindowsSampleWindow : Window, IExport {
    private readonly EventLog Log = new();

    private EventLogController Controller;
    private bool ClosingStarted;
    private bool AllowClose;

    private void OnSourceReady(object sender, EventArgs e) {
        try {
            Controller = new EventLogController(SystemMessengerSet, this, Log, Dispatcher);
            Controller.Start();
        }
        catch (Exception exception) {
            Log.Fail(exception);
        }
    }

    private async void OnWindowClosing(object sender, CancelEventArgs e) {
        if (AllowClose) {
            return;
        }
        e.Cancel = true;
        if (ClosingStarted) {
            return;
        }
        ClosingStarted = true;
        try {
            if (Controller is not null) {
                await Controller.StopAsync();
            }
        }
        catch (Exception exception) {
            Log.Fail(exception);
        }
        finally {
            AllowClose = true;
            _ = Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.Normal);
        }
    }

    private void OnRowsChanged(object sender, NotifyCollectionChangedEventArgs e) {
        var shouldScroll = Log.AutoScroll && Log.Count > 0;
        if (shouldScroll) {
            EventGrid.ScrollIntoView(Log.Entries[Log.Count - 1]);
        }
    }

    private void OnClear(object sender, RoutedEventArgs e) {
        Log.Clear();
    }

    private void OnPause(object sender, RoutedEventArgs e) {
        Log.TogglePause();
    }

    [ImportRequired]
    internal ISystemMessengerSet SystemMessengerSet { get; set; }

    public Win32WindowsSampleWindow() {
        InitializeComponent();
        DataContext = Log;
        SourceInitialized += OnSourceReady;
        Closing += OnWindowClosing;
        ((INotifyCollectionChanged)Log.Entries).CollectionChanged += OnRowsChanged;
    }
}
