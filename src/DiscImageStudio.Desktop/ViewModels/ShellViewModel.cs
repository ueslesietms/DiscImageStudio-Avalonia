using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscImageStudio.Core;
using DiscImageStudio.Desktop.Services;
using DiscImageStudio.Localization;
using DiscImageStudio.Services;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Desktop.ViewModels;

/// <summary>
/// 外壳：工程生命周期（起始页 / 工作区）与侧栏导航。
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly HomePageViewModel _home;
    private readonly Dictionary<Workspace, object> _pages;
    private readonly RecentProjectStore _recentStore = new();
    private bool _loadInProgress;

    public ShellViewModel()
    {
        _home = new HomePageViewModel(this);
        Design = new DesignPageViewModel(this);
        Export = new ExportPageViewModel(this);
        Preview = new PreviewPageViewModel(this);
        Calibration = new CalibrationPageViewModel(this);
        Burn = new BurnPageViewModel(this);

        _pages = new Dictionary<Workspace, object>
        {
            [Workspace.Design] = Design,
            [Workspace.Export] = Export,
            [Workspace.Burn] = Burn,
            [Workspace.Preview] = Preview,
            [Workspace.Calibration] = Calibration,
        };

        NavItems =
        [
            CreateNav(Workspace.Design, "设计", "IconPalette"),
            CreateNav(Workspace.Export, "导出", "IconFileDown"),
            CreateNav(Workspace.Burn, "刻录", "IconFlame"),
            CreateNav(Workspace.Preview, "预览", "IconEye"),
            CreateNav(Workspace.Calibration, "标定", "IconCrosshair"),
        ];

        _currentPage = _home;
        LoadRecentProjects();
        Design.Layers.CollectionChanged += (_, _) => MarkDirty();
        State.PropertyChanged += (_, _) => MarkDirty();
        Design.PropertyChanged += Design_PropertyChanged;
    }

    private void Design_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DesignPageViewModel.SelectedLayer))
        {
            return;
        }

        MarkDirty();
    }

    public ObservableCollection<NavItemViewModel> NavItems { get; }

    /// <summary>主页的最近工程，最新的在最前。</summary>
    public ObservableCollection<RecentProjectEntry> RecentProjects { get; } = [];

    public DesignPageViewModel Design { get; }

    public ExportPageViewModel Export { get; }

    public PreviewPageViewModel Preview { get; }

    public CalibrationPageViewModel Calibration { get; }

    public BurnPageViewModel Burn { get; }

    /// <summary>共享参数状态：所有工作区绑定同一份。</summary>
    public DiscParametersState State { get; } = new();

    /// <summary>当前打开的工程；新建但未保存的工程也在这里。</summary>
    public ProjectDocument? CurrentProject { get; private set; }

    public string? CurrentProjectPath { get; private set; }

    public IDialogService? Dialogs { get; set; }

    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private Workspace _currentWorkspace = Workspace.Home;

    [ObservableProperty]
    private bool _isProjectOpen;

    [ObservableProperty]
    private bool _isSidebarCollapsed;

    [ObservableProperty]
    private string _projectTitle = "未命名工程";

    [ObservableProperty]
    private string _projectFamily = "CD";

    [ObservableProperty]
    private bool _isDirty;

    public string SaveStateText => IsDirty ? L.T("未保存") : string.Empty;

    public string ProjectTitleDisplay => IsSidebarCollapsed ? string.Empty : ProjectTitle;

    public void NavigateTo(Workspace workspace)
    {
        if (workspace == Workspace.Home)
        {
            BackHome();
            return;
        }

        CurrentWorkspace = workspace;
        CurrentPage = _pages[workspace];
        if (workspace == Workspace.Preview)
        {
            Preview.RefreshCommand.Execute(null);
        }
        foreach (NavItemViewModel item in NavItems)
        {
            item.IsActive = item.Workspace == workspace;
        }
    }

    [RelayCommand]
    private void BackHome()
    {
        IsProjectOpen = false;
        CurrentWorkspace = Workspace.Home;
        CurrentPage = _home;
        foreach (NavItemViewModel item in NavItems)
        {
            item.IsActive = false;
        }
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
        foreach (NavItemViewModel item in NavItems)
        {
            item.ShowLabel = !IsSidebarCollapsed;
        }

        OnPropertyChanged(nameof(ProjectTitleDisplay));
    }

    public void NewProject(OpticalDiscFamily family)
    {
        ProjectDocument document = NewProjectFactory.Create(family, "未命名工程");
        LoadProject(document, path: null);
        NavigateTo(Workspace.Design);
    }

    public async Task OpenProjectAsync()
    {
        if (Dialogs is not { } dialogs)
        {
            return;
        }

        string? path = await dialogs.PickOpenFileAsync(L.T("打开工程"), L.T("Disc Image Studio 工程|*.discproj"));
        if (path is null)
        {
            return;
        }

        OpenProject(path);
    }

    public void OpenProject(string path)
    {
        ProjectDocument document = ProjectJsonStore.Load(path);
        LoadProject(document, path);
        RecordRecent(document, path);
        NavigateTo(Workspace.Export);
    }

    private void MarkDirty()
    {
        if (!_loadInProgress && IsProjectOpen)
        {
            IsDirty = true;
        }
    }

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(SaveStateText));

    private void LoadRecentProjects()
    {
        RecentProjects.Clear();
        foreach (RecentProjectEntry entry in _recentStore.Load())
        {
            RecentProjects.Add(entry);
        }
    }

    [RelayCommand]
    private Task SaveProject() => SaveProjectAsync();

    /// <summary>工程设置里改名：同步标题和工程文档。</summary>
    public void RenameProject(string name)
    {
        if (CurrentProject is { } current)
        {
            CurrentProject = current with { Name = name };
            ProjectTitle = name;
            OnPropertyChanged(nameof(ProjectTitleDisplay));
            MarkDirty();
        }
    }

    [RelayCommand]
    private async Task OpenProjectSettingsAsync()
    {
        if (Dialogs is not { } dialogs || CurrentProject is not { } project)
        {
            return;
        }

        ProjectSettingsViewModel settings = new(project.Name, RenameProject);
        await dialogs.ShowProjectSettingsAsync(settings);
    }

    private void RecordRecent(ProjectDocument document, string path)
    {
        RecentProjectEntry entry = new(
            Path.GetFileNameWithoutExtension(path),
            document.DiscFamily,
            path,
            document.ThumbnailPngBase64,
            DateTimeOffset.UtcNow);
        _recentStore.Record(entry);
        LoadRecentProjects();
    }

    /// <summary>把当前状态写回工程文件；新建后第一次保存会让用户选路径。</summary>
    public async Task SaveProjectAsync()
    {
        if (CurrentProject is not { } current)
        {
            return;
        }

        string? path = CurrentProjectPath;
        if (path is null)
        {
            if (Dialogs is not { } dialogs)
            {
                return;
            }

            path = await dialogs.PickSaveFileAsync(
                L.T("保存工程"), "project.discproj", ".discproj",
                L.T("Disc Image Studio 工程|*.discproj"));
            if (path is null)
            {
                return;
            }
        }

        ProjectDocument document;
        try
        {
            document = BuildDocument(current);
            ProjectJsonStore.Save(path, document);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("[save] " + exception);
            throw;
        }
        CurrentProject = document;
        CurrentProjectPath = path;
        ProjectTitle = Path.GetFileNameWithoutExtension(path);
        OnPropertyChanged(nameof(ProjectTitleDisplay));
        IsDirty = false;
        RecordRecent(document, path);
    }

    private ProjectDocument BuildDocument(ProjectDocument current)
    {
        IReadOnlyList<ProjectLayer> layers = Design.Layers
            .AsEnumerable()
            .Reverse()
            .Select(layer => new ProjectLayer(
                layer.Id,
                layer.Name,
                layer.ImagePath,
                layer.IsVisible,
                layer.TranslateXMm,
                layer.TranslateYMm,
                layer.RotationDeg,
                layer.Scale * (Math.Max(layer.BaseWidthMm, layer.BaseHeightMm)
                    / (2 * Design.ContentOuterRadiusMm))))
            .ToList();

        return current with
        {
            Name = current.Name,
            Geometry = ProjectSession.CaptureGeometry(current, State),
            Layers = layers,
            Outputs = ProjectSession.CaptureOutputs(current, State),
            Tone = ProjectSession.CaptureTone(current, State),
            BluRay = ProjectSession.CaptureBluRay(current, State),
            ThumbnailPngBase64 = RenderThumbnail() ?? current.ThumbnailPngBase64,
            ModifiedUtc = DateTimeOffset.UtcNow,
        };
    }

    private string? RenderThumbnail()
    {
        try
        {
            using RenderTargetBitmap bitmap = Design.RenderCompositeBitmap(256);
            using MemoryStream stream = new();
            bitmap.Save(stream, new PngBitmapEncoderOptions());
            return Convert.ToBase64String(stream.ToArray());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void LoadProject(ProjectDocument document, string? path)
    {
        _loadInProgress = true;
        CurrentProject = document;
        CurrentProjectPath = path;
        ProjectSession.ApplyToState(document, State);
        Design.RefreshGeometry();
        Design.LoadFromProject(document);
        ProjectTitle = path is null
            ? document.Name
            : Path.GetFileNameWithoutExtension(path);
        ProjectFamily = document.FamilyLabel;
        IsProjectOpen = true;
        IsDirty = false;
        _loadInProgress = false;
        OnPropertyChanged(nameof(ProjectTitleDisplay));
    }

    private NavItemViewModel CreateNav(Workspace workspace, string label, string iconKey)
    {
        return new NavItemViewModel(item => NavigateTo(item.Workspace), workspace, label, iconKey);
    }
}
