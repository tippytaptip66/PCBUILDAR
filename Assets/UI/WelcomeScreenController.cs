using UnityEngine;
using UnityEngine.UIElements;
using BuildAR.Managers;
using BuildAR.UI;

/// <summary>Three-slide onboarding. Attach next to a UIDocument (Source Asset = WelcomeScreen.uxml).</summary>
[RequireComponent(typeof(UIDocument))]
public class WelcomeScreenController : MonoBehaviour
{
    struct Slide
    {
        public string icon, kicker, title, body, tag1, tag2, tag3;
    }

    static readonly Slide[] Slides =
    {
        new Slide { icon = "cpu", kicker = "LEARN", title = "Learn what's inside a computer",
            body = "Short, visual lessons explain every part — CPU, RAM, motherboard, storage and more.",
            tag1 = "HEAT SPREADER", tag2 = "PINS", tag3 = "NOTCH" },
        new Slide { icon = "scan", kicker = "IDENTIFY", title = "Scan real parts with AR",
            body = "Point your camera at a component to recognize it and see its ports and connectors.",
            tag1 = "PCIe x16", tag2 = "8-PIN POWER", tag3 = "DISPLAY OUT" },
        new Slide { icon = "shield", kicker = "ASSEMBLE", title = "Build safely, step by step",
            body = "Always unplug the PC, touch the metal case to discharge static, and hold parts by their edges.",
            tag1 = "UNPLUG FIRST", tag2 = "ANTI-STATIC", tag3 = "HOLD EDGES" },
    };

    UIDocument _doc;
    int _index;
    IVisualElementScheduledItem _pulse;

    void OnEnable()
    {
        _doc = GetComponent<UIDocument>();
        var root = _doc.rootVisualElement;
        UIUtil.PrepareScreen(root);

        UIUtil.OnClick(root, "btn-skip", Finish);
        UIUtil.OnClick(root, "btn-next", Next);

        var logo = UIUtil.BrandLogo;
        var mark = root.Q("brand-mark");
        if (logo != null && mark != null)
        {
            mark.Clear();
            mark.style.backgroundImage = new StyleBackground(logo);
            mark.AddToClassList("brand-mark--logo");
        }

        var ring = root.Q("art-ring-inner");
        _pulse = root.schedule.Execute(() => ring.ToggleInClassList("pulse")).Every(1200);

        _index = 0;
        Render();
    }

    void OnDisable() => _pulse?.Pause();

    void Next()
    {
        if (_index >= Slides.Length - 1) { Finish(); return; }
        _index++;
        Render();
    }

    void Render()
    {
        var root = _doc.rootVisualElement;
        var s = Slides[_index];
        root.Q<LineIcon>("slide-icon").icon = s.icon;
        root.Q<Label>("slide-kicker").text = s.kicker;
        root.Q<Label>("slide-title").text = s.title;
        root.Q<Label>("slide-body").text = s.body;
        root.Q<Label>("tag-1").text = s.tag1;
        root.Q<Label>("tag-2").text = s.tag2;
        root.Q<Label>("tag-3").text = s.tag3;
        root.Q<Label>("btn-next-label").text = _index == Slides.Length - 1 ? "Get started" : "Next";

        int i = 0;
        root.Q("dots").Query(className: "dot").ForEach(d => d.EnableInClassList("active", i++ == _index));
    }

    void Finish()
    {
        var progress = ProgressManager.Instance;
        if (progress != null && _index == Slides.Length - 1)
            progress.RecordSafetyTipViewed(); // they read the safety slide

        if (progress != null && !progress.Data.profileSet)
        {
            ProfileSetup.Show(_doc.rootVisualElement, firstTime: true, onDone: EnterApp);
            return;
        }
        EnterApp();
    }

    static void EnterApp()
    {
        ProgressManager.Instance?.MarkOnboardingComplete();
        GameManager.Instance?.ShowScreen(AppScreen.Home);
    }
}
