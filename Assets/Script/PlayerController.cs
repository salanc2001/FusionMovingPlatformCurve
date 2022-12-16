using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using UnityEngine.UI;
using Fusion.KCC;

public class PlayerController : KCCPlayer
{
    [SerializeField]
    MeshRenderer meshRenderer;
    [SerializeField]
    float moveSpeed = 15f;

    [SerializeField]
    Bullet bulletPrefab;

    [Networked]
    NetworkButtons ButtonsPrevious { get; set; }

    [SerializeField]
    Image hpBar;
    [SerializeField]
    int maxHp = 100;


    [Networked(OnChanged = nameof(OnHpChanged))]
    public int Hp { get; set; }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
            Hp = maxHp;
    }

    public static void OnHpChanged(Changed<PlayerController> changed)
    {
        Debug.Log($"OnHpChanged:{changed.Behaviour.Hp}");
        changed.Behaviour.hpBar.fillAmount = (float)changed.Behaviour.Hp / changed.Behaviour.maxHp;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            ChangeColor_RPC(Color.red);
        }
        if (Input.GetKeyDown(KeyCode.G))
        {
            ChangeColor_RPC(Color.green);
        }
        if (Input.GetKeyDown(KeyCode.B))
        {
            ChangeColor_RPC(Color.blue);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
    void ChangeColor_RPC(Color newColor)
    {
        meshRenderer.material.color = newColor;
    }

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();

        if (GetInput(out NetworkInputData data))
        {
            NetworkButtons buttons = data.buttons;

            var pressed = buttons.GetPressed(ButtonsPrevious);
            ButtonsPrevious = buttons;
            Vector3 moveVector = data.movementInput.normalized;

            if (!moveVector.IsAlmostZero())
            {
                Vector2 lookRotation = KCC.FixedData.GetLookRotation(true, true);
                var aAngle = Mathf.Atan2(moveVector.x, moveVector.z) * Mathf.Rad2Deg;
                aAngle -= transform.eulerAngles.y;

                if (moveVector.y < 0)
                {
                    aAngle = 180f - aAngle;
                }
                else // fix the aAngle for 4th quadrant:
                if (moveVector.x < 0)
                {
                    aAngle = 360f + aAngle;
                }


                Vector2 lookRotationDelta = KCCUtility.GetClampedLookRotationDelta(lookRotation, new Vector2(1f, aAngle), -MaxCameraAngle, MaxCameraAngle);
                KCC.AddLookRotation(lookRotationDelta);
            }

            // networkCharacterController.Move(moveSpeed * moveVector * Runner.DeltaTime);
            KCC.SetInputDirection(moveVector);

            if (pressed.IsSet(InputButtons.Jump))
            {
                // By default the character jumps forward in facing direction
                Quaternion jumpRotation = KCC.FixedData.TransformRotation;

                // If we are moving, jump in that direction instead
                if (!moveVector.IsAlmostZero())
                {
                    jumpRotation = Quaternion.LookRotation(moveVector);
                }

                // Applying jump impulse
                KCC.Jump(jumpRotation * JumpImpulse);
            }

            if (pressed.IsSet(InputButtons.Fire))
            {
                Runner.Spawn(bulletPrefab, transform.position + transform.TransformDirection(Vector3.forward),
                Quaternion.LookRotation(transform.TransformDirection(Vector3.forward)),
                Object.InputAuthority);
            }
        }

        if (Hp <= 0 || transform.position.y <= -5f)
        {
            Respawn();
        }

    }

    void Respawn()
    {
        transform.position = Vector3.up * 2;
        Hp = maxHp;
    }

    public void TakeDamage(int damage)
    {
        if (Object.HasStateAuthority)
            Hp -= damage;
    }
}
