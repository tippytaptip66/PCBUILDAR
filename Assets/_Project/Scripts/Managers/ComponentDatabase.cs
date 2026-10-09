using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BuildAR.Data;

namespace BuildAR.Managers
{
    /// <summary>
    /// Loads all content from Resources folders at startup:
    ///   Resources/Components      ComponentDefinitionSO
    ///   Resources/Lessons         LessonSO
    ///   Resources/AssemblyGuides  AssemblyGuideSO
    /// (Assets/_Project/Data/Resources/... in this project.)
    /// </summary>
    public class ComponentDatabase : MonoBehaviour
    {
        public static ComponentDatabase Instance { get; private set; }

        [SerializeField] private string defaultGuideId = "first_build";

        private Dictionary<string, ComponentDefinitionSO> _byId;
        private Dictionary<string, ComponentDefinitionSO> _byMlLabel;
        private List<ComponentDefinitionSO> _all;
        private List<LessonSO> _lessons;
        private List<AssemblyGuideSO> _guides;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _all = Resources.LoadAll<ComponentDefinitionSO>("Components").ToList();
            _byId = new Dictionary<string, ComponentDefinitionSO>();
            foreach (var c in _all)
            {
                if (string.IsNullOrEmpty(c.id) || _byId.ContainsKey(c.id))
                    Debug.LogWarning($"BuildAR: component asset '{c.name}' has an empty or duplicate id.", c);
                else _byId.Add(c.id, c);
            }

            BuildMlLabelIndex();

            _lessons = Resources.LoadAll<LessonSO>("Lessons").OrderBy(l => l.stage).ThenBy(l => l.order).ToList();
            _guides = Resources.LoadAll<AssemblyGuideSO>("AssemblyGuides").ToList();
            Debug.Log($"BuildAR: loaded {_all.Count} components, {_lessons.Count} lessons, {_guides.Count} assembly guides.");
        }

        public IReadOnlyList<ComponentDefinitionSO> All => _all;
        public IReadOnlyList<LessonSO> Lessons => _lessons;

        public ComponentDefinitionSO GetById(string id) => id != null && _byId.TryGetValue(id, out var def) ? def : null;
        public IEnumerable<ComponentDefinitionSO> GetByCategory(ComponentCategory category) => _all.Where(c => c.category == category);

        /// <summary>
        /// The component a detection belongs to. Outside models use their own class names ("ram_stick",
        /// "graphics card"), so the name is normalised and mapped through MlLabelMap first. Returns null for a
        /// class this app has no component for, which the scanner then ignores.
        /// </summary>
        public ComponentDefinitionSO GetByMlLabel(string label)
        {
            if (string.IsNullOrEmpty(label) || _byMlLabel == null) return null;
            return _byMlLabel.TryGetValue(MlLabelMap.Resolve(label), out var def) ? def : null;
        }

        void BuildMlLabelIndex()
        {
            _byMlLabel = new Dictionary<string, ComponentDefinitionSO>();
            foreach (var c in _all)
            {
                Add(MlLabelMap.Normalise(c.mlLabel), c);
                if (c.mlAliases == null) continue;
                foreach (var alias in c.mlAliases) Add(MlLabelMap.Resolve(alias), c);
            }

            void Add(string key, ComponentDefinitionSO def)
            {
                if (key.Length > 0 && !_byMlLabel.ContainsKey(key)) _byMlLabel.Add(key, def);
            }
        }

        public AssemblyGuideSO GetGuide(string guideId) => _guides.FirstOrDefault(g => g.guideId == guideId);
        public AssemblyGuideSO DefaultGuide => GetGuide(defaultGuideId) ?? _guides.FirstOrDefault();

        public ComponentDefinitionSO ComponentForLesson(LessonSO lesson) =>
            GetById(lesson.relatedComponentId) ?? GetByCategory(lesson.relatedCategory).FirstOrDefault();
    }
}
