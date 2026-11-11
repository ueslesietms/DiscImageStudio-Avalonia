using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using DiscImageStudio.Desktop.ViewModels;

namespace DiscImageStudio.Desktop.Views;

public partial class BurnView : UserControl
{
    private BurnPageViewModel? _viewModel;

    public BurnView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Log.CollectionChanged -= OnLogChanged;
        }

        _viewModel = DataContext as BurnPageViewModel;
        if (_viewModel is not null)
        {
            _viewModel.Log.CollectionChanged += OnLogChanged;
        }
    }

    /// <summary>新日志行进来就翻到底；等一次布局，否则刚加的行还没测量到高度。</summary>
    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => LogScroller.ScrollToEnd(), DispatcherPriority.Background);
    }
}
