using ImGuiNET;

public sealed partial class MainWindow
{
    private void DrawConfigFileModals()
    {
        RenderAddConfigModal();
        RenderDeleteConfigModal();
    }

    private void RenderAddConfigModal()
    {
        var isAddModalOpen = _homeViewState.IsAddModalOpen;
        var addNameBuffer = _homeViewState.AddNameBuffer;
        if (_homeViewState.IsAddModalOpenRequested)
        {
            ImGui.OpenPopup("请输入新配置名称");
            _homeViewState.IsAddModalOpenRequested = false;
        }

        if (ImGui.BeginPopupModal("请输入新配置名称", ref isAddModalOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.InputText("##AddConfigNameInput", ref addNameBuffer, 256);
            _homeViewState.AddNameBuffer = addNameBuffer;
            if (!string.IsNullOrEmpty(_homeViewState.AddError))
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(_homeViewState.AddError);
            }

            if (ImGui.Button("创建"))
            {
                if (TryCreateEmptyConfigFile(_homeViewState.AddNameBuffer, out var err))
                {
                    _homeViewState.CloseAddModal();
                    ImGui.CloseCurrentPopup();
                }
                else
                {
                    _homeViewState.AddError = err;
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("取消"))
            {
                _homeViewState.CloseAddModal();
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        _homeViewState.IsAddModalOpen = isAddModalOpen;
    }

    private void RenderDeleteConfigModal()
    {
        var isDeleteModalOpen = _homeViewState.IsDeleteModalOpen;
        if (_homeViewState.IsDeleteModalOpenRequested)
        {
            ImGui.OpenPopup("删除配置确认");
            _homeViewState.IsDeleteModalOpenRequested = false;
        }

        if (ImGui.BeginPopupModal("删除配置确认", ref isDeleteModalOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            var name = _homeViewState.PendingDeleteConfigBaseName ?? string.Empty;
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted($"确定删除配置文件 {name} 吗？此操作不可撤销。");
            if (ImGui.Button("确定"))
            {
                TryDeleteSelectedConfigFile(name);
                _homeViewState.CloseDeleteModal();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("取消"))
            {
                _homeViewState.CloseDeleteModal();
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        _homeViewState.IsDeleteModalOpen = isDeleteModalOpen;
    }

    private bool TryCreateEmptyConfigFile(string rawName, out string error)
    {
        if (_configRepository.TryCreateEmptyConfigFile(
                rawName,
                SpecialWeaponLogicConfigKey,
                AimSnapWeaponListConfigKey,
                RapidFireWeaponListConfigKey,
                ReleaseFireWeaponListConfigKey,
                out var baseName,
                out error))
        {
            ResetConfigUiStateToDefaults();
            RefreshConfigFiles(baseName);
            return true;
        }

        return false;
    }

    private void TryDeleteSelectedConfigFile(string baseName)
    {
        _configRepository.TryDeleteConfigFile(baseName);
        RefreshConfigFiles();
        if (_configFiles.Count > 0)
        {
            WriteCurrentConfigFileName(_configFiles[_selectedConfigFileIndex]);
        }
        else
        {
            _configRepository.ClearCurrentConfigPointerFile();
        }
    }
}
