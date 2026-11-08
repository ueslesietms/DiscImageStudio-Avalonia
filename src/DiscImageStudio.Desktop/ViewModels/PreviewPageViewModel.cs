using System.Globalization;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscImageStudio.Core;
using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Desktop.ViewModels;

/// <summary>预览页：把设计合成图按工程几何渲染成盘面投影，可导出 PNG。</summary>
public partial class PreviewPageViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private Bitmap? _preview;

    public PreviewPageViewModel(ShellViewModel shell)
    {
        _shell = shell;
        _shell.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ShellViewModel.CurrentProject) or nameof(ShellViewModel.IsProjectOpen))
            {
                OnPropertyChanged(nameof(GeometrySummary));
                OnPropertyChanged(nameof(FamilyLabel));
                Refresh();
            }
        };
        _shell.Design.Layers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasLayers));
    }

    public DiscParametersState State => _shell.State;

    public string FamilyLabel => _shell.CurrentProject?.FamilyLabel ?? "CD";

    public bool HasLayers => _shell.Design.Layers.Count > 0;

    public string GeometrySummary
    {
        get
        {
            (string inner, string outer, string sectors) = _shell.CurrentProject?.Family switch
            {
                OpticalDiscFamily.Dvd => (State.DvdInnerRadius, State.DvdOuterRadius, State.DvdTotalSectors),
                OpticalDiscFamily.BluRay => (State.BdInnerRadius, State.BdOuterRadius, State.BdTotalSectors),
                _ => (State.CdInnerRadius, State.CdOuterRadius, State.CdSectors),
            };
            return string.Create(CultureInfo.InvariantCulture,
                $"r0 {inner} mm · r1 {outer} mm · {sectors} 扇区");
        }
    }

    public Bitmap? Preview
    {
        get => _preview;
        private set
        {
            Bitmap? previous = _preview;
            if (SetProperty(ref _preview, value))
            {
                previous?.Dispose();
            }
        }
    }

    [ObservableProperty]
    private int _previewSize = 1280;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [RelayCommand]
    private void Refresh()
    {
        if (!HasLayers)
        {
            Preview = null;
            StatusMessage = L.T("设计页还没有图层。");
            return;
        }

        try
        {
            Preview = _shell.Design.RenderCompositeBitmap(Math.Clamp(PreviewSize, 256, 4096));
            StatusMessage = string.Empty;
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task ExportPngAsync()
    {
        if (_shell.Dialogs is not { } dialogs || !HasLayers)
        {
            return;
        }

        string? path = await dialogs.PickSaveFileAsync(
            L.T("导出预览图"), "preview.png", ".png", L.T("PNG 图片|*.png"));
        if (path is null)
        {
            return;
        }

        try
        {
            _shell.Design.RenderCompositePng(Math.Clamp(PreviewSize, 256, 4096), path);
            StatusMessage = L.F($"已导出 {Path.GetFileName(path)}");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private void GoNext() => _shell.NavigateTo(Workspace.Burn);

    [RelayCommand]
    private void GoBack() => _shell.NavigateTo(Workspace.Design);
}
