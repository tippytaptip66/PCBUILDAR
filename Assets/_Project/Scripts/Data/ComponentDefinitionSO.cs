using System.Collections.Generic;
using UnityEngine;

namespace BuildAR.Data
{
    [CreateAssetMenu(menuName = "BuildAR/Component Definition", fileName = "NewComponent")]
    public class ComponentDefinitionSO : ScriptableObject
    {
        [Header("Identity")]
        public string id;
        public string displayName;
        public ComponentCategory category;
        public Sprite icon;

        [Tooltip("Class name this component has in the ML model's labels file (e.g. \"gpu\"). Leave empty if it can't be scanned.")]
        public string mlLabel;

        [Tooltip("Other class names that mean this component, for models trained on someone else's data " +
                 "(e.g. \"ram_stick\", \"graphics card\"). Case, spaces and hyphens don't matter. " +
                 "Common ones are already built in — see MlLabelMap.cs.")]
        public List<string> mlAliases = new List<string>();

        [Tooltip("Short spec chips shown on the scanner card, e.g. \"PCIe x16\", \"DDR5\".")]
        public List<string> specChips = new List<string>();

        [Header("Learning content")]
        [TextArea] public string shortDescription;
        [TextArea] public string whatItDoes;
        public List<string> keyFacts = new List<string>();
        [TextArea] public List<string> installationSteps = new List<string>();
        [TextArea] public string safetyTip;

        [Header("3D / AR")]
        [Tooltip("Prefab shown in the 3D viewer and spawned in Virtual Assembly. Pivot at the geometric centre, 1 unit = 1 metre.")]
        public GameObject model3DPrefab;

        [Tooltip("Extra rotation applied when Import Component Models builds this part's prefab. The importer turns " +
                 "the model to match the slot it goes into; use this to correct it, usually in steps of 90.")]
        public Vector3 modelRotationEuler;

        [Tooltip("Fans on the model file for Import Component Models to cut out and spin, each with an RGB ring. " +
                 "Given in the model file's own space — see ModelFan.")]
        public List<ModelFan> modelFans = new List<ModelFan>();

        [Tooltip("RGB light bars Import Component Models adds to the model, in the model file's own space.")]
        public List<ModelLightBar> modelLightBars = new List<ModelLightBar>();

        [Tooltip("I/O panels (rear ports, top buttons) Import Component Models builds onto a case model, in the model " +
                 "file's own space — see ModelIoPanel.")]
        public List<ModelIoPanel> modelPanels = new List<ModelIoPanel>();

        [Tooltip("Labeled features (socket, slots, ports…) the 3D viewer marks on the model and lists in the Parts " +
                 "tab, in the model file's own space — see ModelLabel. Placeholder models carry their own instead.")]
        public List<ModelLabel> modelLabels = new List<ModelLabel>();

        public List<ConnectionPoint> connectionPoints = new List<ConnectionPoint>();

        [Tooltip("Labels drawn over the real component in the AR scanner, positioned inside the detection box.")]
        public List<ARLabelDefinition> arLabels = new List<ARLabelDefinition>();

        [Header("Compatibility")]
        public List<CompatibilityRule> compatibilityRules = new List<CompatibilityRule>();

        [Header("Quiz")]
        public List<QuizQuestionSO> quizQuestions = new List<QuizQuestionSO>();
    }
}
