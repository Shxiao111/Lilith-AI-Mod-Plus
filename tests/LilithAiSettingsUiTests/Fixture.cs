using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Events;
using TMPro;

// Structural Unity doubles: the shipped ToggleItem has a child Toggle and a sibling label.
// These checks cover Mod binding/placement, not visual rendering or native event dispatch.
static class Program
{
    static GameObject row = null!;
    static int assertions;
    static float NextBottom() => .5f;
    static GameObject Clone<T>(float l, float r, float b, float t) => row;
    static void ResponsiveChildren(RectTransform rect) { }
    static void TextStyle(TMP_Text text, string value) => text.text = value;
    static void Place(Component child, Transform parent, float left=0, float right=1, float bottom=0, float top=1, float inset=0)
    {
        child.transform.SetParent(parent);
        child.transform.Bounds = (left, right, bottom, top);
    }
    static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }
    internal static GameObject NewOption()
    {
        var option = new GameObject();
        var circle = new GameObject(); circle.transform.SetParent(option.transform); circle.Add(new Toggle());
        var label = new GameObject(); label.transform.SetParent(option.transform); label.Add(new TMP_Text());
        label.Add(new AutoBindLocalizeStringEvent()); label.Add(new LocalizeStringEvent());
        return option;
    }
    static void Main()
    {
        foreach (var count in new[] { 3, 4 })
        {
            row = new GameObject();
            var template = NewOption(); template.transform.SetParent(row.transform);
            var root = new GameObject(); root.transform.SetParent(row.transform);
            var name = new GameObject(); name.transform.SetParent(row.transform);
            var item = new SettingToggleItem { _nameText=name.Add(new TMP_Text()), _toggleRoot=root.transform,
                _togglePrefab=template, _toggleGroup=root.Add(new ToggleGroup()) };
            row.Add(item);
            Check(template.GetComponent<Toggle>() == null, "Fixture must not have a root Toggle");
            var stale = NewOption(); stale.transform.SetParent(root.transform);
            var values = Enumerable.Range(0,count).Select(i=>"value"+i).ToArray();
            var labels = Enumerable.Range(0,count).Select(i=>"label"+i).ToArray();
            var updates = new List<string>();
            Options("Credential", values, values[1], updates.Add, labels);
            Check(!template.Active, "Native template must be hidden");
            Check(!stale.Active && stale.Destroyed, "Old options must be removed");
            Check(item._nameText.text == "Credential", "Mod row label must replace native label");
            Check(updates.Count == 0, "Initialization must not change draft");
            var options = root.transform.Children.Where(c=>!c.gameObject.Destroyed).ToArray();
            Check(options.Length == count, "All options must render");
            for (var i=0;i<count;i++)
            {
                var option=options[i].gameObject;
                var toggle=option.GetComponentInChildren<Toggle>(true);
                var text=option.GetComponentInChildren<TMP_Text>(true);
                Check(option.Active, "Configured option must be shown");
                Check(option.transform.Bounds.Left >= 0 && option.transform.Bounds.Right <= 1, "Option must stay within its row");
                Check(toggle.transform.Parent == option.transform && text.transform.Parent == option.transform,
                    "Native circle and label must remain siblings");
                Check(toggle.transform.Bounds.Right <= text.transform.Bounds.Left, "Circle must not cover label");
                Check(toggle.group == item._toggleGroup, "Options must use the cloned group");
                Check(toggle.isOn == (i==1), "Selected value must match the draft");
                Check(text.text == labels[i], "Only the Mod option label may be displayed");
                Check(option.GetComponentsInChildren<AutoBindLocalizeStringEvent>(true).All(c=>!c.enabled && c.Destroyed),
                    "Native automatic label binding must be detached");
                Check(option.GetComponentsInChildren<LocalizeStringEvent>(true).All(c=>!c.enabled && c.Destroyed && c.OnUpdateString.Fresh),
                    "Native deferred localization callbacks must be replaced");
            }
            var chosen=options[0].gameObject.GetComponentInChildren<Toggle>(true);
            chosen.onValueChanged.Invoke(false); Check(updates.Count==0, "Deselection must not change draft");
            chosen.onValueChanged.Invoke(true); Check(updates.SequenceEqual(new[]{values[0]}), "Selection must update draft exactly once");
        }
        Console.WriteLine($"AI settings option controls: {assertions} assertions passed.");
    }
    /* PRODUCTION_METHODS */
}
static class DelegateSupport { public static T ConvertDelegate<T>(Delegate d) where T:Delegate => (T)Delegate.CreateDelegate(typeof(T),d.Target,d.Method); }
class SettingToggleItem : Component
{
    public Action? OnValueChanged;
    public TMP_Text _nameText=null!;
    public Transform _toggleRoot=null!;
    public GameObject _togglePrefab=null!;
    public ToggleGroup _toggleGroup=null!;
}
namespace UnityEngine
{
    class Object
    {
        public bool Destroyed;
        public static GameObject Instantiate(GameObject template,Transform root)
        {
            var clone=Program.NewOption(); clone.SetActive(template.Active); clone.transform.SetParent(root); return clone;
        }
        public static void Destroy(Object obj) => obj.Destroyed=true;
    }
    class Component : Object
    {
        public bool enabled=true;
        public GameObject gameObject=null!;
        public Transform transform => this as Transform ?? gameObject.transform;
        public T GetComponent<T>() where T:Component => gameObject.GetComponent<T>();
    }
    class Transform : Component
    {
        public Transform? Parent;
        public List<Transform> Children=new();
        public (float Left,float Right,float Bottom,float Top) Bounds;
        public int childCount=>Children.Count;
        public Transform GetChild(int i)=>Children[i];
        public void SetParent(Transform parent) { Parent?.Children.Remove(this); Parent=parent; parent.Children.Add(this); }
    }
    class RectTransform : Transform { }
    class GameObject : Object
    {
        readonly List<Component> components=new();
        public RectTransform transform;
        public bool Active=true;
        public GameObject() { transform=new RectTransform(); Add(transform); }
        public T Add<T>(T c) where T:Component { c.gameObject=this; components.Add(c); return c; }
        public void SetActive(bool active)=>Active=active;
        public T GetComponent<T>() where T:Component => components.OfType<T>().FirstOrDefault()!;
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T:Component =>
            (includeInactive || Active ? components.OfType<T>() : Enumerable.Empty<T>()).Concat(transform.Children.SelectMany(c=>c.gameObject.GetComponentsInChildren<T>(includeInactive))).ToArray();
        public T GetComponentInChildren<T>(bool includeInactive) where T:Component => GetComponentsInChildren<T>(includeInactive).First();
    }
}
namespace UnityEngine.Events { delegate void UnityAction<T>(T value); }
namespace UnityEngine.UI
{
    class ToggleGroup : Component { }
    class Toggle : Component
    {
        public class ToggleEvent
        {
            readonly List<UnityAction<bool>> listeners=new();
            public void AddListener(UnityAction<bool> listener)=>listeners.Add(listener);
            public void Invoke(bool value) { foreach(var listener in listeners) listener(value); }
        }
        public ToggleEvent onValueChanged=new();
        public ToggleGroup? group;
        public bool isOn;
        public void SetIsOnWithoutNotify(bool value)=>isOn=value;
    }
}
namespace TMPro { class TMP_Text : Component { public string text="纸条通知纸条通知"; } }
namespace UnityEngine.Localization.Events { class UnityEventString { public bool Fresh=true; } }
namespace UnityEngine.Localization.Components
{
    class LocalizeStringEvent : Component { public UnityEventString OnUpdateString=new(){Fresh=false}; }
}
class AutoBindLocalizeStringEvent : Component { }
