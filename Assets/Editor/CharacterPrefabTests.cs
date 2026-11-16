#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using Unity.Netcode;
using UnityEngine;

public class CharacterPrefabTests
{
    [Test]
    public void SharedBodyContainsTheRigAndSocketWithoutGameplay()
    {
        var body = Load("WizardBody");
        Assert.That(body.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
        Assert.That(body.GetComponentsInChildren<Collider>(true), Is.Empty);
        Assert.That(body.GetComponentsInChildren<Animator>(true).Length, Is.EqualTo(1));
        Assert.That(body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length, Is.EqualTo(1));
        var socket = System.Array.Find(body.GetComponentsInChildren<Transform>(true), t => t.name == "WeaponSocket");
        Assert.That(socket, Is.Not.Null);
        Assert.That(socket.childCount, Is.Zero);
    }

    [Test]
    public void CombatAndLobbyShareOnlyTheVisualBody()
    {
        foreach (string name in new[] { "DuelWizard-1", "DuelWizard-2", "LobbyWizard-1", "LobbyWizard-2" })
        {
            var root = Load(name);
            var wizard = root.transform.Find("Wizard");
            Assert.That(wizard, Is.Not.Null, name);
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(wizard.gameObject),
                Is.EqualTo("Assets/Game/Player/Prefabs/WizardBody.prefab"), name);
            foreach (string dependency in AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(root)))
                Assert.That(dependency, Is.Not.EqualTo("Assets/Player/Player.prefab"), name);
            if (name.StartsWith("Duel"))
            {
                Assert.That(root.GetComponents<PlayerMotor>().Length, Is.EqualTo(1), name);
                Assert.That(wizard.GetComponent<WizardAnimationEvent>(), Is.Not.Null, name);
            }
            else
            {
                Assert.That(root.GetComponents<Component>().Length, Is.EqualTo(1), name);
                Assert.That(wizard.GetComponent<WizardAnimationEvent>(), Is.Null, name);
                Assert.That(root.GetComponentsInChildren<WeaponBase>(true), Is.Empty, name);
            }
        }
    }

    [Test]
    public void TeamAppearancesStayTheSameInCombatAndLobby()
    {
        for (int team = 1; team <= 2; team++)
        {
            var battle = Load("DuelWizard-" + team).GetComponentInChildren<SkinnedMeshRenderer>(true);
            var lobby = Load("LobbyWizard-" + team).GetComponentInChildren<SkinnedMeshRenderer>(true);
            CollectionAssert.AreEqual(battle.sharedMaterials, lobby.sharedMaterials);
        }
        Assert.That(Load("DuelWizard-1").GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterial,
            Is.Not.SameAs(Load("DuelWizard-2").GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterial));
    }

    [Test]
    public void NetworkListRegistersBothCombatPrefabsWithoutEmptyOrDuplicateEntries()
    {
        var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
        var prefabs = new System.Collections.Generic.HashSet<GameObject>();
        var hashes = new System.Collections.Generic.HashSet<uint>();
        foreach (var entry in list.PrefabList)
        {
            Assert.That(entry.Prefab, Is.Not.Null);
            Assert.That(prefabs.Add(entry.Prefab), Is.True, entry.Prefab.name);
            var networkObject = entry.Prefab.GetComponent<NetworkObject>();
            Assert.That(networkObject, Is.Not.Null, entry.Prefab.name);
            uint hash = new SerializedObject(networkObject).FindProperty("GlobalObjectIdHash").uintValue;
            Assert.That(hash, Is.Not.Zero, entry.Prefab.name);
            Assert.That(hashes.Add(hash), Is.True, entry.Prefab.name);
        }
        CollectionAssert.AreEquivalent(new[] { Load("DuelWizard-1"), Load("DuelWizard-2") }, prefabs);
    }

    static GameObject Load(string name) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Player/Prefabs/" + name + ".prefab");
}
#endif
