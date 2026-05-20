using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace UnitySkills
{
    public class ServerTabController
    {
        private const string TabUxmlPath = "Packages/com.besty.unity-skills/Editor/UI/Tabs/ServerTab.uxml";

        private readonly VisualElement _root;
        private readonly UnitySkillsWindow _window;

        // UI references
        private VisualElement _statusDot;
        private Label _statusLabel;
        private Button _serverToggleBtn;

        private VisualElement _connCard;
        private Label _connTitle;
        private Label _portLabel;
        private Label _idLabel;
        private TextField _urlField;
        private Button _urlCopyBtn;

        private VisualElement _statsCard;
        private Label _statsTitle;
        private Label _queueLabel;
        private Label _processedLabel;
        private Button _statsResetBtn;

        private Label _settingsTitle;
        private Toggle _autoStartToggle;
        private Label _autoStartHint;
        private Label _portSelectLabel;
        private DropdownField _portDropdown;
        private Label _timeoutLabel;
        private IntegerField _timeoutField;
        private Label _timeoutUnit;
        private Label _keepaliveLabel;
        private IntegerField _keepaliveField;
        private Label _keepaliveUnit;
        private Label _keepaliveHint;
        private Label _loglevelLabel;
        private DropdownField _logDropdown;
        private Toggle _confirmToggle;
        private Label _confirmHint;

        private Label _testTitle;
        private TextField _testNameField;
        private Label _testParamsLabel;
        private TextField _testParamsField;
        private Button _testExecBtn;
        private VisualElement _testResultContainer;
        private Label _resultTitle;
        private TextField _testResultField;

        private bool? _lastServerRunningState;

        public ServerTabController(VisualElement root, UnitySkillsWindow window)
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
                Debug.LogError($"[UnitySkills] Failed to load ServerTab UXML at path: {TabUxmlPath}");
                return;
            }

            CacheUiReferences();
            Initialize();
        }

        private void CacheUiReferences()
        {
            _statusDot = _root.Q<VisualElement>("status-dot");
            _statusLabel = _root.Q<Label>("status-label");
            _serverToggleBtn = _root.Q<Button>("server-toggle-btn");

            _connCard = _root.Q<VisualElement>("conn-card");
            _connTitle = _root.Q<Label>("conn-title");
            _portLabel = _root.Q<Label>("port-label");
            _idLabel = _root.Q<Label>("id-label");
            _urlField = _root.Q<TextField>("url-field");
            _urlCopyBtn = _root.Q<Button>("url-copy-btn");

            _statsCard = _root.Q<VisualElement>("stats-card");
            _statsTitle = _root.Q<Label>("stats-title");
            _queueLabel = _root.Q<Label>("queue-label");
            _processedLabel = _root.Q<Label>("processed-label");
            _statsResetBtn = _root.Q<Button>("stats-reset-btn");

            _settingsTitle = _root.Q<Label>("settings-title");
            _autoStartToggle = _root.Q<Toggle>("autostart-toggle");
            _autoStartHint = _root.Q<Label>("autostart-hint");
            _portSelectLabel = _root.Q<Label>("port-select-label");
            _portDropdown = _root.Q<DropdownField>("port-dropdown");
            _timeoutLabel = _root.Q<Label>("timeout-label");
            _timeoutField = _root.Q<IntegerField>("timeout-field");
            _timeoutUnit = _root.Q<Label>("timeout-unit");
            _keepaliveLabel = _root.Q<Label>("keepalive-label");
            _keepaliveField = _root.Q<IntegerField>("keepalive-field");
            _keepaliveUnit = _root.Q<Label>("keepalive-unit");
            _keepaliveHint = _root.Q<Label>("keepalive-hint");
            _loglevelLabel = _root.Q<Label>("loglevel-label");
            _logDropdown = _root.Q<DropdownField>("loglevel-dropdown");
            _confirmToggle = _root.Q<Toggle>("confirm-toggle");
            _confirmHint = _root.Q<Label>("confirm-hint");

            _testTitle = _root.Q<Label>("test-title");
            _testNameField = _root.Q<TextField>("test-name-field");
            _testParamsLabel = _root.Q<Label>("test-params-label");
            _testParamsField = _root.Q<TextField>("test-params-field");
            _testExecBtn = _root.Q<Button>("test-exec-btn");
            _testResultContainer = _root.Q<VisualElement>("test-result-container");
            _resultTitle = _root.Q<Label>("result-title");
            _testResultField = _root.Q<TextField>("test-result-field");
        }

        private void Initialize()
        {
            // Bind server toggle button
            if (_serverToggleBtn != null)
            {
                _serverToggleBtn.clicked += ToggleServer;
            }

            // Bind copy URL button
            if (_urlCopyBtn != null)
            {
                _urlCopyBtn.clicked += () =>
                {
                    EditorGUIUtility.systemCopyBuffer = RegistryService.InstanceId;
                };
            }

            // Bind stats reset button
            if (_statsResetBtn != null)
            {
                _statsResetBtn.clicked += () =>
                {
                    SkillsHttpServer.ResetStatistics();
                    UpdateLiveDataInternal(true);
                };
            }

            // Bind autostart toggle
            if (_autoStartToggle != null)
            {
                _autoStartToggle.value = SkillsHttpServer.AutoStart;
                _autoStartToggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue != SkillsHttpServer.AutoStart)
                    {
                        SkillsHttpServer.AutoStart = evt.newValue;
                    }
                });
            }

            // Bind Port Dropdown
            if (_portDropdown != null)
            {
                _portDropdown.choices = new List<string> { "Auto", "8090", "8091", "8092", "8093", "8094", "8095", "8096", "8097", "8098", "8099", "8100" };
                int currentPort = SkillsHttpServer.PreferredPort;
                int idx = (currentPort == 0) ? 0 : currentPort - 8089;
                if (idx < 0 || idx >= _portDropdown.choices.Count) idx = 0;
                _portDropdown.value = _portDropdown.choices[idx];

                _portDropdown.RegisterValueChangedCallback(evt =>
                {
                    int newIdx = _portDropdown.choices.IndexOf(evt.newValue);
                    int targetPort = (newIdx <= 0) ? 0 : 8089 + newIdx;
                    if (targetPort != SkillsHttpServer.PreferredPort)
                    {
                        SkillsHttpServer.PreferredPort = targetPort;
                    }
                });
            }

            // Bind Request Timeout
            if (_timeoutField != null)
            {
                _timeoutField.value = SkillsHttpServer.RequestTimeoutMinutes;
                _timeoutField.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue != SkillsHttpServer.RequestTimeoutMinutes)
                    {
                        SkillsHttpServer.RequestTimeoutMinutes = evt.newValue;
                    }
                });
            }

            // Bind KeepAlive Interval
            if (_keepaliveField != null)
            {
                _keepaliveField.value = SkillsHttpServer.KeepAliveIntervalSeconds;
                _keepaliveField.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue != SkillsHttpServer.KeepAliveIntervalSeconds)
                    {
                        SkillsHttpServer.KeepAliveIntervalSeconds = evt.newValue;
                    }
                });
            }

            // Bind Log Level Dropdown
            if (_logDropdown != null)
            {
                _logDropdown.choices = new List<string> { "Off", "Error", "Warning", "Info", "Agent", "Verbose" };
                _logDropdown.value = _logDropdown.choices[(int)SkillsLogger.Level];
                _logDropdown.RegisterValueChangedCallback(evt =>
                {
                    int newLevelIdx = _logDropdown.choices.IndexOf(evt.newValue);
                    if (newLevelIdx != (int)SkillsLogger.Level)
                    {
                        SkillsLogger.Level = (LogLevel)newLevelIdx;
                    }
                });
            }

            // Bind Require Confirmation Toggle
            if (_confirmToggle != null)
            {
                _confirmToggle.value = ConfirmationTokenService.RequireConfirmation;
                _confirmToggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue != ConfirmationTokenService.RequireConfirmation)
                    {
                        ConfirmationTokenService.RequireConfirmation = evt.newValue;
                    }
                });
            }

            // Bind Execute Skill Button
            if (_testExecBtn != null)
            {
                _testExecBtn.clicked += ExecuteTestSkill;
            }

            // Hide test result by default
            if (_testResultContainer != null)
            {
                _testResultContainer.style.display = DisplayStyle.None;
            }

            // Perform initial live data update
            UpdateLiveDataInternal(true);
        }

        private void ToggleServer()
        {
            bool isRunning = SkillsHttpServer.IsRunning;
            if (isRunning)
            {
                SkillsHttpServer.StopPermanent();
            }
            else
            {
                SkillsHttpServer.Start(SkillsHttpServer.PreferredPort);
            }
            UpdateLiveDataInternal(true);
        }

        private void ExecuteTestSkill()
        {
            if (_testNameField == null || _testParamsField == null) return;

            string skillName = _testNameField.value;
            string jsonParams = _testParamsField.value;

            string result = SkillRouter.Execute(skillName, jsonParams);

            if (_testResultField != null && _testResultContainer != null)
            {
                _testResultField.value = result;
                _testResultContainer.style.display = string.IsNullOrEmpty(result) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        public void SetTestSkill(string name, string paramsJson)
        {
            if (_testNameField != null) _testNameField.value = name;
            if (_testParamsField != null) _testParamsField.value = paramsJson;
            if (_testResultField != null) _testResultField.value = "";
            if (_testResultContainer != null) _testResultContainer.style.display = DisplayStyle.None;
        }

        public void UpdateLiveData()
        {
            UpdateLiveDataInternal(false);
        }

        private void UpdateLiveDataInternal(bool force)
        {
            bool isRunning = SkillsHttpServer.IsRunning;

            // Only update layout visibility when state changes
            if (_lastServerRunningState != isRunning || force)
            {
                _lastServerRunningState = isRunning;

                // Update Status Dot and text
                if (_statusDot != null)
                {
                    if (isRunning)
                    {
                        _statusDot.RemoveFromClassList("error");
                        _statusDot.AddToClassList("success");
                    }
                    else
                    {
                        _statusDot.RemoveFromClassList("success");
                        _statusDot.AddToClassList("error");
                    }
                }

                if (_statusLabel != null)
                {
                    _statusLabel.text = isRunning ? L("server_running") : L("server_stopped");
                }

                // Update server toggle button text & style
                if (_serverToggleBtn != null)
                {
                    _serverToggleBtn.text = isRunning ? L("stop_server") : L("start_server");
                    if (isRunning)
                    {
                        _serverToggleBtn.RemoveFromClassList("btn-start");
                        _serverToggleBtn.AddToClassList("btn-stop");
                    }
                    else
                    {
                        _serverToggleBtn.RemoveFromClassList("btn-stop");
                        _serverToggleBtn.AddToClassList("btn-start");
                    }
                }

                // Cards visibility
                if (_connCard != null) _connCard.style.display = isRunning ? DisplayStyle.Flex : DisplayStyle.None;
                if (_statsCard != null) _statsCard.style.display = isRunning ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Update live labels if running
            if (isRunning)
            {
                string portLabelStr = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "端口" : "Port";
                if (_portLabel != null) _portLabel.text = $"{portLabelStr}: {SkillsHttpServer.Port}";
                if (_idLabel != null) _idLabel.text = $"ID: {RegistryService.InstanceId}";
                if (_urlField != null) _urlField.value = SkillsHttpServer.Url;

                if (_queueLabel != null) _queueLabel.text = $"{L("queued_requests")}: {SkillsHttpServer.QueuedRequests}";
                if (_processedLabel != null) _processedLabel.text = $"{L("total_processed")}: {SkillsHttpServer.TotalProcessed}";
            }
        }

        public void RefreshLocalization()
        {
            // Cards text
            if (_connTitle != null) _connTitle.text = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "连接信息" : "Connection Info";
            if (_urlCopyBtn != null) _urlCopyBtn.text = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "复制" : "Copy";
            if (_statsTitle != null) _statsTitle.text = L("server_stats");
            if (_statsResetBtn != null) _statsResetBtn.text = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "重置" : "Reset";
            if (_settingsTitle != null) _settingsTitle.text = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "设置" : "Settings";

            // Settings labels
            if (_autoStartToggle != null) _autoStartToggle.label = L("auto_restart");
            if (_autoStartHint != null) _autoStartHint.text = L("auto_restart_hint");
            
            string portSelectStr = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "启动端口:" : "Port:";
            if (_portSelectLabel != null) _portSelectLabel.text = portSelectStr;

            string timeoutStr = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "请求超时:" : "Timeout:";
            if (_timeoutLabel != null) _timeoutLabel.text = timeoutStr;
            if (_timeoutUnit != null) _timeoutUnit.text = L("timeout_unit");

            string keepaliveStr = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "保活间隔:" : "KeepAlive:";
            if (_keepaliveLabel != null) _keepaliveLabel.text = keepaliveStr;
            if (_keepaliveUnit != null) _keepaliveUnit.text = L("keepalive_unit");
            if (_keepaliveHint != null) _keepaliveHint.text = L("keepalive_hint");

            string loglevelStr = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "日志级别:" : "Log Level:";
            if (_loglevelLabel != null) _loglevelLabel.text = loglevelStr;

            string confirmStr = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "高风险技能二次确认" : "Confirm High-Risk Skills";
            if (_confirmToggle != null) _confirmToggle.label = confirmStr;
            if (_confirmHint != null)
            {
                _confirmHint.text = SkillsLocalization.Current == SkillsLocalization.Language.Chinese
                    ? "开启后：删除类/RiskLevel=high 技能首次调用返回 _confirm token + dryRun 预览，5 分钟内带 token 重试才执行。默认关闭，全自动场景无感。"
                    : "When ON: delete / high-risk skills first return a _confirm token + dryRun preview; re-call within 5 min with the token to execute. OFF by default — fully automated flows are unaffected.";
            }

            // Test section
            if (_testTitle != null) _testTitle.text = L("test_skill");
            if (_testNameField != null) _testNameField.label = L("skill_name");
            if (_testParamsLabel != null) _testParamsLabel.text = L("parameters_json") + ":";
            if (_testExecBtn != null) _testExecBtn.text = L("execute_skill");
            if (_resultTitle != null) _resultTitle.text = L("result") + ":";

            // Force update dynamic status text
            UpdateLiveDataInternal(true);
        }

        private string L(string key) => SkillsLocalization.Get(key);
    }
}
