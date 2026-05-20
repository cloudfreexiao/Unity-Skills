using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace UnitySkills
{
    public class SkillsTabController
    {
        private const string TabUxmlPath = "Packages/com.besty.unity-skills/Editor/UI/Tabs/SkillsTab.uxml";

        private readonly VisualElement _root;
        private readonly UnitySkillsWindow _window;

        // UI references
        private Label _skillsTitle;
        private Button _refreshBtn;
        private Button _validateBtn;
        private Label _totalSkillsLabel;
        private VisualElement _skillsContainer;

        public SkillsTabController(VisualElement root, UnitySkillsWindow window)
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
                Debug.LogError($"[UnitySkills] Failed to load SkillsTab UXML at path: {TabUxmlPath}");
                return;
            }

            CacheUiReferences();
            Initialize();
        }

        private void CacheUiReferences()
        {
            _skillsTitle = _root.Q<Label>("skills-title");
            _refreshBtn = _root.Q<Button>("refresh-btn");
            _validateBtn = _root.Q<Button>("validate-btn");
            _totalSkillsLabel = _root.Q<Label>("total-skills-label");
            _skillsContainer = _root.Q<VisualElement>("skills-container");
        }

        private void Initialize()
        {
            // Bind refresh button
            if (_refreshBtn != null)
            {
                _refreshBtn.clicked += () =>
                {
                    _window.RefreshSkillsList();
                    SkillRouter.Refresh();
                    RebuildSkillsList();
                };
            }

            // Bind validate button
            if (_validateBtn != null)
            {
                _validateBtn.clicked += ValidateSkills;
            }

            // Initial build of skills list
            RebuildSkillsList();
        }

        private void ValidateSkills()
        {
            var issues = SkillRouter.ValidateMetadata();
            if (issues.Count == 0)
            {
                SkillsLogger.Log(L("metadata_validation_passed"));
            }
            else
            {
                SkillsLogger.Log(string.Format(L("metadata_validation_found"), issues.Count));
                foreach (var msg in issues)
                {
                    if (msg.StartsWith("[ERROR]"))
                        Debug.LogError($"[UnitySkills] {msg}");
                    else
                        Debug.LogWarning($"[UnitySkills] {msg}");
                }
            }
        }

        private void RebuildSkillsList()
        {
            if (_skillsContainer == null) return;
            _skillsContainer.Clear();

            var skillsDict = _window.SkillsByCategory;
            if (skillsDict == null) return;

            // Update total label
            int totalSkills = skillsDict.Values.Sum(list => list.Count);
            if (_totalSkillsLabel != null)
            {
                _totalSkillsLabel.text = string.Format(L("total_skills"), totalSkills, skillsDict.Count);
            }

            // Build categorized cards
            foreach (var kvp in skillsDict.OrderBy(k => k.Key))
            {
                var categoryCard = new VisualElement();
                categoryCard.AddToClassList("card");

                // Create Foldout
                var foldout = new Foldout();
                // Set bold foldout title with skills count
                foldout.text = $"{kvp.Key} ({kvp.Value.Count})";
                foldout.style.fontSize = 12;
                foldout.style.unityFontStyleAndWeight = FontStyle.Bold;

                // Bind foldout value persistence
                string foldoutPrefKey = $"UnitySkills_Foldout_{kvp.Key}";
                foldout.value = EditorPrefs.GetBool(foldoutPrefKey, false);
                foldout.RegisterValueChangedCallback(evt =>
                {
                    EditorPrefs.SetBool(foldoutPrefKey, evt.newValue);
                });

                // Build category inner skills list
                foreach (var skill in kvp.Value)
                {
                    var skillItem = new VisualElement();
                    skillItem.style.marginBottom = 6;
                    skillItem.style.marginTop = 2;

                    // Skill header (Name + Use button)
                    var skillHeader = new VisualElement();
                    skillHeader.AddToClassList("horizontal-layout");
                    skillHeader.style.justifyContent = Justify.SpaceBetween;

                    var skillNameLabel = new Label(skill.Name);
                    skillNameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    skillNameLabel.style.fontSize = 11;
                    skillNameLabel.style.color = new Color(0.9f, 0.9f, 0.9f);

                    var useBtn = new Button(() =>
                    {
                        string defaultJson = _window.BuildDefaultParams(skill.Method);
                        _window.SelectTestSkill(skill.Name, defaultJson);
                    });
                    useBtn.text = L("use");
                    useBtn.AddToClassList("btn-primary");
                    useBtn.AddToClassList("btn-mini");
                    useBtn.style.width = 45;
                    useBtn.style.marginRight = 0;

                    skillHeader.Add(skillNameLabel);
                    skillHeader.Add(useBtn);

                    // Skill description
                    var skillDescLabel = new Label();
                    skillDescLabel.AddToClassList("muted-label");
                    skillDescLabel.style.marginLeft = 6;
                    skillDescLabel.style.marginTop = 2;
                    skillDescLabel.style.whiteSpace = WhiteSpace.Normal;

                    var desc = SkillsLocalization.Get(skill.Name);
                    if (desc == skill.Name) desc = skill.Description;
                    skillDescLabel.text = desc;

                    skillItem.Add(skillHeader);
                    if (!string.IsNullOrEmpty(desc))
                    {
                        skillItem.Add(skillDescLabel);
                    }

                    foldout.Add(skillItem);
                }

                categoryCard.Add(foldout);
                _skillsContainer.Add(categoryCard);
            }
        }

        public void RefreshLocalization()
        {
            if (_skillsTitle != null) _skillsTitle.text = L("available_skills");
            if (_refreshBtn != null) _refreshBtn.text = L("refresh");
            if (_validateBtn != null) _validateBtn.text = L("validate");

            // Rebuild lists to refresh all dynamically translated content
            RebuildSkillsList();
        }

        private string L(string key) => SkillsLocalization.Get(key);
    }
}
