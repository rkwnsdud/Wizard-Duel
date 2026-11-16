#if UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHealthTests
{
    static void Invoke(Component component, string method, params object[] args) =>
        component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, args);

    [Test]
    public void ReceivedServerValuesReplaceTheDisplayAndUpdateTheHealthBar()
    {
        var root = new GameObject("Health display test");
        try
        {
            var health = root.AddComponent<PlayerHealth>();
            Invoke(health, "Awake");
            var player = root.AddComponent<DuelPlayer>();
            Invoke(player, "Awake");
            var barObject = new GameObject("Bar");
            barObject.transform.SetParent(root.transform);
            var bar = barObject.AddComponent<HealthBarBillboard>();
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            fill.transform.SetParent(barObject.transform);
            var trail = new GameObject("Trail", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            trail.transform.SetParent(barObject.transform);
            var serialized = new SerializedObject(bar);
            serialized.FindProperty("fillImage").objectReferenceValue = fill;
            serialized.FindProperty("damageTrailImage").objectReferenceValue = trail;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Invoke(bar, "Awake");
            Invoke(bar, "Start");

            Invoke(player, "OnHealthChanged", 100f, 65f);
            Invoke(bar, "LateUpdate");
            Assert.That(health.CurrentHealth, Is.EqualTo(65f));
            Assert.That(fill.fillAmount, Is.EqualTo(0.65f).Within(0.001f));
            Assert.That(trail.fillAmount, Is.EqualTo(1f), "Damage trail must keep its existing hold.");
            // 증감 차이를 피해로 해석하지 않으므로, 같은 값 재수신과 증가도 서버값을 그대로 따른다.
            Invoke(player, "OnHealthChanged", 65f, 80f);
            Invoke(player, "OnHealthChanged", 80f, 80f);
            Invoke(bar, "LateUpdate");
            Assert.That(health.CurrentHealth, Is.EqualTo(80f));
            Assert.That(fill.fillAmount, Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(health.IsDead, Is.False);
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void LocalSinkUsesServerDeduplicationAndDeathIsPresentedOnce()
    {
        var root = new GameObject("Health server path test");
        root.SetActive(false);
        try
        {
            var health = root.AddComponent<PlayerHealth>();
            Invoke(health, "Awake");
            var pose = new GameObject("Book pose").AddComponent<BookHoldPose>();
            pose.transform.SetParent(root.transform);
            var sink = new LocalShotSink();
            var attack = new AttackInfo(7, 1);
            sink.Hit(attack, 0, health, 30f);
            sink.Hit(attack, 0, health, 30f);
            Assert.That(health.CurrentHealth, Is.EqualTo(70f));
            sink.Hit(attack, 1, health, 100f);
            Assert.That(health.CurrentHealth, Is.Zero);
            Assert.That(health.IsDead, Is.True);
            Assert.That(pose.enabled, Is.False);
            // 같은 0 스냅샷은 사망 처리를 다시 일으키지 않는다.
            pose.enabled = true;
            health.ApplyServerHealth(0f);
            sink.Hit(attack, 2, health, 10f);
            Assert.That(pose.enabled, Is.True);
            Assert.That(health.CurrentHealth, Is.Zero);
        }
        finally { Object.DestroyImmediate(root); }
    }
}
#endif
