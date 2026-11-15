#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public class WeaponIdentityTests
{
    static WeaponSpawnCatalog Catalog() => AssetDatabase.LoadAssetAtPath<WeaponSpawnCatalog>(
        "Assets/Scripts/Battle/WeaponSpawnCatalog.asset");

    [Test]
    public void DefinitionsHaveUniqueIdsAndMatchingPrefabs()
    {
        var ids = new HashSet<byte>();
        foreach (var definition in Catalog().Entries)
        {
            Assert.That(definition.id, Is.Not.Zero);
            Assert.That(ids.Add(definition.id), Is.True, definition.name);
            Assert.That(definition.prefab.Definition, Is.SameAs(definition));
            Assert.That(Catalog().Identify(definition.prefab), Is.EqualTo(definition.id));
        }
    }

    [Test]
    public void IdentityComesFromTheDefinitionInsteadOfTheComponentType()
    {
        var root = new GameObject("Weapon identity");
        root.SetActive(false);
        try
        {
            var weapon = root.AddComponent<BasicWandWeapon>();
            var serialized = new SerializedObject(weapon);
            foreach (var definition in Catalog().Entries)
            {
                serialized.FindProperty("definition").objectReferenceValue = definition;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(Catalog().Identify(weapon), Is.EqualTo(definition.id));
            }
        }
        finally { Object.DestroyImmediate(root); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WeaponButtonsGrowWithTheDefinitionListAndSelectTheirOwnIndex(bool alreadyInitialized)
    {
        var root = new GameObject("Weapon choices");
        root.SetActive(false);
        try
        {
            var button = new GameObject("Template", typeof(RectTransform), typeof(Image), typeof(Button));
            button.transform.SetParent(root.transform);
            button.GetComponent<Button>().targetGraphic = button.GetComponent<Image>();
            var border = new GameObject("Border", typeof(RectTransform), typeof(Image));
            border.transform.SetParent(button.transform);
            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            icon.transform.SetParent(button.transform);
            var control = root.AddComponent<SegmentedControl>();
            var serialized = new SerializedObject(control);
            serialized.FindProperty("buttons").arraySize = 1;
            serialized.FindProperty("buttons").GetArrayElementAtIndex(0).objectReferenceValue = button.GetComponent<Button>();
            serialized.FindProperty("icons").arraySize = 1;
            serialized.FindProperty("icons").GetArrayElementAtIndex(0).objectReferenceValue = icon.GetComponent<Image>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var definitions = Catalog().Entries;
            if (alreadyInitialized) control.SetIcons(new[] { definitions[0].icon });
            control.SetIcons(System.Array.ConvertAll(definitions, definition => definition.icon));
            Assert.That(control.Count, Is.EqualTo(definitions.Length));
            int changes = 0;
            control.Changed += _ => changes++;
            var buttons = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Assert.That(buttons[i].GetComponentInChildren<Image>().sprite, Is.Null);
                Assert.That(buttons[i].transform.GetChild(0).GetComponent<Image>().sprite, Is.Null, "Border stays separate from the weapon icon.");
                Assert.That(buttons[i].transform.GetChild(1).GetComponent<Image>().sprite, Is.SameAs(definitions[i].icon));
                buttons[i].onClick.Invoke();
                Assert.That(control.Value, Is.EqualTo(i));
            }
            Assert.That(changes, Is.EqualTo(definitions.Length - 1));
        }
        finally { Object.DestroyImmediate(root); }
    }
}
#endif
