using UnityEngine;

// 어깨-팔꿈치-손 같은 두 마디 본을 목표 위치로 굽힌다. 뼈 길이는 바꾸지 않는다.
public static class TwoBoneIK
{
    // root(위팔)·mid(아래팔)를 돌려 tip(손)을 target에 맞추고, 팔꿈치가 hint 쪽을 향하게 한다.
    // 닿지 않는 거리면 target 방향으로 최대한 뻗는다.
    public static void Solve(Transform root, Transform mid, Transform tip, Vector3 target, Vector3 hint)
    {
        Vector3 a = root.position;
        Vector3 b = mid.position;
        Vector3 c = tip.position;
        Vector3 ab = b - a;
        Vector3 bc = c - b;
        Vector3 ac = c - a;
        Vector3 at = target - a;
        float abLength = ab.magnitude;
        float bcLength = bc.magnitude;
        if (abLength < 1e-5f || bcLength < 1e-5f) return;
        float atLength = Mathf.Clamp(at.magnitude, Mathf.Abs(abLength - bcLength) + 1e-4f,
            abLength + bcLength - 1e-4f);

        float oldAngle = TriangleAngle(ac.magnitude, abLength, bcLength);
        float newAngle = TriangleAngle(atLength, abLength, bcLength);
        Vector3 axis = Vector3.Cross(ab, bc);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(hint - a, bc);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(at, bc);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.up;
        axis.Normalize();
        float half = 0.5f * (oldAngle - newAngle);
        float sin = Mathf.Sin(half);
        mid.rotation = new Quaternion(axis.x * sin, axis.y * sin, axis.z * sin, Mathf.Cos(half)) * mid.rotation;

        ac = tip.position - a;
        root.rotation = Quaternion.FromToRotation(ac, at) * root.rotation;

        // 손 위치는 그대로 두고 어깨-손 축을 기준으로 팔꿈치를 hint 쪽으로 돌린다.
        ab = mid.position - a;
        ac = tip.position - a;
        if (ac.sqrMagnitude < 1e-8f) return;
        Vector3 acNormal = ac.normalized;
        Vector3 abProjected = ab - acNormal * Vector3.Dot(ab, acNormal);
        Vector3 hintProjected = (hint - a) - acNormal * Vector3.Dot(hint - a, acNormal);
        float reach = abLength + bcLength;
        if (abProjected.sqrMagnitude > reach * reach * 0.001f && hintProjected.sqrMagnitude > 1e-8f)
            root.rotation = Quaternion.FromToRotation(abProjected, hintProjected) * root.rotation;
    }

    // 세 변 길이로 opposite 변의 맞은편 각(라디안)을 구한다.
    private static float TriangleAngle(float opposite, float side1, float side2)
    {
        float cos = (side1 * side1 + side2 * side2 - opposite * opposite) / (2f * side1 * side2);
        return Mathf.Acos(Mathf.Clamp(cos, -1f, 1f));
    }
}
