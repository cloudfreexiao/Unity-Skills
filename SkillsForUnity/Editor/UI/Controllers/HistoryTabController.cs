using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace UnitySkills
{
    public class HistoryTabController
    {
        private const string TabUxmlPath = "Packages/com.besty.unity-skills/Editor/UI/Tabs/HistoryTab.uxml";

        private readonly VisualElement _root;
        private readonly UnitySkillsWindow _window;

        // UI references
        private Label _historyTitle;
        private Button _refreshBtn;
        private Button _clearBtn;
        private HelpBox _cacheWarning;
        private Label _activeTitle;
        private VisualElement _activeContainer;
        private Label _undoneTitle;
        private VisualElement _undoneContainer;

        public HistoryTabController(VisualElement root, UnitySkillsWindow window)
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
                Debug.LogError($"[UnitySkills] Failed to load HistoryTab UXML at path: {TabUxmlPath}");
                return;
            }

            CacheUiReferences();
            Initialize();
        }

        private void CacheUiReferences()
        {
            _historyTitle = _root.Q<Label>("history-title");
            _refreshBtn = _root.Q<Button>("refresh-btn");
            _clearBtn = _root.Q<Button>("clear-btn");
            _cacheWarning = _root.Q<HelpBox>("cache-warning");
            _activeTitle = _root.Q<Label>("active-tasks-title");
            _activeContainer = _root.Q<VisualElement>("active-tasks-container");
            _undoneTitle = _root.Q<Label>("undone-tasks-title");
            _undoneContainer = _root.Q<VisualElement>("undone-tasks-container");
        }

        private void Initialize()
        {
            // Bind refresh button
            if (_refreshBtn != null)
            {
                _refreshBtn.clicked += RefreshHistory;
            }

            // Bind clear button
            if (_clearBtn != null)
            {
                _clearBtn.clicked += ClearHistory;
            }

            // Initial refresh and rendering
            RefreshHistory();
        }

        private void RefreshHistory()
        {
            WorkflowManager.LoadHistory();
            RebuildHistoryList();
        }

        private void ClearHistory()
        {
            string confirmTitle = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "清除历史" : "Clear History";
            string confirmMsg = SkillsLocalization.Current == SkillsLocalization.Language.Chinese 
                ? "确定要清除所有历史记录吗？这也会删除磁盘上的工作流缓存快照。" 
                : "Are you sure you want to clear all history? This will also delete workflow cached snapshots on disk.";

            if (EditorUtility.DisplayDialog(confirmTitle, confirmMsg, "Yes", "No"))
            {
                WorkflowManager.ClearHistory();
                RefreshHistory();
            }
        }

        private void RebuildHistoryList()
        {
            var history = WorkflowManager.History;
            if (history == null)
            {
                // Try reloading if null
                WorkflowManager.LoadHistory();
                history = WorkflowManager.History;
            }

            // Rebuild active tasks
            if (_activeContainer != null)
            {
                _activeContainer.Clear();
                var tasks = history?.tasks;

                if (_activeTitle != null)
                {
                    int count = tasks?.Count ?? 0;
                    _activeTitle.text = (SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "活动任务" : "Active Tasks") + $" ({count})";
                }

                if (tasks == null || tasks.Count == 0)
                {
                    var label = new Label(SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "无活动任务。" : "No active tasks.");
                    label.AddToClassList("muted-label");
                    label.style.marginLeft = 6;
                    _activeContainer.Add(label);
                }
                else
                {
                    for (int i = tasks.Count - 1; i >= 0; i--)
                    {
                        var item = CreateTaskHistoryItemElement(tasks[i], true);
                        _activeContainer.Add(item);
                    }
                }
            }

            // Rebuild undone tasks
            if (_undoneContainer != null)
            {
                _undoneContainer.Clear();
                var undoneTasks = history?.undoneStack;

                if (_undoneTitle != null)
                {
                    int count = undoneTasks?.Count ?? 0;
                    _undoneTitle.text = (SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "已撤销任务" : "Undone Tasks") + $" ({count})";
                }

                if (undoneTasks == null || undoneTasks.Count == 0)
                {
                    var label = new Label(SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "无已撤销任务。" : "No undone tasks.");
                    label.AddToClassList("muted-label");
                    label.style.marginLeft = 6;
                    _undoneContainer.Add(label);
                }
                else
                {
                    for (int i = undoneTasks.Count - 1; i >= 0; i--)
                    {
                        var item = CreateTaskHistoryItemElement(undoneTasks[i], false);
                        _undoneContainer.Add(item);
                    }
                }
            }
        }

        private VisualElement CreateTaskHistoryItemElement(WorkflowTask task, bool isActive)
        {
            var card = new VisualElement();
            card.AddToClassList("card");

            // Row 1: Timestamp + Tags + Changes count + Buttons
            var headerRow = new VisualElement();
            headerRow.AddToClassList("horizontal-layout");
            headerRow.style.justifyContent = Justify.SpaceBetween;

            // Left side metadata info
            var metaContainer = new VisualElement();
            metaContainer.AddToClassList("horizontal-layout");

            var timeLabel = new Label(task.GetFormattedTime());
            timeLabel.style.fontSize = 11;
            timeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            metaContainer.Add(timeLabel);

            // Tags
            if (!string.IsNullOrEmpty(task.tag))
            {
                var tagsLabel = new Label($" [{task.tag}]");
                tagsLabel.AddToClassList("muted-label");
                tagsLabel.style.color = new Color(0.35f, 0.65f, 0.9f);
                metaContainer.Add(tagsLabel);
            }

            // Mutation changes count
            int changeCount = task.snapshots?.Count ?? 0;
            if (changeCount > 0)
            {
                string changesText = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "项修改" : "changes";
                var changesLabel = new Label($" ({changeCount} {changesText})");
                changesLabel.AddToClassList("muted-label");
                metaContainer.Add(changesLabel);
            }

            headerRow.Add(metaContainer);

            // Right side Action Buttons
            var btnContainer = new VisualElement();
            btnContainer.AddToClassList("horizontal-layout");

            if (isActive)
            {
                // Undo Button
                var undoBtn = new Button(() =>
                {
                    WorkflowManager.UndoTask(task.id);
                    RefreshHistory();
                });
                undoBtn.text = "Undo";
                undoBtn.AddToClassList("btn-mini");
                undoBtn.AddToClassList("btn-warning");
                undoBtn.style.marginRight = 4;
                undoBtn.style.width = 45;
                btnContainer.Add(undoBtn);

                // Delete Button
                var deleteBtn = new Button(() =>
                {
                    WorkflowManager.DeleteTask(task.id);
                    RefreshHistory();
                });
                deleteBtn.text = "×";
                deleteBtn.AddToClassList("btn-mini");
                deleteBtn.AddToClassList("btn-danger");
                deleteBtn.style.width = 24;
                deleteBtn.style.marginRight = 0;
                btnContainer.Add(deleteBtn);
            }
            else
            {
                // Redo Button
                var redoBtn = new Button(() =>
                {
                    WorkflowManager.RedoTask(task.id);
                    RefreshHistory();
                });
                redoBtn.text = "Redo";
                redoBtn.AddToClassList("btn-mini");
                redoBtn.AddToClassList("btn-primary");
                redoBtn.style.marginRight = 4;
                redoBtn.style.width = 45;
                btnContainer.Add(redoBtn);

                // Delete Button (undone)
                var deleteBtn = new Button(() =>
                {
                    WorkflowManager.DeleteTask(task.id);
                    RefreshHistory();
                });
                deleteBtn.text = "×";
                deleteBtn.AddToClassList("btn-mini");
                deleteBtn.AddToClassList("btn-danger");
                deleteBtn.style.width = 24;
                deleteBtn.style.marginRight = 0;
                btnContainer.Add(deleteBtn);
            }

            headerRow.Add(btnContainer);
            card.Add(headerRow);

            // Row 2: Description
            if (!string.IsNullOrEmpty(task.description))
            {
                var descLabel = new Label(task.description);
                descLabel.style.fontSize = 11;
                descLabel.style.marginTop = 4;
                descLabel.style.whiteSpace = WhiteSpace.Normal;
                descLabel.style.color = new Color(0.85f, 0.85f, 0.85f);
                card.Add(descLabel);
            }

            return card;
        }

        public void RefreshLocalization()
        {
            if (_historyTitle != null) _historyTitle.text = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "工作流历史" : "Workflow History";
            if (_refreshBtn != null) _refreshBtn.text = L("refresh");
            if (_clearBtn != null) _clearBtn.text = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "清除历史" : "Clear History";

            // Warning HelpBox
            if (_cacheWarning != null)
            {
                _cacheWarning.text = SkillsLocalization.Current == SkillsLocalization.Language.Chinese
                    ? "工作流缓存警告：撤销操作仅恢复场景状态和文件快照，不会撤销如包管理器操作或外部系统的副作用。"
                    : "Workflow Cache Warning: Undo operations restore scene hierarchies and asset snapshots. External side effects (e.g. Package Manager tasks) cannot be reverted.";
            }

            // Rebuild history records to refresh translated text ("No active tasks", "changes" label)
            RebuildHistoryList();
        }

        private string L(string key) => SkillsLocalization.Get(key);
    }
}
