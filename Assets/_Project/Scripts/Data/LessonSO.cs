using System.Collections.Generic;
using UnityEngine;

namespace BuildAR.Data
{
    [CreateAssetMenu(menuName = "BuildAR/Lesson", fileName = "NewLesson")]
    public class LessonSO : ScriptableObject
    {
        public enum Difficulty { Beginner, Intermediate, Advanced }

        /// <summary>The guided path, in order.</summary>
        public enum PathStage { IdentifyComponents, Compatibility, WorkspacePreparation, InstallationSteps, FirstBoot }

        public string lessonId;
        public string title;
        [TextArea] public string summary;
        public Difficulty difficulty = Difficulty.Beginner;
        public int estimatedMinutes = 5;
        public PathStage stage;
        [Tooltip("Sort order inside the stage.")]
        public int order;
        public ComponentCategory relatedCategory;
        [Tooltip("Component opened when the lesson starts. Falls back to the first component in relatedCategory.")]
        public string relatedComponentId;
        public List<string> prerequisiteLessonIds = new List<string>();
        public List<QuizQuestionSO> quiz = new List<QuizQuestionSO>();

        public static string StageTitle(PathStage s)
        {
            switch (s)
            {
                case PathStage.IdentifyComponents: return "Identify components";
                case PathStage.Compatibility: return "Compatibility";
                case PathStage.WorkspacePreparation: return "Workspace prep";
                case PathStage.InstallationSteps: return "Installation";
                default: return "First boot";
            }
        }
    }
}
