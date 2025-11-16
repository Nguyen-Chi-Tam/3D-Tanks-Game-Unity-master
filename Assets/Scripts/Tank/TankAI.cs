// 08/11/2025 AI-Tag
// Basic AI controller for 1v1 PvE mode. Moves toward the enemy tank and fires when in line of sight.
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

public class TankAI : MonoBehaviour
{
    private GameManager _gameManager;
    private TankManager _selfManager;
    private TankShooting _shooting; // May be null if not assigned
    private TankMovement _movementSource; // Reference for speed/turn settings
    private Rigidbody _rb;

    [Header("Movement Settings")]
    public float desiredDistance = 15f; // Preferred distance to enemy
    public float minDistance = 8f;      // If closer than this, back up a little
    public float moveSpeedMultiplier = 1f; // Allows tweaking relative speed
    public float turnSpeedMultiplier = 1f; // Allows tweaking relative turn speed

    [Header("Firing Settings")]
    public float fireRange = 35f;
    public float fireCooldown = 1.5f;
    public LayerMask lineOfSightMask = Physics.DefaultRaycastLayers;
    public float aimToleranceDegrees = 6f;  // how aligned we must be to shoot
    public float maxLeadTime = 3f;          // clamp prediction time

    private float _nextFireTime;
    private readonly Dictionary<Transform, Vector3> _lastPos = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, float> _lastTime = new Dictionary<Transform, float>();

    [Header("Strafing & Avoidance")]
    public float strafeDistanceBand = 4f;   // +/- around desired distance where we strafe instead of advancing/retreating
    public float strafeSwitchInterval = 2.5f; // seconds between strafe side switches
    public float obstacleAvoidDistance = 5f;  // raycast distance to probe for obstacles
    public LayerMask obstacleMask;            // if 0, will use lineOfSightMask

    private int _strafeSign = 1;
    private float _nextStrafeSwitch;

    public void Initialize(GameManager gm, TankManager self, TankShooting shooting, TankMovement movementSource)
    {
        _gameManager = gm;
        _selfManager = self;
        _shooting = shooting;
        _movementSource = movementSource;
        _rb = GetComponent<Rigidbody>();
        if (obstacleMask == 0) obstacleMask = lineOfSightMask;
    }

    private void Update()
    {
        var target = AcquireTarget();
        if (target == null) return;

        HandleMovement(target);
        HandleFire(target);
    }

    private Transform AcquireTarget()
    {
        // Multiplayer-friendly fallback: pick nearest opposite-team TankTeam in scene.
        if (_gameManager == null || _gameManager.m_Tanks == null)
        {
            int myTeam = -1;
            var tt = GetComponent<TankTeam>();
            if (tt != null) myTeam = tt.TeamId;
            Transform closest = null;
            float best = float.MaxValue;
            var all = Object.FindObjectsByType<TankTeam>(FindObjectsSortMode.None);
            foreach (var other in all)
            {
                if (other == null || other.gameObject == this.gameObject) continue;
                if (!other.gameObject.activeSelf) continue;
                if (myTeam >= 0 && other.TeamId == myTeam) continue;
                float d = Vector3.Distance(transform.position, other.transform.position);
                if (d < best)
                {
                    best = d;
                    closest = other.transform;
                }
            }
            return closest;
        }
        else
        {
            Transform closest = null;
            float closestDist = float.MaxValue;
            foreach (var tm in _gameManager.m_Tanks)
            {
                if (tm == null || tm == _selfManager) continue;
                if (tm.m_Instance == null || !tm.m_Instance.activeSelf) continue;
                if (tm.m_TeamId == _selfManager.m_TeamId && _selfManager.m_TeamId >= 0) continue;
                float d = Vector3.Distance(transform.position, tm.m_Instance.transform.position);
                if (d < closestDist)
                {
                    closestDist = d;
                    closest = tm.m_Instance.transform;
                }
            }
            return closest;
        }
    }

    private void HandleMovement(Transform target)
    {
        if (_rb == null) return;
        Vector3 toTarget = target.position - transform.position;
        float distance = toTarget.magnitude;

        // Decide aim-facing direction (predicted) and rotate toward it on XZ
        Vector3 aimDir3D;
        float aimDist;
        bool hasAim = GetPredictedAim(target, out aimDir3D, out aimDist);
        Vector3 aimFlatDir = hasAim ? new Vector3(aimDir3D.x, 0f, aimDir3D.z).normalized
                                     : new Vector3(toTarget.x, 0f, toTarget.z).normalized;
        if (aimFlatDir.sqrMagnitude > 0.0001f)
        {
            Quaternion desired = Quaternion.LookRotation(aimFlatDir, Vector3.up);
            float turnSpeed = (_movementSource ? _movementSource.m_TurnSpeed : 180f) * turnSpeedMultiplier;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, turnSpeed * Time.deltaTime);
        }

        // Decide movement
        float speed = (_movementSource ? _movementSource.m_Speed : 12f) * moveSpeedMultiplier;
        Vector3 moveDir = Vector3.zero;

        // Switch strafing side periodically
        if (Time.time >= _nextStrafeSwitch)
        {
            _strafeSign = -_strafeSign;
            _nextStrafeSwitch = Time.time + strafeSwitchInterval;
        }

        // Radial component (advance/retreat to maintain distance band)
        if (distance > desiredDistance + strafeDistanceBand)
            moveDir += transform.forward; // advance
        else if (distance < desiredDistance - strafeDistanceBand)
            moveDir += -transform.forward; // retreat

        // Strafe component (perpendicular to current facing)
        Vector3 strafe = Vector3.Cross(Vector3.up, transform.forward) * _strafeSign;
        moveDir += strafe;

        // Obstacle avoidance influence
        moveDir += ComputeAvoidance();

        // Normalize and move
        if (moveDir.sqrMagnitude > 0.001f)
        {
            moveDir = new Vector3(moveDir.x, 0f, moveDir.z).normalized;
            _rb.MovePosition(_rb.position + moveDir * speed * Time.deltaTime);
        }
    }

    private void HandleFire(Transform target)
    {
        if (_shooting == null) return; // no shooting capability
        if (Time.time < _nextFireTime) return;

        // Predict aim and check range/LOS
        Vector3 aimDir;
        float predDist;
        if (!GetPredictedAim(target, out aimDir, out predDist)) return;
        if (predDist > fireRange) return;

        // Ensure we're roughly aligned before firing
        float angle = Vector3.Angle(new Vector3(transform.forward.x, 0f, transform.forward.z), new Vector3(aimDir.x, 0f, aimDir.z));
        if (angle > aimToleranceDegrees) return;

        // Check line of sight toward predicted point
        Ray ray = new Ray(_shooting.m_FireTransform.position, aimDir);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Min(fireRange, predDist + 0.5f), lineOfSightMask, QueryTriggerInteraction.Ignore))
        {
            // If something else blocks between us and the predicted point, don't fire yet
            if (hit.transform != target) return;
        }

        // Fire
        if (_shooting.m_Shell != null && _shooting.m_FireTransform != null)
        {
            Rigidbody shellInstance = Object.Instantiate(_shooting.m_Shell, _shooting.m_FireTransform.position, _shooting.m_FireTransform.rotation) as Rigidbody;
            float launchForce = _shooting.m_MaxLaunchForce;
            shellInstance.linearVelocity = launchForce * _shooting.m_FireTransform.forward;
            if (_shooting.m_ShootingAudio && _shooting.m_FireClip)
            {
                _shooting.m_ShootingAudio.clip = _shooting.m_FireClip;
                _shooting.m_ShootingAudio.Play();
            }
        }
        _nextFireTime = Time.time + fireCooldown;
    }

    private bool GetPredictedAim(Transform target, out Vector3 aimDir, out float predictedDistance)
    {
        aimDir = Vector3.zero; predictedDistance = 0f;
        if (_shooting == null || _shooting.m_FireTransform == null || target == null) return false;

        Vector3 firePos = _shooting.m_FireTransform.position;
        Vector3 targetPos = target.position;
        Vector3 r = targetPos - firePos;

        Vector3 v = GetTargetVelocity(target);
        float s = Mathf.Max(0.1f, _shooting.m_MaxLaunchForce);

        float a = Vector3.Dot(v, v) - s * s;
        float b = 2f * Vector3.Dot(r, v);
        float c = Vector3.Dot(r, r);

        float t = 0f;
        if (Mathf.Abs(a) < 1e-4f)
        {
            if (Mathf.Abs(b) > 1e-4f)
                t = Mathf.Clamp(-c / b, 0f, maxLeadTime);
            else
                t = 0f;
        }
        else
        {
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return false;
            float sqrt = Mathf.Sqrt(disc);
            float t1 = (-b - sqrt) / (2f * a);
            float t2 = (-b + sqrt) / (2f * a);
            // choose smallest positive time
            t = Mathf.Min(t1 > 0f ? t1 : float.PositiveInfinity, t2 > 0f ? t2 : float.PositiveInfinity);
            if (!float.IsFinite(t)) return false;
            t = Mathf.Min(t, maxLeadTime);
        }

        Vector3 aimPoint = targetPos + v * t;
        Vector3 dir = (aimPoint - firePos);
        predictedDistance = dir.magnitude;
        if (predictedDistance < 0.001f) return false;
        aimDir = dir / predictedDistance;
        return true;
    }

    private Vector3 GetTargetVelocity(Transform target)
    {
        Rigidbody rb = target.GetComponent<Rigidbody>();
        if (rb != null) return rb.linearVelocity;

        float now = Time.time;
        Vector3 prevPos;
        float prevTime;
        if (_lastPos.TryGetValue(target, out prevPos) && _lastTime.TryGetValue(target, out prevTime))
        {
            float dt = Mathf.Max(0.0001f, now - prevTime);
            Vector3 vel = (target.position - prevPos) / dt;
            _lastPos[target] = target.position;
            _lastTime[target] = now;
            return vel;
        }
        else
        {
            _lastPos[target] = target.position;
            _lastTime[target] = now;
            return Vector3.zero;
        }
    }

    private Vector3 ComputeAvoidance()
    {
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        float dist = obstacleAvoidDistance;
        Vector3 avoid = Vector3.zero;

        Vector3 dirF = transform.forward;
        Vector3 dirL = Quaternion.Euler(0f, -30f, 0f) * transform.forward;
        Vector3 dirR = Quaternion.Euler(0f, 30f, 0f) * transform.forward;

        if (Physics.Raycast(origin, dirF, out RaycastHit hitF, dist, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            avoid += Vector3.ProjectOnPlane(hitF.normal, Vector3.up).normalized;
        }
        if (Physics.Raycast(origin, dirL, out RaycastHit hitL, dist * 0.8f, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            avoid += Vector3.ProjectOnPlane(hitL.normal, Vector3.up).normalized;
        }
        if (Physics.Raycast(origin, dirR, out RaycastHit hitR, dist * 0.8f, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            avoid += Vector3.ProjectOnPlane(hitR.normal, Vector3.up).normalized;
        }

        // Bias a little towards current strafe direction if straight ahead is blocked
        if (avoid.sqrMagnitude > 0.001f && Physics.Raycast(origin, dirF, dist * 0.6f, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            avoid += Vector3.Cross(Vector3.up, transform.forward) * _strafeSign * 0.5f;
        }
        return new Vector3(avoid.x, 0f, avoid.z);
    }
}
