using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct SpawnedWeapon
{
    public readonly uint Id;
    public readonly int Site;
    public readonly byte Weapon;
    public readonly Vector3 Ground;
    public readonly double Born;
    public SpawnedWeapon(uint id, int site, byte weapon, Vector3 ground, double born)
    { Id = id; Site = site; Weapon = weapon; Ground = ground; Born = born; }
}

// Server state, kept independent of rendering and transport for timing/race tests.
public sealed class WeaponSpawnRound
{
    public const double Interval = 30;
    readonly Dictionary<uint, SpawnedWeapon> drops = new Dictionary<uint, SpawnedWeapon>();
    readonly HashSet<int> occupied = new HashSet<int>();
    readonly List<int> available = new List<int>();
    IReadOnlyList<Vector3> sites;
    IReadOnlyList<byte> weapons;
    System.Random random;
    uint nextId;
    double nextSpawn;
    public bool Running { get; private set; }
    public IReadOnlyDictionary<uint, SpawnedWeapon> Drops => drops;

    public void Begin(IReadOnlyList<Vector3> positions, IReadOnlyList<byte> choices, double now, int seed)
    {
        Stop();
        sites = positions;
        weapons = choices;
        random = new System.Random(seed);
        nextSpawn = now + Interval;
        Running = true;
    }

    public bool Tick(double now, Predicate<Vector3> positionAvailable, out SpawnedWeapon drop)
    {
        drop = default;
        if (!Running || now < nextSpawn) return false;
        nextSpawn = now + Interval; // No catch-up burst after a paused/stalled server.
        available.Clear();
        for (int i = 0; i < sites.Count; i++)
            if (!occupied.Contains(i) && (positionAvailable == null || positionAvailable(sites[i]))) available.Add(i);
        if (available.Count == 0 || weapons.Count == 0) return false;
        int site = available[random.Next(available.Count)];
        drop = new SpawnedWeapon(++nextId, site, weapons[random.Next(weapons.Count)], sites[site], now);
        drops.Add(drop.Id, drop);
        occupied.Add(site);
        return true;
    }

    public bool Remove(uint id)
    {
        if (!drops.TryGetValue(id, out var drop)) return false;
        drops.Remove(id);
        occupied.Remove(drop.Site);
        return true;
    }

    public void Stop()
    {
        Running = false;
        drops.Clear();
        occupied.Clear();
    }
}
