using UnityEngine;
using UnityEngine.UIElements;

namespace BuildAR.UI
{
    /// <summary>
    /// Mobile-style scrolling for vertical ScrollViews: elastic bounce at the ends and a slim indicator on the
    /// right edge that fades in while scrolling. It also flashes once when a screen opens, to show it can scroll.
    /// </summary>
    public static class ScrollFx
    {
        const string AttachedClass = "scroll-fx";
        const float MinThumb = 16f;
        const float MaxThumb = 48f;
        const int HideAfterMs = 900;

        public static void Attach(ScrollView view)
        {
            if (view.mode == ScrollViewMode.Horizontal || view.ClassListContains(AttachedClass)) return;
            view.AddToClassList(AttachedClass);
            view.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;

            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.AddToClassList("scroll-indicator");
            var thumb = new VisualElement { pickingMode = PickingMode.Ignore };
            thumb.AddToClassList("scroll-indicator__thumb");
            track.Add(thumb);
            view.hierarchy.Add(track);

            float lastOffset = float.NaN;
            bool flashed = false;
            IVisualElementScheduledItem hide = null;

            void Show()
            {
                track.AddToClassList("scroll-indicator--visible");
                hide?.Pause();
                hide = track.schedule.Execute(() => track.RemoveFromClassList("scroll-indicator--visible")).StartingIn(HideAfterMs);
            }

            track.schedule.Execute(() =>
            {
                float viewport = view.contentViewport.layout.height;
                float content = view.contentContainer.layout.height;
                float trackHeight = track.layout.height;
                if (float.IsNaN(viewport) || float.IsNaN(content) || float.IsNaN(trackHeight) || trackHeight <= 0f) return;

                bool scrollable = content > viewport + 1f;
                thumb.style.display = scrollable ? DisplayStyle.Flex : DisplayStyle.None;
                if (!scrollable) { lastOffset = view.scrollOffset.y; return; }

                float thumbHeight = Mathf.Clamp(trackHeight * viewport / content, MinThumb, Mathf.Min(MaxThumb, trackHeight));
                float t = Mathf.Clamp01(view.scrollOffset.y / (content - viewport));
                thumb.style.height = thumbHeight;
                thumb.style.top = (trackHeight - thumbHeight) * t;

                if (!flashed) { flashed = true; Show(); }
                else if (!Mathf.Approximately(lastOffset, view.scrollOffset.y)) Show();
                lastOffset = view.scrollOffset.y;
            }).Every(33);
        }
    }
}
