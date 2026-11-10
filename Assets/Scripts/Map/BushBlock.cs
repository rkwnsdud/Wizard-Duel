using UnityEngine;

// 부시 칸에서 바닥 위에 까는 덤불(갈대). 이동과 투사체를 막지 않도록 콜라이더 없이 렌더러만 둔다.
// 내 캐릭터가 들어간 덩어리만 SetFaded(true)로 반투명하게 그린다. 상대 화면에서는 늘 불투명하다.
public class BushBlock : Block
{
    [Tooltip("반투명으로 그릴 때 모든 머티리얼 슬롯에 대신 끼울 머티리얼. 비우면 반투명으로 바뀌지 않는다.")]
    [SerializeField] Material fadedMaterial;

    Renderer[] renderers;
    Material[][] opaqueMaterials;
    Material[][] fadedMaterials;

    public bool Faded { get; private set; }

    void Awake() => Cache();

    // 맵이 이 칸을 깔 때 BushField가 부른다. 갈대(ReedPatch)면 이웃 부시 칸에 맞춰 이어지는 모양을 새로 만든다.
    public void FitTo(int seed, BushNeighbors neighbors, float cellSize)
    {
        var patch = GetComponent<ReedPatch>();
        if (patch != null) patch.Build(seed, neighbors, cellSize);
    }

    public void SetFaded(bool faded)
    {
        if (Faded == faded || fadedMaterial == null) return;
        Cache();
        Faded = faded;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null)
                renderers[i].sharedMaterials = faded ? fadedMaterials[i] : opaqueMaterials[i];
    }

    void Cache()
    {
        if (renderers != null) return;
        renderers = GetComponentsInChildren<Renderer>(true);
        opaqueMaterials = new Material[renderers.Length][];
        fadedMaterials = new Material[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++)
        {
            opaqueMaterials[i] = renderers[i].sharedMaterials;
            fadedMaterials[i] = new Material[opaqueMaterials[i].Length];
            for (int j = 0; j < fadedMaterials[i].Length; j++) fadedMaterials[i][j] = fadedMaterial;
        }
    }
}
