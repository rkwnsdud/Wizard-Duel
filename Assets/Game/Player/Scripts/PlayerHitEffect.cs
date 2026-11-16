using UnityEngine;

public class PlayerHitEffect : MonoBehaviour
{
    [SerializeField]
    private SkinnedMeshRenderer characterRenderer;

    [SerializeField]
    private Material hitMaterial;

    private Material originalMaterial;

    private void Awake()
    {
        if (characterRenderer == null)
        {
            Debug.LogWarning("Character Renderer is not assigned.");
            return;
        }

        originalMaterial = characterRenderer.sharedMaterial;
    }

    public void ChangeToRed()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Run Test Red in Play mode.");
            return;
        }

        if (characterRenderer == null || hitMaterial == null || originalMaterial == null)
        {
            Debug.LogWarning("Assign the Character Renderer and Hit Material.");
            return;
        }

        CancelInvoke(nameof(RestoreColor));
        characterRenderer.sharedMaterial = hitMaterial;
        Invoke(nameof(RestoreColor), 0.15f);
    }

    private void RestoreColor()
    {
        if (characterRenderer != null && originalMaterial != null)
        {
            characterRenderer.sharedMaterial = originalMaterial;
        }
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(RestoreColor));
        RestoreColor();
    }
}
