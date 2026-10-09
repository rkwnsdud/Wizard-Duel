using System.Runtime.CompilerServices;
using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;

    private float currentHealth;
    private bool isDead = false;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public bool IsDead => isDead;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float damage)
    {
        if (damage <= 0f || isDead)
            return;

        currentHealth = Mathf.Max(0f, currentHealth - damage);

        Debug.Log("체력: " + currentHealth);

        if(currentHealth <= 0f)
        {
            isDead = true;
            StopPlayerAfterDeath();
        }
        
    }

    private void StopPlayerAfterDeath()
    {
        PlayerAttack playerAttack = GetComponent<PlayerAttack>();
        if (playerAttack != null)
            playerAttack.enabled = false;

        PlayerMovement playerMovement = GetComponent<PlayerMovement>();
        if (playerMovement != null)
            playerMovement.enabled = false;

        PlayerAim playerAim = GetComponent<PlayerAim>();
        if (playerAim != null)
            playerAim.enabled = false;

        Transform character = transform.Find("Wizard");
        Animator animator = character != null ? character.GetComponent<Animator>() : null;
        if (animator != null)
        {
            animator.ResetTrigger("Attack");
            animator.SetFloat("MoveSpeed", 0f);
            animator.SetFloat("MoveX", 0f);
            animator.SetFloat("MoveZ", 0f);
            animator.enabled = false;
        }
    }
}
