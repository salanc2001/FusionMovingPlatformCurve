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
    float rotateSpeed = 3f;
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
                var aMoveAngle = Mathf.Atan2(moveVector.x, moveVector.z) * Mathf.Rad2Deg;

                while (aMoveAngle < 0f)
                    aMoveAngle += 360f;
                while (aMoveAngle > 360f)
                    aMoveAngle -= 360f;

                // 305 - 45 = 260
                // - 45 - 45 = -90

                float aMoveAngle1 = aMoveAngle - transform.eulerAngles.y;
                float aMoveAngle2 = aMoveAngle - 360f - transform.eulerAngles.y;

                float aDeltaAngle;
                if (Mathf.Abs(aMoveAngle1) > Mathf.Abs(aMoveAngle2))
                    aDeltaAngle = aMoveAngle2;
                else
                    aDeltaAngle = aMoveAngle1;

                aDeltaAngle = Mathf.Lerp(0, aDeltaAngle, Runner.DeltaTime * rotateSpeed);

                KCC.AddLookRotation(new Vector2(1f, aDeltaAngle));
            }

            // networkCharacterController.Move(moveSpeed * moveVector * Runner.DeltaTime);
            KCC.SetInputDirection(moveVector * moveSpeed);

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
