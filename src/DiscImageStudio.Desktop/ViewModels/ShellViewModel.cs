using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscImageStudio.Core;
using DiscImageStudio.Desktop.Services;
using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Desktop.ViewModels;

/// <summary>
/// 外壳：工程生命周期（起始页 / 工作区）与侧栏导航。
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly HomePageViewModel _home;
    private readonly Dictionary<Workspace, object> _pages;

    public ShellViewModel()
    {
        _home = new HomePageViewModel(this);
        Design = new DesignPageViewModel(this);
        Export = new ExportPageViewModel(this);

        _pages = new Dictionary<Workspace, object>
        {
            [Workspace.Design] = Design,
            [Workspace.Export] = Export,
            [Workspace.Burn] = new WorkspacePageViewModel(Workspace.Burn, "刻录", "把镜像写入可报废的空白盘片。", "IconFlame"),
            [Workspace.Preview] = new WorkspacePageViewModel(Workspace.Preview, "预览", "核对盘面投影与实测几何。", "IconEye"),
            [Workspace.Calibration] = new WorkspacePageViewModel(Workspace.Calibration, "标定", "录入照片与轨迹，校准物理模型。", "IconCrosshair"),
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
    }

    public ObservableCollection<NavItemViewModel> NavItems { get; }

    public DesignPageViewModel Design { get; }

    public ExportPageViewModel Export { get; }

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
        NavigateTo(Workspace.Export);
    }

    private void LoadProject(ProjectDocument document, string? path)
    {
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
        OnPropertyChanged(nameof(ProjectTitleDisplay));
    }

    private NavItemViewModel CreateNav(Workspace workspace, string label, string iconKey)
    {
        return new NavItemViewModel(item => NavigateTo(item.Workspace), workspace, label, iconKey);
    }
}
