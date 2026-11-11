using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DiscImageStudio.Desktop.ViewModels;

/// <summary>工程设置：只负责工程层面的信息（名字），盘片参数在独立的盘片设置里。</summary>
public partial class ProjectSettingsViewModel : ObservableObject
{
    private readonly Action<string> _rename;

    public ProjectSettingsViewModel(string name, Action<string> rename)
    {
        _name = name;
        _rename = rename;
    }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _saved;

    [RelayCommand]
    private void Save()
    {
        string trimmed = Name.Trim();
        if (trimmed.Length == 0)
        {
            ErrorMessage = "工程名不能为空。";
            return;
        }

        ErrorMessage = string.Empty;
        _rename(trimmed);
        Saved = true;
    }
}
