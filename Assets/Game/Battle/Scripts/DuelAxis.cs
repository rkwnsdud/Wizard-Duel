using UnityEngine;

// 두 스폰을 잇는 결투 축. 전투 카메라 방향(DuelCameraSettings.FacingYaw)과 전투 광원 방향(SideLight)이 이 축을 기준으로 선다.
// 두 클라이언트가 같은 값을 얻도록 언제나 방장 스폰에서 참가자 스폰 쪽으로 잰다.
public static class DuelAxis
{
    // 방장 스폰에서 참가자 스폰을 보는 방향(도, +Z가 0이고 +X가 90). 0 이상 360 미만이다. 높이 차이는 보지 않는다.
    public static float Yaw(Vector3 hostSpawn, Vector3 guestSpawn)
    {
        Vector3 toward = guestSpawn - hostSpawn;
        if (toward.x * toward.x + toward.z * toward.z <= 1e-6f) return 0f;
        return Mathf.Repeat(Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg, 360f);
    }

    // spawns(0은 방장, 1은 참가자의 스폰 지점)로 축을 구한다. 스폰이 모자라면 false다.
    public static bool TryYaw(Transform[] spawns, out float axis)
    {
        axis = 0f;
        if (spawns == null || spawns.Length < 2 || spawns[0] == null || spawns[1] == null) return false;
        axis = Yaw(spawns[0].position, spawns[1].position);
        return true;
    }

    // 두 스폰을 잇는 선에 직교하도록 home을 세로축으로만 돌린 광원 방향. 내리쬐는 높이(고도)는 그대로이고, 선의 두 옆 가운데
    // home에 가까운 쪽에서 비춘다. 그림자가 어느 플레이어 쪽으로도 늘어지지 않고 두 사람 사이 선의 옆으로 진다.
    public static Quaternion SideLight(float axis, Quaternion home)
    {
        Vector3 forward = home * Vector3.forward;
        // 바로 위에서 내리쬐면 이미 어느 축에나 직교한다.
        if (forward.x * forward.x + forward.z * forward.z <= 1e-8f) return home;
        float homeYaw = Heading(home);
        float side = Mathf.Abs(Mathf.DeltaAngle(homeYaw, axis + 90f)) <= 90f ? axis + 90f : axis - 90f;
        return Quaternion.AngleAxis(Mathf.DeltaAngle(homeYaw, side), Vector3.up) * home;
    }

    // from에서 to 쪽으로 세로축만 짧은 방향으로 돌며 t만큼 간 광원 방향. 로비에서 전투로 내려가는 카메라처럼
    // 해가 하늘을 따라 옆으로 돌아간다. SideLight로 돌린 방향끼리는 고도가 같아서 도는 동안에도 높이가 그대로이고,
    // Slerp처럼 중간에 하늘 꼭대기 쪽으로 솟지 않는다. t가 1이면 to와 같다.
    public static Quaternion TurnLight(Quaternion from, Quaternion to, float t)
    {
        float turn = Mathf.DeltaAngle(Heading(from), Heading(to));
        return Quaternion.AngleAxis(turn * t, Vector3.up) * from;
    }

    // 광원이 비추는 수평 방향(도, +Z가 0이고 +X가 90).
    static float Heading(Quaternion light)
    {
        Vector3 forward = light * Vector3.forward;
        return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    }
}
