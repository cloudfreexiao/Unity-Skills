using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace UnitySkills
{
    public class AIConfigTabController
    {
        private const string TabUxmlPath = "Packages/com.besty.unity-skills/Editor/UI/Tabs/AIConfigTab.uxml";

        private readonly VisualElement _root;
        private readonly UnitySkillsWindow _window;

        // UI references
        private Label _configTitle;
        private VisualElement _agentsContainer;

        private Label _customTitle;
        private Label _customPathLabel;
        private TextField _customPathField;
        private Button _customPathBrowseBtn;
        private Label _customAgentLabel;
        private TextField _customAgentField;
        private Button _customInstallBtn;

        private HelpBox _helpBox;

        // Agent metadata configs
        private List<AgentConfig> _agentConfigs;

        private class AgentConfig
        {
            public string id;
            public string name;
            public Func<bool> isProjInstalled;
            public Func<bool> isGlobInstalled;
            public Func<bool, (bool success, string message)> installFunc;
            public Func<bool, (bool success, string message)> uninstallFunc;
            public Func<bool, string, string> getInstallSuccessMsg;
        }

        public AIConfigTabController(VisualElement root, UnitySkillsWindow window)
        {
            _root = root;
            _window = window;

            // Load tab UXML and append to root
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TabUxmlPath);
            if (uxml != null)
            {
                uxml.CloneTree(_root);
            }
            else
            {
                Debug.LogError($"[UnitySkills] Failed to load AIConfigTab UXML at path: {TabUxmlPath}");
                return;
            }

            CacheUiReferences();
            SetupAgentConfigs();
            Initialize();
        }

        private void CacheUiReferences()
        {
            _configTitle = _root.Q<Label>("config-title");
            _agentsContainer = _root.Q<VisualElement>("agents-container");

            _customTitle = _root.Q<Label>("custom-title");
            _customPathLabel = _root.Q<Label>("custom-path-label");
            _customPathField = _root.Q<TextField>("custom-path-field");
            _customPathBrowseBtn = _root.Q<Button>("custom-path-browse-btn");
            _customAgentLabel = _root.Q<Label>("custom-agent-label");
            _customAgentField = _root.Q<TextField>("custom-agent-field");
            _customInstallBtn = _root.Q<Button>("custom-install-btn");

            _helpBox = _root.Q<HelpBox>("help-box");
        }

        private void SetupAgentConfigs()
        {
            _agentConfigs = new List<AgentConfig>
            {
                new AgentConfig
                {
                    id = "claude_code",
                    name = "Claude Code",
                    isProjInstalled = () => SkillInstaller.IsClaudeProjectInstalled,
                    isGlobInstalled = () => SkillInstaller.IsClaudeGlobalInstalled,
                    installFunc = SkillInstaller.InstallClaude,
                    uninstallFunc = SkillInstaller.UninstallClaude
                },
                new AgentConfig
                {
                    id = "antigravity",
                    name = "Antigravity",
                    isProjInstalled = () => SkillInstaller.IsAntigravityProjectInstalled,
                    isGlobInstalled = () => SkillInstaller.IsAntigravityGlobalInstalled,
                    installFunc = SkillInstaller.InstallAntigravity,
                    uninstallFunc = SkillInstaller.UninstallAntigravity
                },
                new AgentConfig
                {
                    id = "codex",
                    name = "Codex",
                    isProjInstalled = () => SkillInstaller.IsCodexProjectInstalled,
                    isGlobInstalled = () => SkillInstaller.IsCodexGlobalInstalled,
                    installFunc = SkillInstaller.InstallCodex,
                    uninstallFunc = SkillInstaller.UninstallCodex,
                    getInstallSuccessMsg = (global, msg) => SkillsLocalization.Current == SkillsLocalization.Language.Chinese
                        ? "安装成功！\n" + msg + (global ? "" : "\n\n注意：Antigravity 和 Codex 工作区共享 .agents/skills 路径。")
                        : "Install success!\n" + msg + (global ? "" : "\n\nNote: Antigravity and Codex share .agents/skills in workspace mode.")
                },
                new AgentConfig
                {
                    id = "cursor",
                    name = "Cursor",
                    isProjInstalled = () => SkillInstaller.IsCursorProjectInstalled,
                    isGlobInstalled = () => SkillInstaller.IsCursorGlobalInstalled,
                    installFunc = SkillInstaller.InstallCursor,
                    uninstallFunc = SkillInstaller.UninstallCursor
                }
            };
        }

        private void Initialize()
        {
            // Bind custom path browse button
            if (_customPathBrowseBtn != null)
            {
                _customPathBrowseBtn.clicked += BrowseCustomPath;
            }

            // Bind custom install button
            if (_customInstallBtn != null)
            {
                _customInstallBtn.clicked += InstallCustom;
            }

            // Set default custom fields
            if (_customAgentField != null)
            {
                _customAgentField.value = "Custom";
            }

            // Build Agent list
            RebuildAgentsList();
        }

        private void BrowseCustomPath()
        {
            if (_customPathField == null) return;

            string title = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "选择安装目录" : "Select Install Directory";
            string selectedPath = EditorUtility.OpenFolderPanel(title, _customPathField.value, "");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                _customPathField.value = selectedPath;
            }
        }

        private void InstallCustom()
        {
            if (_customPathField == null || _customAgentField == null) return;

            string path = _customPathField.value;
            string agentName = _customAgentField.value;

            if (string.IsNullOrEmpty(path))
            {
                string errorTitle = "Error";
                string errorMsg = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "路径不能为空" : "Path cannot be empty";
                EditorUtility.DisplayDialog(errorTitle, errorMsg, "OK");
                return;
            }

            var result = SkillInstaller.InstallCustom(path, agentName);
            if (result.success)
            {
                EditorUtility.DisplayDialog("Success", L("install_success"), "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Error", string.Format(L("install_failed"), result.message), "OK");
            }
        }

        private void RebuildAgentsList()
        {
            if (_agentsContainer == null) return;
            _agentsContainer.Clear();

            foreach (var cfg in _agentConfigs)
            {
                var card = new VisualElement();
                card.AddToClassList("card");

                // Agent Header Name Row
                var headerRow = new VisualElement();
                headerRow.AddToClassList("horizontal-layout");
                headerRow.style.marginBottom = 6;

                var nameLabel = new Label(cfg.name);
                nameLabel.AddToClassList("bold-label");
                nameLabel.style.fontSize = 12;
                headerRow.Add(nameLabel);

                card.Add(headerRow);

                // Row 1: Project Installation
                var projRow = CreateAgentRow(cfg, false);
                card.Add(projRow);

                // Space
                var spacing = new VisualElement();
                spacing.style.height = 4;
                card.Add(spacing);

                // Row 2: Global Installation
                var globRow = CreateAgentRow(cfg, true);
                card.Add(globRow);

                _agentsContainer.Add(card);
            }
        }

        private VisualElement CreateAgentRow(AgentConfig cfg, bool isGlobal)
        {
            var row = new VisualElement();
            row.AddToClassList("horizontal-layout");
            row.style.height = 22;

            // 1. Row Label
            string rowText = isGlobal ? L("install_global") : L("install_project");
            var label = new Label(rowText + ":");
            label.style.width = 110;
            label.style.fontSize = 11;
            row.Add(label);

            // 2. Status Label & Buttons Container
            var actionsContainer = new VisualElement();
            actionsContainer.AddToClassList("horizontal-layout");
            actionsContainer.AddToClassList("flex-grow");
            actionsContainer.style.justifyContent = Justify.FlexStart;

            bool isInstalled = isGlobal ? cfg.isGlobInstalled() : cfg.isProjInstalled();

            if (isInstalled)
            {
                // Installed text label
                var installedLabel = new Label(L("installed"));
                installedLabel.AddToClassList("bold-label");
                installedLabel.style.color = new Color(0.3f, 0.8f, 0.4f);
                installedLabel.style.fontSize = 11;
                installedLabel.style.marginRight = 10;
                installedLabel.style.width = 60;
                actionsContainer.Add(installedLabel);

                // Update Button
                var updateBtn = new Button(() => OnInstallClick(cfg, isGlobal, true));
                updateBtn.text = L("update");
                updateBtn.AddToClassList("btn-mini");
                updateBtn.style.width = 50;
                updateBtn.style.marginRight = 4;
                actionsContainer.Add(updateBtn);

                // Uninstall Button
                var uninstallBtn = new Button(() => OnUninstallClick(cfg, isGlobal));
                uninstallBtn.text = L("uninstall");
                uninstallBtn.AddToClassList("btn-mini");
                uninstallBtn.style.width = 60;
                actionsContainer.Add(uninstallBtn);
            }
            else
            {
                // Install Button
                var installBtn = new Button(() => OnInstallClick(cfg, isGlobal, false));
                installBtn.text = isGlobal ? L("install_global") : L("install_project");
                installBtn.AddToClassList("btn-mini");
                installBtn.style.width = 120;
                actionsContainer.Add(installBtn);
            }

            row.Add(actionsContainer);
            return row;
        }

        private void OnInstallClick(AgentConfig cfg, bool isGlobal, bool isUpdate)
        {
            var result = cfg.installFunc(isGlobal);
            if (result.success)
            {
                string successMsg = cfg.getInstallSuccessMsg != null 
                    ? cfg.getInstallSuccessMsg(isGlobal, result.message) 
                    : L("install_success") + "\n" + result.message;

                EditorUtility.DisplayDialog("Success", successMsg, "OK");
            }
            else
            {
                string errorTitle = "Error";
                string errorMsg = isUpdate 
                    ? string.Format(L("update_failed"), result.message) 
                    : string.Format(L("install_failed"), result.message);
                EditorUtility.DisplayDialog(errorTitle, errorMsg, "OK");
            }

            // Refresh UI list state
            RebuildAgentsList();
        }

        private void OnUninstallClick(AgentConfig cfg, bool isGlobal)
        {
            string scopeText = isGlobal ? " (Global)" : " (Project)";
            string confirmMsg = string.Format(L("uninstall_confirm"), cfg.name + scopeText);

            if (EditorUtility.DisplayDialog(L("uninstall"), confirmMsg, "OK", "Cancel"))
            {
                var result = cfg.uninstallFunc(isGlobal);
                if (result.success)
                {
                    EditorUtility.DisplayDialog("Success", L("uninstall_success"), "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", string.Format(L("uninstall_failed"), result.message), "OK");
                }

                // Refresh UI list state
                RebuildAgentsList();
            }
        }

        public void RefreshLocalization()
        {
            if (_configTitle != null) _configTitle.text = L("skill_config");

            // Custom section
            if (_customTitle != null) _customTitle.text = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "自定义安装位置" : "Custom Install Location";
            if (_customPathLabel != null) _customPathLabel.text = L("path") + ":";
            if (_customAgentLabel != null) _customAgentLabel.text = "Agent:";
            if (_customInstallBtn != null) _customInstallBtn.text = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "安装 / 更新" : "Install / Update";

            // Help text box
            if (_helpBox != null)
            {
                _helpBox.text = SkillsLocalization.Current == SkillsLocalization.Language.Chinese
                    ? "项目安装：将 Skill 安装到当前 Unity 项目目录\n全局安装：将 Skill 安装到用户目录，所有项目可用\n\n注意：Antigravity 和 Codex 工作区都使用 .agents/skills，安装一次即两边可用"
                    : "Project Install: Install skill to current Unity project\nGlobal Install: Install skill to user folder, available for all projects\n\nNote: Antigravity and Codex both use .agents/skills in workspace mode — install once works for both.";
            }

            // Rebuild the whole agent sections to refresh dynamic labels
            RebuildAgentsList();
        }

        private string L(string key) => SkillsLocalization.Get(key);
    }
}
