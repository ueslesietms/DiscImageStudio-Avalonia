using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Media.Imaging;
using DiscImageStudio.Core;
using DiscImageStudio.Desktop.Controls;
using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Desktop.ViewModels;

/// <summary>设计页：图层栈、选中状态、画布绘制用的几何快照。合成图与影调后续接。</summary>
public partial class DesignPageViewModel : ObservableObject
{
    public const double DiscRadiusMm = 60.0;
    public const double HubRadiusMm = 15.0;

    private readonly ShellViewModel _shell;

    public DesignPageViewModel(ShellViewModel shell)
    {
        _shell = shell;
        _shell.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ShellViewModel.CurrentProject) or nameof(ShellViewModel.IsProjectOpen))
            {
                RefreshGeometry();
            }
        };
        Layers.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasLayers));
            if (SelectedLayer is not null && !Layers.Contains(SelectedLayer))
            {
                SelectedLayer = null;
            }
        };
        RefreshGeometry();
    }

    public ObservableCollection<DesignLayerViewModel> Layers { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private DesignLayerViewModel? _selectedLayer;

    partial void OnSelectedLayerChanged(DesignLayerViewModel? value)
    {
        foreach (DesignLayerViewModel layer in Layers)
        {
            layer.IsSelected = ReferenceEquals(layer, value);
        }
    }

    public bool HasLayers => Layers.Count > 0;

    public bool HasSelection => SelectedLayer is not null;

    public DiscParametersState State => _shell.State;

    public bool IsCd => _shell.CurrentProject?.IsCd ?? true;

    public bool IsDvd => _shell.CurrentProject?.IsDvd ?? false;

    public bool IsBluRay => _shell.CurrentProject?.IsBluRay ?? false;

    public string FamilyLabel => _shell.CurrentProject?.FamilyLabel ?? "CD";

    /// <summary>内容环内半径（毫米），画布用它画引导圈和标题。</summary>
    public double ContentInnerRadiusMm { get; private set; } = 24.5;

    public double ContentOuterRadiusMm { get; private set; } = 56.8;

    public string ContentRingText =>
        L.F($"{ContentInnerRadiusMm:0.##}–{ContentOuterRadiusMm:0.##} mm");

    [RelayCommand]
    private void GoNext() => _shell.NavigateTo(Workspace.Export);

    [RelayCommand]
    private void GoBack() => _shell.NavigateTo(Workspace.Preview);

    [RelayCommand]
    private async Task ImportImagesAsync()
    {
        if (_shell.Dialogs is not { } dialogs)
        {
            return;
        }

        IReadOnlyList<string> files = await dialogs.PickOpenFilesAsync(
            L.T("导入图片"),
            L.T("图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*"));
        foreach (string path in files)
        {
            AddLayer(path);
        }
    }

    public DesignLayerViewModel AddLayer(string path)
    {
        DesignLayerViewModel layer = new()
        {
            Name = Path.GetFileNameWithoutExtension(path),
            ImagePath = path,
            SelectCommand = new RelayCommand<DesignLayerViewModel>(_ => SelectedLayer = _),
        };
        if (!layer.LoadImage(ContentOuterRadiusMm))
        {
            // 读不出来也保留图层，画布上会画成红色虚框，提示图片丢了。
            layer.Name = L.F($"{Path.GetFileNameWithoutExtension(path)}（无法读取）");
        }

        Layers.Insert(0, layer);
        SelectedLayer = layer;
        return layer;
    }

    /// <summary>随盘文件：整包放进盘里的数据目录，写盘时按内圈/外圈布置。</summary>
    [RelayCommand]
    private async Task BrowseDataAsync()
    {
        if (_shell.Dialogs is not { } dialogs)
        {
            return;
        }

        string title = IsCd ? "选择要放进 CD 数据盘的文件夹"
            : IsDvd ? "选择要放进 DVD 的文件夹"
            : "选择要放进蓝光盘的文件夹";
        string? folder = await dialogs.PickFolderAsync(title);
        if (folder is null)
        {
            return;
        }

        if (IsCd)
        {
            State.CdDataDirectory = folder;
        }
        else if (IsDvd)
        {
            State.DvdDataDirectory = folder;
        }
        else
        {
            State.BdDataDirectory = folder;
        }

        OnPropertyChanged(nameof(HasDataFiles));
        OnPropertyChanged(nameof(DataDirectory));
    }

    public bool HasDataFiles => IsCd ? State.HasCdFiles : IsDvd ? State.HasDvdFiles : State.HasBluRayFiles;

    public string DataDirectory
    {
        get => IsCd ? State.CdDataDirectory : IsDvd ? State.DvdDataDirectory : State.BdDataDirectory;
        set
        {
            if (IsCd)
            {
                State.CdDataDirectory = value;
            }
            else if (IsDvd)
            {
                State.DvdDataDirectory = value;
            }
            else
            {
                State.BdDataDirectory = value;
            }

            OnPropertyChanged(nameof(HasDataFiles));
        }
    }

    public bool FilesAtOuterRing
    {
        get => IsCd ? State.CdFilesAtOuterRing : IsDvd ? State.DvdFilesAtOuterRing : State.BdFilesAtOuterRing;
        set
        {
            if (IsCd)
            {
                State.CdFilesAtOuterRing = value;
            }
            else if (IsDvd)
            {
                State.DvdFilesAtOuterRing = value;
            }
            else
            {
                State.BdFilesAtOuterRing = value;
            }
        }
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedLayer is { } layer)
        {
            Layers.Remove(layer);
        }
    }

    [RelayCommand]
    private void ToggleVisibility(DesignLayerViewModel? layer)
    {
        if (layer is not null)
        {
            layer.IsVisible = !layer.IsVisible;
        }
    }

    /// <summary>把盘面合成图渲染成位图（预览直接显示它）。必须在 UI 线程调用，调用方负责释放。</summary>
    public RenderTargetBitmap RenderCompositeBitmap(int size)
    {
        DiscCompositeControl control = new() { Design = this };
        control.Measure(new Size(size, size));
        control.Arrange(new Rect(0, 0, size, size));
        RenderTargetBitmap bitmap = new(new PixelSize(size, size));
        bitmap.Render(control);
        return bitmap;
    }

    /// <summary>把盘面合成图渲染成 PNG（导出/刻录的源图）。必须在 UI 线程调用。</summary>
    public string RenderCompositePng(int size, string path)
    {
        using RenderTargetBitmap bitmap = RenderCompositeBitmap(size);
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        bitmap.Save(path, new PngBitmapEncoderOptions());
        return path;
    }

    /// <summary>打开工程时把工程的图层装回画布；图片按当前内容环直径重新算基准尺寸。</summary>
    public void LoadFromProject(ProjectDocument document)
    {
        Layers.Clear();
        SelectedLayer = null;
        foreach (ProjectLayer saved in document.Layers.Reverse())
        {
            DesignLayerViewModel layer = new()
            {
                Name = saved.Name,
                ImagePath = saved.ImagePath,
                IsVisible = saved.Visible,
                TranslateXMm = saved.TranslateXMm,
                TranslateYMm = saved.TranslateYMm,
                RotationDeg = saved.RotationDeg,
                Scale = saved.Scale,
                SelectCommand = new RelayCommand<DesignLayerViewModel>(item => SelectedLayer = item),
            };
            layer.LoadImage(ContentOuterRadiusMm);
            Layers.Add(layer);
        }

        if (Layers.Count > 0)
        {
            SelectedLayer = Layers[0];
        }

        OnPropertyChanged(nameof(HasLayers));
    }

    /// <summary>切盘型或打开工程后，内容环跟着当前参数的半径走。</summary>
    public void RefreshGeometry()
    {
        DiscParametersState state = _shell.State;
        (string inner, string outer) = _shell.CurrentProject?.Family switch
        {
            OpticalDiscFamily.Dvd => (state.DvdInnerRadius, state.DvdOuterRadius),
            OpticalDiscFamily.BluRay => (state.BdInnerRadius, state.BdOuterRadius),
            _ => (state.CdInnerRadius, state.CdOuterRadius),
        };

        ContentInnerRadiusMm = ParseMm(inner, 24.5);
        ContentOuterRadiusMm = ParseMm(outer, 56.8);
        if (ContentOuterRadiusMm <= ContentInnerRadiusMm)
        {
            ContentOuterRadiusMm = ContentInnerRadiusMm + 1;
        }

        OnPropertyChanged(nameof(ContentInnerRadiusMm));
        OnPropertyChanged(nameof(ContentOuterRadiusMm));
        OnPropertyChanged(nameof(ContentRingText));
        OnPropertyChanged(nameof(FamilyLabel));
        OnPropertyChanged(nameof(IsCd));
        OnPropertyChanged(nameof(IsDvd));
        OnPropertyChanged(nameof(IsBluRay));
        OnPropertyChanged(nameof(DataDirectory));
        OnPropertyChanged(nameof(FilesAtOuterRing));
        OnPropertyChanged(nameof(HasDataFiles));
    }

    private static double ParseMm(string text, double fallback) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
        && double.IsFinite(value) && value > 0
            ? value
            : fallback;
}
