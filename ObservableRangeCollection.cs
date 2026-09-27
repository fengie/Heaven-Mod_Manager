using MhwModManager.Core;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace MhwModManager.App.ViewModels;

public sealed class ObservableRangeCollection<T>:ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        CheckReentrancy();
        Items.Clear();
        foreach(var item in items)Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
