using UnityEngine;

public class PlayerAttack : MonoBehaviour
{
    PlayerInputActions inputActions;

    private void Awake()
    {
        inputActions = new PlayerInputActions();

    }

}
