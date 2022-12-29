using Fusion;
using Fusion.KCC;
using UnityEngine;

[RequireComponent(typeof(KCC))]
[OrderBefore(typeof(KCC))]
public abstract class KCCPlayer : NetworkKCCProcessor
{
    // PUBLIC MEMBERS

    public KCC KCC => _kcc;
    protected float MaxCameraAngle => _maxCameraAngle;
    protected Vector3 JumpImpulse => _jumpImpulse;

    private KCC _kcc;

    [SerializeField]
    private float _areaOfInterestRadius;
    [SerializeField]
    private float _maxCameraAngle;
    [SerializeField]
    private Vector3 _jumpImpulse;
    [Networked]
    public float SpeedMultiplier { get; set; } = 1.0f;

    /// <summary>
    /// Called from menu to speed up character for faster navigation through example levels.
    /// Players should not be able to define their speed unless this is a design decision.
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void ToggleSpeedRPC(int direction)
    {
        if (direction > 0)
        {
            SpeedMultiplier *= 2.0f;
            if (SpeedMultiplier >= 10.0f)
            {
                SpeedMultiplier = 0.25f;
            }
        }
        else
        {
            SpeedMultiplier *= 0.5f;
            if (SpeedMultiplier <= 0.2f)
            {
                SpeedMultiplier = 8.0f;
            }
        }
    }

    public override void Spawned()
    {
        // Explicit KCC initialization. This needs to be called before using API, otherwise changes could be overriden by implicit initialization from KCC.Start() or KCC.Spawned()
        _kcc.Initialize(EKCCDriver.Fusion);

        // Player itself can modify kinematic speed, registering to KCC
        _kcc.AddModifier(this);
    }

    public override void FixedUpdateNetwork()
    {
        // By default we expect derived classes to process input in FixedUpdateNetwork().
        // The correct approach is to set input before KCC updates internally => we need to specify [OrderBefore(typeof(KCC))] attribute.

        // SimplePlayer runs input processing in FixedUpdateNetwork() as expected, but KCC runs its internal update after Player.FixedUpdateNetwork().
        // Following call sets AoI position to last fixed update KCC position. It should not be a problem in most cases, but some one-frame glitches after teleporting might occur.
        // This problem is solved in AdvancedPlayer which uses manual KCC update at the cost of slightly increased complexity.

        Runner.AddPlayerAreaOfInterest(Object.InputAuthority, _kcc.FixedData.TargetPosition, _areaOfInterestRadius);
    }

    // NetworkKCCProcessor INTERFACE

    // Lowest priority => this processor will be executed last.
    public override float Priority => float.MinValue;

    public override EKCCStages GetValidStages(KCC kcc, KCCData data)
    {
        // Only SetKinematicSpeed stage is used, rest are filtered out and corresponding method calls will be skipped.
        return EKCCStages.SetKinematicSpeed;
    }

    public override void SetKinematicSpeed(KCC kcc, KCCData data)
    {
        // Applying multiplier.
        data.KinematicSpeed *= SpeedMultiplier;
    }

    private void Awake()
    {
        if (gameObject.TryGetComponent(out KCC iKCC))
            _kcc = iKCC;
        else
            _kcc = gameObject.GetComponent<KCC>();
    }


}