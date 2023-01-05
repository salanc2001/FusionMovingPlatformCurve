using UnityEditor;

namespace Fusion.KCC
{
    using System;
    using UnityEngine;
    using Fusion;
    using Fusion.KCC;
    using System.Linq;
    using com.spacepuppy.Geom;
    using System.Collections.Generic;
    using UnityEngine.UI;

    /// <summary>
    /// Basic platform which moves the object between waypoints and propagates transform changes to all KCCs within snap volume.
    /// This script handles transition of KCC proxies between interpolated space and locally predicted platform space, which is in sync with local player KCC space.
    /// The implementation is not compatible with shared mode.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [OrderBefore(typeof(NetworkAreaOfInterestBehaviour))]
    public sealed class MovingPlatform : NetworkAoIKCCProcessor
    {
        [SerializeField]
        private EPlatformMode _mode;
        [SerializeField]
        private float _speed = 1.0f;
        [SerializeField]
        private PlatformWaypoint[] _waypoints;
        [SerializeField]
        private Collider _snapVolume;
        [SerializeField]
        private float _spaceTransitionSpeed = 2.0f;

        [Networked]
        [Accuracy(AccuracyDefaults.POSITION)]
        private Vector3 _position { get; set; }
        [Networked]
        [Capacity(8)]
        private NetworkArray<PlatformEntity> _entities { get; }
        [Networked]
        private int _waypoint { get; set; }
        [Networked]
        private int _direction { get; set; }
        [Networked]
        private float _waitTime { get; set; }
        [Networked]
        private float _rotationDelta { get; set; }
        [Networked]
        float mAngle { get; set; }
        private Transform _transform;
        private Rigidbody _rigidbody;
        private float _renderTime;
        private int _renderWaypoint;
        private int _renderDirection;
        private Vector3 _renderPosition;
        private RawInterpolator _entitiesInterpolator;
        public override int PositionWordOffset => 0;

        [SerializeField]
        Text[] mDebugLog;

        public override void Spawned()
        {
            _position = _transform.position;
            _waypoint = default;
            _direction = default;

            _renderTime = Runner.SimulationTime;
            _renderPosition = _position;
            _renderWaypoint = _waypoint;
            _renderDirection = _direction;
            _entitiesInterpolator = GetInterpolator(nameof(_entities));

            HandleCurvePoints();
        }

        void HandleCurvePoints()
        {
            if (_mode != EPlatformMode.Curve)
                return;

            List<PlatformWaypoint> points = new List<PlatformWaypoint>();
            _curvePath = GenerateCurvePath(GetWayPoints());
            for (int i = 0; i < _curvePath.Length; i++)
                mCatmullRomSpline.AddControlPoint(_curvePath[i]);


            int aInterval = 20;
            float aIntervalTime = mCatmullRomSpline.GetPathLength() / _speed / aInterval;

            for (int i = 0; i < aInterval; i++)
            {
                Transform aT = new GameObject().transform;
                aT.gameObject.name = $"CurvePath{i}";
                aT.SetParent(transform.parent);
                aT.position = mCatmullRomSpline.GetPosition((float)i / aInterval);
                Vector3 aAngleVelocity = Vector3.zero;
                if (i != 0)
                {
                    Vector3 aDelta = aT.position - points[i - 1].Transform.position;
                    float aAngle = Mathf.Atan2(aDelta.x, aDelta.z) * Mathf.Rad2Deg;
                    aT.eulerAngles = new Vector3(transform.eulerAngles.x, aAngle, transform.eulerAngles.z);
                    float aAngleDelta = Mathf.DeltaAngle(points[i - 1].Transform.eulerAngles.y, aT.eulerAngles.y);
                    aAngleVelocity = new Vector3(0f, aAngleDelta / aIntervalTime, 0f);
                }
                else
                    aT.eulerAngles = transform.eulerAngles;

                points.Add(new PlatformWaypoint
                {
                    Transform = aT,
                    WaitTime = 0,
                    AngleVelocity = aAngleVelocity
                });

                if (i == 0)
                {
                    points[0].WaitTime = _waypoints[0].WaitTime;
                }
            }

            float aAngleDelta2 = Mathf.DeltaAngle(points[points.Count - 1].Transform.eulerAngles.y, points[0].Transform.eulerAngles.y);
            var aAngleVelocity2 = new Vector3(0f, aAngleDelta2 / aIntervalTime, 0f);
            points[0].AngleVelocity = aAngleVelocity2;

            _waypoints = points.ToArray();
        }

        public override void FixedUpdateNetwork()
        {
            Vector3 positionDelta = default;

            if (_waitTime > 0.0f)
            {
                _waitTime = Mathf.Max(_waitTime - Runner.DeltaTime, 0.0f);
            }
            else
            {
                // Calculate next position of the platform.
                CalculateNextPosition(_waypoint, _direction, _position, Runner.DeltaTime, out int nextWaypoint, out int nextDirection, out positionDelta, out float waitTime);

                if (true)
                {
                    if (_mode == EPlatformMode.Curve)
                    {

                        if (Object.HasStateAuthority)
                        {
                            if (_waitTime > 0.0f)
                                transform.eulerAngles = _waypoints[_waypoint].Transform.eulerAngles;
                            else if (_waypoint != nextWaypoint)
                                transform.eulerAngles = _waypoints[_waypoint].Transform.eulerAngles;
                            else
                                transform.eulerAngles += _waypoints[_waypoint].AngleVelocity * Runner.DeltaTime;

                            _rotationDelta = _waypoints[_waypoint].AngleVelocity.y * Runner.DeltaTime;
                            mAngle = transform.eulerAngles.y;
                        }
                        else
                        {
                            Vector3 aAngle = transform.eulerAngles;
                            aAngle.y = mAngle;
                            transform.eulerAngles = aAngle;
                        }
                    }
                }


                _position += positionDelta;
                _waypoint = nextWaypoint;
                _direction = nextDirection;
                _waitTime = waitTime;
            }

            // Store last values separately for render prediction.

            _renderTime = Runner.SimulationTime;
            _renderPosition = _position;
            _renderWaypoint = _waypoint;
            _renderDirection = _direction;

            _transform.position = _position;
            _rigidbody.position = _position;

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

            ApplyPositionDelta(positionDelta, _rotationDelta);
        }

        public override void Render()
        {
            if (_waitTime > 0.0f)
                return;

            float renderTime = Runner.SimulationTime + Runner.DeltaTime * Runner.Simulation.StateAlpha;
            float deltaTime = renderTime - _renderTime;

            // Calculate next render position of the platform.
            // We always have to calculate delta against previous render frame to avoid clearing render changes from other sources.
            CalculateNextPosition(_renderWaypoint, _renderDirection, _renderPosition, deltaTime, out int nextWaypoint, out int nextDirection, out Vector3 positionDelta, out float waitTime);
            _renderTime = renderTime;
            _renderPosition += positionDelta;
            _renderWaypoint = nextWaypoint;
            _renderDirection = nextDirection;

            _transform.position = _renderPosition;
            _rigidbody.position = _renderPosition;

            ApplyPositionDelta(positionDelta, 0f);
        }

        public Transform[] GetWayTransforms()
        {
            return _waypoints.Select(x => x.Transform).ToArray();
        }

        public Vector3[] GetWayPoints()
        {
            return _waypoints.Select(x => x.Transform.position).ToArray();
        }

        // MonoBehaviour INTERFACE

        private void Awake()
        {
            _transform = transform;
            _rigidbody = GetComponent<Rigidbody>();

            if (_rigidbody == null)
                throw new NullReferenceException($"GameObject {name} has missing Rigidbody component!");

            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
            _rigidbody.interpolation = RigidbodyInterpolation.None;
            _rigidbody.constraints = RigidbodyConstraints.FreezeAll;
        }

        public override float Priority => float.MaxValue;

        public override EKCCStages GetValidStages(KCC kcc, KCCData data)
        {
            return EKCCStages.SetInputProperties | EKCCStages.OnStay | EKCCStages.OnInterpolate;
        }

        public override void SetInputProperties(KCC kcc, KCCData data)
        {
            // Prediction correction can produce glitches on platforms with higher velocity when direction flips.

            kcc.SuppressFeature(EKCCFeature.PredictionCorrection);
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
                        // Debug.Log($"OnStay:{entity.Id}\n" + JsonUtility.ToJson(entity));

                        entity.Offset = data.TargetPosition - _position;
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
                        entity.Offset = data.TargetPosition - _position;
                        entity.SpaceAlpha = Runner.DeltaTime * _spaceTransitionSpeed + 0.001f;
                        // Debug.Log($"OnStay2:{kcc.Object.Id}\n" + JsonUtility.ToJson(entity));

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

                        data.TargetPosition = Vector3.Lerp(data.TargetPosition, _transform.position + interpolatedOffset, interpolatedSpaceAlpha);
                        mDebugLog[2].text = $"{kcc.Object.Id} iOffset:{Mathf.Atan2(interpolatedOffset.x, interpolatedOffset.z) * Mathf.Rad2Deg:F2}";
                    }

                    break;
                }
            }
        }

        // IMapStatusProvider INTERFACE

        // bool IMapStatusProvider.IsActive(PlayerRef player)
        // {
        //     return true;
        // }

        // string IMapStatusProvider.GetStatus(PlayerRef player)
        // {
        //     if (_waitTime > 0.0f)
        //         return $"{name} - Waiting {_waitTime:F1}s";

        //     string waypointName = _waypoint >= 0 && _waypoint < _waypoints.Length ? _waypoints[_waypoint].Transform.name : "---";
        //     return $"{name} - {Mathf.RoundToInt(CalculateRelativeWaypointDistance(_waypoint, _direction, _transform.position) * 100.0f)}% ({waypointName})";
        // }

        // PRIVATE METHODS

        private void CalculateNextPosition(int baseWaypoint, int baseDirection, Vector3 basePosition, float deltaTime, out int nextWaypoint, out int nextDirection, out Vector3 positionDelta, out float waitTime)
        {
            nextWaypoint = baseWaypoint;
            nextDirection = baseDirection;
            positionDelta = default;
            waitTime = default;

            if (baseWaypoint >= _waypoints.Length)
                return;

            float remainingDistance = _speed * deltaTime;
            while (remainingDistance > 0.0f)
            {
                PlatformWaypoint targetWaypoint = _waypoints[nextWaypoint];
                Vector3 targetDelta = targetWaypoint.Transform.position - basePosition;

                if (targetDelta.sqrMagnitude >= (remainingDistance * remainingDistance))
                {
                    positionDelta += targetDelta.normalized * remainingDistance;
                    break;
                }
                else
                {
                    basePosition += targetDelta;
                    positionDelta += targetDelta;

                    remainingDistance -= targetDelta.magnitude;

                    waitTime = targetWaypoint.WaitTime;

                    if (_mode == EPlatformMode.None)
                    {
                        ++nextWaypoint;
                        if (nextWaypoint >= _waypoints.Length)
                            break;
                    }
                    else if (_mode == EPlatformMode.Looping || _mode == EPlatformMode.Curve)
                    {
                        ++nextWaypoint;
                        nextWaypoint %= _waypoints.Length;
                    }
                    else if (_mode == EPlatformMode.PingPong)
                    {
                        if (nextDirection == 0)
                        {
                            ++nextWaypoint;
                            if (nextWaypoint >= _waypoints.Length)
                            {
                                nextWaypoint = _waypoints.Length - 2;
                                nextDirection = -1;
                            }
                        }
                        else
                        {
                            --nextWaypoint;
                            if (nextWaypoint < 0)
                            {
                                nextWaypoint = 1;
                                nextDirection = 0;
                            }
                        }
                    }
                    else
                    {
                        throw new NotImplementedException(_mode.ToString());
                    }

                    if (waitTime != default)
                        break;
                }
            }
        }

        private float CalculateRelativeWaypointDistance(int nextWaypoint, int direction, Vector3 currentPosition)
        {
            if (_waypoints.Length <= 1)
                return 0.0f;

            int previousWaypoint = nextWaypoint;

            if (_mode == EPlatformMode.None)
            {
                --previousWaypoint;
                if (previousWaypoint < 0)
                    return 0.0f;
            }
            else if (_mode == EPlatformMode.Looping)
            {
                previousWaypoint = (previousWaypoint - 1 + _waypoints.Length) % _waypoints.Length;
            }
            else if (_mode == EPlatformMode.PingPong)
            {
                if (direction == 0)
                {
                    previousWaypoint = (previousWaypoint - 1 + _waypoints.Length) % _waypoints.Length;
                    if (previousWaypoint < 0)
                    {
                        previousWaypoint = 1;
                    }
                }
                else
                {
                    ++previousWaypoint;
                    if (previousWaypoint >= _waypoints.Length)
                    {
                        previousWaypoint = _waypoints.Length - 2;
                    }
                }
            }
            else
            {
                throw new NotImplementedException(_mode.ToString());
            }

            if (previousWaypoint == nextWaypoint)
                return 1.0f;

            Vector3 previousPosition = _waypoints[previousWaypoint].Transform.position;
            Vector3 nextPosition = _waypoints[nextWaypoint].Transform.position;

            float length = Vector3.Distance(previousPosition, nextPosition);
            float distance = Vector3.Distance(previousPosition, currentPosition);

            return length > 0.001f ? Mathf.Clamp01(distance / length) : 1.0f;
        }

        private void ApplyPositionDelta(Vector3 positionDelta, float rotationDelta)
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
                            mDebugLog[3].text = $"kcc.IsProxy :{kcc.Object.Id} {Object.HasStateAuthority}";
                            // Proxies are early interpolated, position delta is already applied to platform transform.
                            kcc.Interpolate();
                            continue;
                        }

                        AdjustFocus aFocus;
                        if (!mAdjustFocus.TryGetValue(kcc.Object.Id.ToString(), out aFocus))
                        {
                            aFocus = new AdjustFocus();
                            mAdjustFocus.Add(kcc.Object.Id.ToString(), aFocus);
                        }

                        aFocus.mTargetPos = kcc.Data.TargetPosition;
                        aFocus.mDeltaPos = positionDelta;

                        // aFocus.mCurvePos = Vector3.zero;

                        if (_mode == EPlatformMode.Curve && !rotationDelta.IsAlmostZero())
                        {
                            var aCurvePos = GetDeltaPosition(kcc.Data.TargetPosition + positionDelta, rotationDelta);

                            mDebugLog[i].text = $"{kcc.Object.Id} pl:{Mathf.Atan2(positionDelta.x, positionDelta.z) * Mathf.Rad2Deg:F2} c:{Mathf.Atan2(aCurvePos.x, aCurvePos.z) * Mathf.Rad2Deg:F2}";
                            positionDelta = aCurvePos + positionDelta;

                            aFocus.mCurvePos = aCurvePos;
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

        Vector3 RotateAroundPoint(Vector3 point, Vector3 pivot, Quaternion angle)
        {
            var finalPos = point - pivot;
            //Center the point around the origin
            finalPos = angle * finalPos;
            //Rotate the point.
            finalPos += pivot;
            //Move the point back to its original offset. 
            return finalPos;
        }

        Vector3 GetDeltaPosition(Vector3 child, float rotationDelta)
        {
            Vector3 aChildDestPos = RotateAroundPoint(child, _position, Quaternion.Euler(0f, rotationDelta, 0f));
            return aChildDestPos - child;
        }

        Vector3[] _curvePath = null;

        CatmullRomSpline mCatmullRomSpline = new CatmullRomSpline
        {
            UseConstantSpeed = true
        };

        public Vector3[] GenerateCurvePath(Vector3[] iWayPoints)
        {
            //build calculated path:
            Vector3[] aResult = new Vector3[iWayPoints.Length + 2];
            //populate calculate path;
            Array.Copy(iWayPoints, 0, aResult, 1, iWayPoints.Length);

            //populate start and end control points:
            //vector3s[0] = vector3s[1] - vector3s[2];
            aResult[0] = aResult[1] + (aResult[1] - aResult[2]);
            aResult[aResult.Length - 1] = aResult[aResult.Length - 2] + (aResult[aResult.Length - 2] - aResult[aResult.Length - 3]);

            //is this a closed, continuous loop? yes? well then so let's make a continuous Catmull-Rom spline!
            if (aResult[1] == aResult[aResult.Length - 2])
            {
                Vector3[] tmpLoopSpline = new Vector3[aResult.Length];
                Array.Copy(aResult, tmpLoopSpline, aResult.Length);
                tmpLoopSpline[0] = tmpLoopSpline[tmpLoopSpline.Length - 3];
                tmpLoopSpline[tmpLoopSpline.Length - 1] = tmpLoopSpline[2];
                aResult = new Vector3[tmpLoopSpline.Length];
                Array.Copy(tmpLoopSpline, aResult, tmpLoopSpline.Length);
            }

            return aResult;
        }

        // DATA STRUCTURES

        [Serializable]
        private sealed class PlatformWaypoint
        {
            public Transform Transform;
            public float WaitTime;
            public Vector3 AngleVelocity;
        }

        private struct PlatformEntity : INetworkStruct
        {
            public NetworkId Id;
            public Vector3 Offset;
            public float SpaceAlpha;
        }

        private enum EPlatformMode
        {
            None = 0,
            Looping = 1,
            PingPong = 2,
            Curve = 3
        }

        Dictionary<string, AdjustFocus> mAdjustFocus = new Dictionary<string, AdjustFocus>();

        public class AdjustFocus
        {
            public Vector3 mTargetPos;
            public Vector3 mDeltaPos;
            public Vector3 mCurvePos;
        }
        public AdjustFocus[] GetAdjustFocus()
        {
            return mAdjustFocus.Values.ToArray();
        }
    }


}
