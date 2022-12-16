using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.KCC;

public class Plate : NetworkAoIKCCProcessor
{
    public override int PositionWordOffset => 0;

    [SerializeField]
    private Collider _snapVolume;

    [Networked]
    [Capacity(8)]
    private NetworkArray<PlatformEntity> _entities { get; }

    [Networked]
    [Accuracy(AccuracyDefaults.POSITION)]
    private Vector3 _oldPosition { get; set; }

    [SerializeField]
    private float _spaceTransitionSpeed = 4.0f;

    private float _renderTime;

    private RawInterpolator _entitiesInterpolator;
    Rigidbody _rigidBody;

    private void Awake()
    {
        _rigidBody = GetComponent<Rigidbody>();

        if (_rigidBody == null)
            throw new System.Exception($"GameObject {name} has missing Rigidbody component!");

        _rigidBody.isKinematic = true;
        _rigidBody.useGravity = false;
        _rigidBody.interpolation = RigidbodyInterpolation.None;
        _rigidBody.constraints = RigidbodyConstraints.FreezeAll;
    }

    public override void Spawned()
    {
        _oldPosition = transform.position;

        _entitiesInterpolator = GetInterpolator(nameof(_entities));
    }


    public override void FixedUpdateNetwork()
    {
        Vector3 positionDelta = transform.position - _oldPosition;

        _oldPosition = transform.position;

        _rigidBody.position = transform.position;

        // Decrease SpaceAlpha of all entities.
        // 0.0f - the entity is moving in its interpolated space
        // 1.0f - the entity is moving in platform predicted space

        if (Object.HasStateAuthority == true)
        {
            for (int i = 0; i < _entities.Length; ++i)
            {
                PlatformEntity entity = _entities.Get(i);
                if (entity.SpaceAlpha > 0.0f)
                {
                    entity.SpaceAlpha = Mathf.Max(0.0f, entity.SpaceAlpha - Runner.DeltaTime * _spaceTransitionSpeed);
                    if (entity.SpaceAlpha == 0.0f)
                    {
                        entity.Id = default;
                        entity.Offset = default;
                    }

                    _entities.Set(i, entity);
                }
            }
        }

        ApplyPositionDelta(positionDelta);
    }

    // public override void Render()
    // {
    //     Vector3 positionDelta = transform.position - _oldPosition;

    //     _oldPosition = transform.position;

    //     _rigidBody.position = transform.position;

    //     ApplyPositionDelta(positionDelta);
    // }

    private void ApplyPositionDelta(Vector3 positionDelta)
    {
        if (positionDelta.IsZero() == true)
            return;

        // Valid only for entities within snap volume.
        // We need to apply the position delta immediately before any KCC runs its update.
        // Otherwise KCCs would collide with colliders positioned at previous update.

        for (int i = 0; i < _entities.Length; ++i)
        {
            PlatformEntity entity = _entities.Get(i);
            if (entity.Id.IsValid == true)
            {
                NetworkObject networkObject = Runner.FindObject(entity.Id);
                if (networkObject != null)
                {
                    KCC kcc = networkObject.GetComponent<KCC>();
                    if (kcc.IsProxy == true)
                    {
                        // Proxies are early interpolated, position delta is already applied to platform transform.
                        kcc.Interpolate();
                        continue;
                    }

                    KCCData kccData = kcc.Data;
                    Vector3 targetPosition = kccData.TargetPosition + positionDelta;

                    if (_snapVolume.ClosestPoint(targetPosition).AlmostEquals(targetPosition) == true)
                    {
                        kccData.BasePosition += positionDelta;
                        kccData.DesiredPosition += positionDelta;
                        kccData.TargetPosition += positionDelta;

                        // Just applying position delta to KCCData is not enough.
                        // The change must be immediately propagated to Transform and Rigidbody as well.

                        kcc.SynchronizeTransform(true, false);
                    }
                }
            }
        }
    }

    public override void OnStay(KCC kcc, KCCData data)
    {
        // State authority maintains list of KCCs inside snap volume.
        // These entities are transitioned from interpolated space to locally predicted space (driven by SpaceAlpha).

        if (kcc.IsInFixedUpdate == true && Object.HasStateAuthority == true && _snapVolume.ClosestPoint(data.TargetPosition).AlmostEquals(data.TargetPosition) == true)
        {
            // Find the KCC in the list and increase SpaceAlpha if it exists.

            for (int i = 0; i < _entities.Length; ++i)
            {
                PlatformEntity entity = _entities.Get(i);
                if (entity.Id == kcc.Object.Id)
                {
                    entity.Offset = data.TargetPosition - _oldPosition;
                    entity.SpaceAlpha = Mathf.Min(entity.SpaceAlpha + Runner.DeltaTime * _spaceTransitionSpeed * 2.0f, 1.0f);

                    _entities.Set(i, entity);

                    return;
                }
            }

            // The KCC is not tracked yet, find empty spot in the list and set initial SpaceAlpha.

            for (int i = 0; i < _entities.Length; ++i)
            {
                PlatformEntity entity = _entities.Get(i);
                if (entity.Id == default)
                {
                    entity.Id = kcc.Object.Id;
                    entity.Offset = data.TargetPosition - _oldPosition;
                    entity.SpaceAlpha = Runner.DeltaTime * _spaceTransitionSpeed + 0.001f;

                    _entities.Set(i, entity);

                    return;
                }
            }
        }
    }

    public override void OnInterpolate(KCC kcc, KCCData data)
    {
        if (kcc.IsProxy == false)
            return;

        // KCC proxy tries to find itself in the list and lerp between its interpolated space position and predicted platform space position + offset

        for (int i = 0; i < _entities.Length; ++i)
        {
            PlatformEntity entity = _entities.Get(i);
            if (entity.Id == kcc.Object.Id)
            {
                if (_entitiesInterpolator.TryGetArray(_entities, out NetworkArray<PlatformEntity> from, out NetworkArray<PlatformEntity> to, out float alpha) == true)
                {
                    PlatformEntity fromEntity = from.Get(i);
                    PlatformEntity toEntity = to.Get(i);

                    Vector3 interpolatedOffset = Vector3.Lerp(fromEntity.Offset, toEntity.Offset, alpha);
                    float interpolatedSpaceAlpha = Mathf.Lerp(fromEntity.SpaceAlpha, toEntity.SpaceAlpha, alpha);

                    data.TargetPosition = Vector3.Lerp(data.TargetPosition, transform.position + interpolatedOffset, interpolatedSpaceAlpha);
                }

                break;
            }
        }
    }

    private struct PlatformEntity : INetworkStruct
    {
        public NetworkId Id;
        public Vector3 Offset;
        public float SpaceAlpha;
    }

}
