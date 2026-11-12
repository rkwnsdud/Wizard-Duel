using UnityEngine;

// 카메라 한 장면. 어느 점(Focus)을 화면 어디(Anchor)에 얼마나 크게(HalfHeight) 보는지와 원근 정도로 적는다.
// 로비의 원근 카메라와 전투의 직교 카메라를 이 값으로 섞으면 초점은 늘 Anchor에 붙어 있고 원근만 고르게 줄어든다.
// 원근과 직교 투영 행렬을 원소별로 섞으면 카메라를 뒤로 물리면서 시야각을 좁힌 원근(돌리 줌)과 같은 그림이 나온다.
// 그래서 카메라는 실제로 멀리 가지 않고 Distance에 머물러, 그림자 거리와 안개가 그대로 맞는다.
public struct CameraView
{
    // 화면에 붙잡아 둘 월드 점.
    public Vector3 Focus;
    // Focus가 놓일 화면 위치. (0, 0)은 왼쪽 아래, (1, 1)은 오른쪽 위.
    public Vector2 Anchor;
    // 초점 평면에서 화면 절반 높이에 보이는 월드 길이. 직교 카메라의 orthographicSize와 같다.
    public float HalfHeight;
    public float Yaw;
    public float Pitch;
    // 실제 카메라 위치에서 Focus까지의 거리.
    public float Distance;
    // 원근이 기준으로 삼는 거리의 역수. 원근 장면은 1/Distance, 직교 장면은 0이다. 섞으면 그 사이에서 원근이 고르게 줄어든다.
    public float InverseDepth;
    // 안개가 시작되고 끝나는 거리.
    public Vector2 Fog;
    public float ShadowDistance;

    public Quaternion Rotation => Quaternion.Euler(Pitch, Yaw, 0f);
    public Vector3 Position => Focus - Rotation * Vector3.forward * Distance;
    // 실제 카메라 위치에서 HalfHeight가 화면 절반 높이를 채우는 원근 시야각.
    public float FieldOfView => 2f * Mathf.Atan(HalfHeight / Distance) * Mathf.Rad2Deg;

    // 직교 행렬을 섞는 비율. 섞은 투영이 원근 기준 거리 1/InverseDepth의 원근과 같아지도록 정한다.
    // 카메라를 k만큼 뒤로 물린 원근이 원소 비율 k/(1+k)로 섞은 투영과 같다(k는 월드 단위).
    public float OrthographicWeight
    {
        get
        {
            if (InverseDepth <= 0f) return 1f;
            float pullBack = Mathf.Max(0f, 1f / InverseDepth - Distance);
            return pullBack / (1f + pullBack);
        }
    }

    // distance 떨어진 카메라가 fieldOfView로 focus를 anchor에 두고 보는 원근 장면.
    public static CameraView Perspective(Vector3 focus, Vector2 anchor, float yaw, float pitch, float distance,
        float fieldOfView, Vector2 fog, float shadowDistance) => new CameraView
    {
        Focus = focus,
        Anchor = anchor,
        HalfHeight = distance * Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad),
        Yaw = yaw,
        Pitch = pitch,
        Distance = distance,
        InverseDepth = 1f / distance,
        Fog = fog,
        ShadowDistance = shadowDistance
    };

    // halfHeight만큼 보이는 직교 장면. 카메라는 focus에서 distance 떨어진 자리에 선다.
    public static CameraView Orthographic(Vector3 focus, Vector2 anchor, float yaw, float pitch, float distance,
        float halfHeight, Vector2 fog, float shadowDistance) => new CameraView
    {
        Focus = focus,
        Anchor = anchor,
        HalfHeight = halfHeight,
        Yaw = yaw,
        Pitch = pitch,
        Distance = distance,
        InverseDepth = 0f,
        Fog = fog,
        ShadowDistance = shadowDistance
    };

    // a에서 b로 t만큼 섞는다. Yaw는 그대로 선형으로 섞으므로, 반대쪽으로 돌지 않게 b.Yaw를 a.Yaw 가까이로 풀어서 넘긴다.
    public static CameraView Lerp(CameraView a, CameraView b, float t)
    {
        // 줌은 배율이 고르게 바뀌도록 로그로 섞는다.
        float halfHeight = LogLerp(a.HalfHeight, b.HalfHeight, t);
        // 초점은 HalfHeight가, 앵커는 1/HalfHeight가 나아간 만큼 옮긴다. 그러면 b의 초점이 화면에서 곧게 제자리로 가고,
        // t로 함께 옮길 때처럼 줌이 앞서는 동안 화면 바깥쪽으로 밀렸다 돌아오지 않는다.
        float focusStep = t;
        float anchorStep = t;
        if (Mathf.Abs(a.HalfHeight - b.HalfHeight) > 1e-4f * Mathf.Max(a.HalfHeight, b.HalfHeight))
        {
            focusStep = (a.HalfHeight - halfHeight) / (a.HalfHeight - b.HalfHeight);
            anchorStep = (1f / halfHeight - 1f / a.HalfHeight) / (1f / b.HalfHeight - 1f / a.HalfHeight);
        }
        return new CameraView
        {
            Focus = Vector3.LerpUnclamped(a.Focus, b.Focus, focusStep),
            Anchor = Vector2.LerpUnclamped(a.Anchor, b.Anchor, anchorStep),
            HalfHeight = halfHeight,
            Yaw = Mathf.LerpUnclamped(a.Yaw, b.Yaw, t),
            Pitch = Mathf.LerpUnclamped(a.Pitch, b.Pitch, t),
            Distance = LogLerp(a.Distance, b.Distance, t),
            InverseDepth = Mathf.LerpUnclamped(a.InverseDepth, b.InverseDepth, t),
            Fog = Vector2.LerpUnclamped(a.Fog, b.Fog, t),
            ShadowDistance = Mathf.LerpUnclamped(a.ShadowDistance, b.ShadowDistance, t)
        };
    }

    static float LogLerp(float a, float b, float t) => a * Mathf.Pow(b / a, t);

    // 카메라의 투영 행렬(OpenGL 규약, Camera.projectionMatrix와 같은 꼴).
    public Matrix4x4 Projection(float aspect, float near, float far)
    {
        // 렌즈를 옮겨 Focus가 화면 가운데가 아닌 Anchor에 오게 한다.
        Matrix4x4 perspective = Matrix4x4.Perspective(FieldOfView, aspect, near, far);
        perspective[0, 2] = 1f - 2f * Anchor.x;
        perspective[1, 2] = 1f - 2f * Anchor.y;
        float weight = OrthographicWeight;
        if (weight <= 0f) return perspective;

        Matrix4x4 orthographic = Matrix4x4.Ortho(-HalfHeight * aspect, HalfHeight * aspect, -HalfHeight, HalfHeight, near, far);
        orthographic[0, 3] = 2f * Anchor.x - 1f;
        orthographic[1, 3] = 2f * Anchor.y - 1f;
        if (weight >= 1f) return orthographic;

        Matrix4x4 blended = Matrix4x4.zero;
        for (int i = 0; i < 16; i++) blended[i] = Mathf.LerpUnclamped(perspective[i], orthographic[i], weight);
        return blended;
    }

    // 카메라를 이 장면 자리에 놓는다. 투영은 직접 넣으므로 원근 카메라로 둔다. 안개와 그림자 거리는 부르는 쪽이 맞춘다.
    public void ApplyTo(Camera camera)
    {
        camera.orthographic = false;
        camera.transform.SetPositionAndRotation(Position, Rotation);
        camera.projectionMatrix = Projection(camera.aspect, camera.nearClipPlane, camera.farClipPlane);
    }
}
