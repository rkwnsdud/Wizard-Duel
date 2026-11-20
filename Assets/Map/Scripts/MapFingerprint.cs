using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

// 서버와 클라이언트가 만든 맵이 같은지 비교하는 지문. 논리 레이아웃과 두 스폰의 월드 위치·회전을 SHA-256으로 묶는다.
public static class MapFingerprint
{
    public static string Compute(MapLayout layout, Transform[] spawns)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (string row in layout.ToBlockLayout()) writer.Write(row);
        foreach (Transform spawn in spawns)
        {
            Vector3 position = spawn.position;
            Quaternion rotation = spawn.rotation;
            writer.Write(position.x);
            writer.Write(position.y);
            writer.Write(position.z);
            writer.Write(rotation.x);
            writer.Write(rotation.y);
            writer.Write(rotation.z);
            writer.Write(rotation.w);
        }
        writer.Flush();
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "");
    }
}
