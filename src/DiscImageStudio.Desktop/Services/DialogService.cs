using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DiscImageStudio.Desktop.ViewModels;
using DiscImageStudio.Desktop.Views;

namespace DiscImageStudio.Desktop.Services;

public interface IDialogService
{
    /// <summary>保存文件选择；filter 用 WPF 风格的 "显示名|*.ext|显示名|*.*"。</summary>
    Task<string?> PickSaveFileAsync(string title, string suggestedName, string defaultExtension, string filter);

    Task<string?> PickOpenFileAsync(string title, string filter);

    Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, string filter);

    Task<string?> PickFolderAsync(string title);

    Task ShowDiscSettingsAsync(DiscSettingsViewModel viewModel);

    Task ShowProjectSettingsAsync(ProjectSettingsViewModel viewModel);

    /// <summary>单行文本输入；取消返回 null。</summary>
    Task<string?> PromptAsync(string title, string label, string initial = "");

    /// <summary>二次确认；返回用户是否点了确认。</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "确定", string cancelText = "取消");
}

/// <summary>Avalonia StorageProvider 实现，把 WPF 的过滤器字符串翻译成文件类型。</summary>
public sealed class AvaloniaDialogService : IDialogService
{
    private readonly TopLevel _owner;

    public AvaloniaDialogService(TopLevel owner) => _owner = owner;

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, string defaultExtension, string filter)
    {
        IStorageFile? file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = defaultExtension.TrimStart('.'),
            FileTypeChoices = ParseFilter(filter),
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickOpenFileAsync(string title, string filter)
    {
        IReadOnlyList<IStorageFile> files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = ParseFilter(filter),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, string filter)
    {
        IReadOnlyList<IStorageFile> files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = ParseFilter(filter),
        });
        return files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        IReadOnlyList<IStorageFolder> folders = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task ShowDiscSettingsAsync(DiscSettingsViewModel viewModel)
    {
        if (_owner is not Window owner)
        {
            return;
        }

        DiscSettingsWindow dialog = new(viewModel);
        await dialog.ShowDialog(owner);
    }

    public async Task ShowProjectSettingsAsync(ProjectSettingsViewModel viewModel)
    {
        if (_owner is not Window owner)
        {
            return;
        }

        ProjectSettingsWindow dialog = new(viewModel);
        await dialog.ShowDialog(owner);
    }

    public async Task<string?> PromptAsync(string title, string label, string initial = "")
    {
        if (_owner is not Window owner)
        {
            return null;
        }

        var dialog = new PresetNameWindow(initial) { Title = title };
        return await dialog.ShowDialog<string?>(owner);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "确定",
        string cancelText = "取消")
    {
        if (_owner is not Window owner)
        {
            return false;
        }

        ConfirmWindow dialog = new(title, message, confirmText, cancelText);
        return await dialog.ShowDialog<bool>(owner);
    }

    public static FilePickerFileType[] ParseFilter(string filter)
    {
        string[] parts = filter.Split('|');
        List<FilePickerFileType> result = [];
        for (int index = 0; index + 1 < parts.Length; index += 2)
        {
            result.Add(new FilePickerFileType(parts[index])
            {
                Patterns = parts[index + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            });
        }

        return [.. result];
    }
}
