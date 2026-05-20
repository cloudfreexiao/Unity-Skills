using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace UnitySkills
{
    /// <summary>
    /// Unity Editor Window for UnitySkills REST API control.
    /// Re-architected with UI Toolkit (UXML + USS) for Unity 2022.3+.
    /// </summary>
    public class UnitySkillsWindow : EditorWindow
    {
        private const string UxmlPath = "Packages/com.besty.unity-skills/Editor/UI/UnitySkillsWindow.uxml";
        private const string UssPath = "Packages/com.besty.unity-skills/Editor/UI/UnitySkillsWindow.uss";

        [SerializeField]
        private int _selectedTab = 0;

        private Dictionary<string, List<SkillInfo>> _skillsByCategory;
        private ServerTabController _serverController;
        private SkillsTabController _skillsController;
        private AIConfigTabController _configController;
        private HistoryTabController _historyController;

        private VisualElement[] _tabContents;
        private Button[] _tabButtons;
        private VisualElement[] _tabUnderlines;
        private Button _langToggleBtn;

        public class SkillInfo
        {
            public string Name;
            public string Description;
            public MethodInfo Method;
        }

        public Dictionary<string, List<SkillInfo>> SkillsByCategory => _skillsByCategory;

        [MenuItem("Window/UnitySkills")]
        public static void ShowWindow()
        {
            var window = GetWindow<UnitySkillsWindow>("UnitySkills");
            window.minSize = new Vector2(300, 300);
        }

        private void OnEnable()
        {
            // Initial load of skills list
            RefreshSkillsList();
        }

        public void CreateGUI()
        {
            // Load and apply StyleSheet (USS) first, so variables in UXML inline styles can be resolved during cloning
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (uss != null)
            {
                rootVisualElement.styleSheets.Add(uss);
            }
            else
            {
                Debug.LogWarning($"[UnitySkills] Failed to load USS at path: {UssPath}");
            }

            // Load and clone VisualTreeAsset (UXML)
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (uxml == null)
            {
                Debug.LogError($"[UnitySkills] Failed to load UXML at path: {UxmlPath}");
                return;
            }
            uxml.CloneTree(rootVisualElement);

            // Cache UI references
            CacheUiReferences();

            // Initialize Tab controllers
            _serverController = new ServerTabController(_tabContents[0], this);
            _skillsController = new SkillsTabController(_tabContents[1], this);
            _configController = new AIConfigTabController(_tabContents[2], this);
            _historyController = new HistoryTabController(_tabContents[3], this);

            // Setup Tab Buttons click events
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                int index = i;
                _tabButtons[i].clicked += () => SwitchTab(index);
            }

            // Setup Language Toggle Button click event
            if (_langToggleBtn != null)
            {
                _langToggleBtn.clicked += ToggleLanguage;
            }

            // Switch to preserved tab
            SwitchTab(_selectedTab);

            // Refresh text based on active locale
            RefreshLocalization();

            // Schedule live server monitoring (500ms intervals)
            rootVisualElement.schedule.Execute(OnLiveDataUpdate).Every(500).StartingIn(0);
        }

        private void CacheUiReferences()
        {
            _tabButtons = new Button[4];
            _tabButtons[0] = rootVisualElement.Q<Button>("tab-btn-0");
            _tabButtons[1] = rootVisualElement.Q<Button>("tab-btn-1");
            _tabButtons[2] = rootVisualElement.Q<Button>("tab-btn-2");
            _tabButtons[3] = rootVisualElement.Q<Button>("tab-btn-3");

            _tabContents = new VisualElement[4];
            _tabContents[0] = rootVisualElement.Q<VisualElement>("tab-content-0");
            _tabContents[1] = rootVisualElement.Q<VisualElement>("tab-content-1");
            _tabContents[2] = rootVisualElement.Q<VisualElement>("tab-content-2");
            _tabContents[3] = rootVisualElement.Q<VisualElement>("tab-content-3");

            _tabUnderlines = new VisualElement[4];
            _tabUnderlines[0] = rootVisualElement.Q<VisualElement>("tab-underline-0");
            _tabUnderlines[1] = rootVisualElement.Q<VisualElement>("tab-underline-1");
            _tabUnderlines[2] = rootVisualElement.Q<VisualElement>("tab-underline-2");
            _tabUnderlines[3] = rootVisualElement.Q<VisualElement>("tab-underline-3");

            _langToggleBtn = rootVisualElement.Q<Button>("lang-toggle");
        }

        private void SwitchTab(int index)
        {
            _selectedTab = index;

            for (int i = 0; i < 4; i++)
            {
                if (_tabContents[i] != null)
                {
                    _tabContents[i].style.display = (i == index) ? DisplayStyle.Flex : DisplayStyle.None;
                }

                if (_tabButtons[i] != null)
                {
                    if (i == index)
                    {
                        _tabButtons[i].AddToClassList("tab-active");
                    }
                    else
                    {
                        _tabButtons[i].RemoveFromClassList("tab-active");
                    }
                }

                // Underline is an independent VisualElement (sibling of the Button
                // inside .tab-wrap), not the Button's own border. Toggling its class
                // applies immediately because no built-in Unity Button pseudo-style
                // competes for the property.
                if (_tabUnderlines != null && _tabUnderlines[i] != null)
                {
                    if (i == index)
                    {
                        _tabUnderlines[i].AddToClassList("active");
                    }
                    else
                    {
                        _tabUnderlines[i].RemoveFromClassList("active");
                    }
                }
            }

            // Drop focus from the clicked tab button so its :focus pseudo-state
            // doesn't keep the idle text color applied over .tab-active.
            if (index >= 0 && index < _tabButtons.Length && _tabButtons[index] != null)
            {
                _tabButtons[index].Blur();
            }
        }

        public void SelectTestSkill(string skillName, string defaultParams)
        {
            _serverController?.SetTestSkill(skillName, defaultParams);
            SwitchTab(0);
        }

        private void ToggleLanguage()
        {
            SkillsLocalization.Current = SkillsLocalization.Current == SkillsLocalization.Language.English
                ? SkillsLocalization.Language.Chinese
                : SkillsLocalization.Language.English;

            RefreshLocalization();
        }

        private void RefreshLocalization()
        {
            // Refresh main window buttons
            if (_tabButtons[0] != null) _tabButtons[0].text = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "服务器" : "Server";
            if (_tabButtons[1] != null) _tabButtons[1].text = "Skills";
            if (_tabButtons[2] != null) _tabButtons[2].text = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "AI配置" : "AI Config";
            if (_tabButtons[3] != null) _tabButtons[3].text = SkillsLocalization.Current == SkillsLocalization.Language.Chinese ? "历史记录" : "History";

            if (_langToggleBtn != null)
            {
                _langToggleBtn.text = SkillsLocalization.Current == SkillsLocalization.Language.English ? "EN / 中文" : "中文 / EN";
            }

            // Propagate language refresh to child controllers
            _serverController?.RefreshLocalization();
            _skillsController?.RefreshLocalization();
            _configController?.RefreshLocalization();
            _historyController?.RefreshLocalization();
        }

        private void OnLiveDataUpdate()
        {
            _serverController?.UpdateLiveData();
        }

        public void RefreshSkillsList()
        {
            _skillsByCategory = new Dictionary<string, List<SkillInfo>>();

            var allTypes = SkillsCommon.GetAllLoadedTypes();

            foreach (var type in allTypes)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    UnitySkillAttribute attr;
                    try { attr = method.GetCustomAttribute<UnitySkillAttribute>(); }
                    catch { continue; }
                    if (attr != null)
                    {
                        var category = type.Name.Replace("Skills", "");
                        if (!_skillsByCategory.ContainsKey(category))
                            _skillsByCategory[category] = new List<SkillInfo>();

                        _skillsByCategory[category].Add(new SkillInfo
                        {
                            Name = attr.Name ?? method.Name,
                            Description = attr.Description ?? "",
                            Method = method
                        });
                    }
                }
            }
        }

        public string BuildDefaultParams(MethodInfo method)
        {
            var ps = method.GetParameters();
            if (ps.Length == 0) return "{}";

            var parts = ps.Select(p =>
            {
                var defaultVal = p.HasDefaultValue ? p.DefaultValue : GetDefaultForType(p.ParameterType);
                var valStr = defaultVal == null ? "null" :
                    p.ParameterType == typeof(string) ? $"\"{defaultVal}\"" :
                    defaultVal.ToString().ToLower();
                return $"\"{p.Name}\": {valStr}";
            });

            return "{\n  " + string.Join(",\n  ", parts) + "\n}";
        }

        private object GetDefaultForType(System.Type t)
        {
            if (t == typeof(string)) return "";
            if (t == typeof(int) || t == typeof(float)) return 0;
            if (t == typeof(bool)) return false;
            return null;
        }
    }
}
