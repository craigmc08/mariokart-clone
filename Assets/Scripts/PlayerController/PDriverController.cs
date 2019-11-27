﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PDriverController : MonoBehaviour
{
    [Header("Speed")]
    public float maxForwardSpeed;
    public float forwardAcceleration;
    public float maxBackwardSpeed;
    public float backwardAcceleration;
    [Space(10)]
    public float boostingAcceleration;
    [Space(10)]
    public float frictionCoF;

    [Header("Turning")]
    public float kartWeight = 1;
    public float turningForce;
    public float minTurnRadius = 1;

    [Header("Drifting/Hopping")]
    public float hopSpeed;
    public float driftInitiateThreshold = 0.2f;
    [Tooltip("After pressing the hop button, the hop will be remembered on the ground for this amount of time")]
    public float hopExtensionTime = 0.1f;
    public bool insideDrift = true;
    [Tooltip("Multiplies the maximum turning force by this value. Higher means tighter turns")]
    public float driftTightness = 3;
    public float outsideSlippiness = 10;
    public Vector2 driftSteerRange;

    [Header("Mini-Turbo Charging")]
    public float chargeForMiniturbo = 250;
    public float chargeForSuperturbo = 450;
    public float chargeForUltraturbo = 650;
    public bool canUltraTurbo = false;
    [Space(10)]
    public float tightChargeRate = 150;
    public float looseChargeRate = 50;
    [Tooltip("If the steering input is higher than this value (adjusted for drift direction), then the turn is considered 'tight'.")]
    [Range(-1, 1)]
    public float driftTightnessThreshold = 0.5f;
    [Space(10)]
    public float miniturboDuration = 0.5f;
    public float superturboDuration = 1f;
    public float ultraturboDuration = 1.5f;
    [Space(10)]
    [Tooltip("Multiplier on base speed while mini/super/ultra-turboing")]
    public float miniturboBoost = 2f;

    [Header("Physics")]
    [Range(0,1)]
    [Tooltip("How fast the surface normal can change")]
    public float surfaceSmoothing = 0.2f;
    [Space(10)]
    public LayerMask groundLayerMask;

    [Header("Wheels And Body")]
    public Transform frontAxle;
    public Transform backAxle;
    public Transform steeringAssembly;
    public float frontWheelRadii;
    public float backWheelRadii;
    public float bodyLength;
    public float maxFrontAxleYaw = 15f;
    [Range(0, 1)]
    public float frontAxleYawSmoothing = 0.8f;

    [Header("Drift Particles")]
    public ParticleSystem[] driftParticles;
    public ParticleSystem.MinMaxGradient driftingColor;
    public ParticleSystem.MinMaxGradient miniturboChargeColor;
    public ParticleSystem.MinMaxGradient superturboChargeColor;
    public ParticleSystem.MinMaxGradient ultraturboChargeColor;

    [Header("Nitro Particles")]
    public ParticleSystem[] nitroParticles;

    private float m_Accelerate = 0;
    private float m_Decelerate = 0;
    private float m_Steer;
    private bool m_Hop;

    private Vector2 m_Lean;
    private bool m_Trick;

    private Rigidbody rb;

    // Driving stuff
    public bool grounded = false;
    Vector3 contactNormal = Vector3.up;
    Vector3 surfaceNormal = Vector3.up;
    Vector3 surfaceForward = Vector3.forward;

    // Drifting stuff
    bool lastHopVal = false;
    bool readyToDrift = false;
    float hoppedAt = 0;

    bool drifting = false;
    bool driftingRight = false;
    float turboCharge = 0;

    bool turboActivated = false;
    float turboStartedAt = 0;
    float turboDuration = 0;
    float turboBoost = 0;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = kartWeight;
    }

    private void FixedUpdate()
    {
        canUltraTurbo = !insideDrift;

        var velocity = rb.velocity;

        // ROAD SURFACE
        surfaceNormal = Vector3.Slerp(contactNormal, surfaceNormal, surfaceSmoothing);
        surfaceForward = Vector3.ProjectOnPlane(surfaceForward, surfaceNormal).normalized;

        // DRIFTING
        if (lastHopVal != m_Hop && m_Hop && !readyToDrift && !drifting)
        {
            // Only hop if the button was pressed this update
            if (grounded)
            {
                velocity += surfaceNormal * hopSpeed;
                hoppedAt = Time.time;
            }
            readyToDrift = true;
        }

        if (readyToDrift && grounded && Time.time > hoppedAt + hopExtensionTime)
        {
            if (Mathf.Abs(m_Steer) > driftInitiateThreshold)
            {
                // Initiate drift
                drifting = true;
                driftingRight = m_Steer > 0;
                turboCharge = 0;
            }
            // Don't initiate drift
            readyToDrift = false;
        }

        if (drifting && !m_Hop)
        {
            drifting = false;
            EndDrift();
        }

        if (drifting)
        {
            var driftSteer = m_Steer * (driftingRight ? 1 : -1);

            if (grounded)
            {
                if (driftSteer > driftTightnessThreshold)
                {
                    turboCharge += tightChargeRate * Time.fixedDeltaTime;
                } else
                {
                    turboCharge += looseChargeRate * Time.fixedDeltaTime;
                }
            }
        }


        // ROTATION
        // Minimum turn radius required to prevent div by 0 errors
        float turnRadius = Mathf.Max(velocity.sqrMagnitude / turningForce, minTurnRadius);
        float turnSpeed;
        if (!drifting)
        {
            turnSpeed = velocity.magnitude / turnRadius * m_Steer;
        } else
        {
            var steerMin = driftingRight ? driftSteerRange.x : -driftSteerRange.y;
            var steerMax = driftingRight ? driftSteerRange.y : -driftSteerRange.x;
            turnRadius /= driftTightness;
            var driftSteer = Mathf.Lerp(steerMin, steerMax, (m_Steer + 1f) / 2f);
            turnSpeed = velocity.magnitude / turnRadius * driftSteer;
        }
        surfaceForward = Quaternion.AngleAxis(turnSpeed * Mathf.Rad2Deg * Time.fixedDeltaTime, surfaceNormal) * surfaceForward;

        Quaternion kartRotation = Quaternion.LookRotation(surfaceForward, surfaceNormal);
        transform.rotation = kartRotation;

        // MOVEMENT
        var surfaceRight = Vector3.Cross(surfaceForward, surfaceNormal);

        var friction = -velocity.normalized * frictionCoF;
        var slipVelocity = Vector3.Dot(velocity, surfaceRight);
        var gripAmount = !drifting ? turningForce
                         : insideDrift ? turningForce * driftTightness : turningForce / outsideSlippiness;
        var gripStrength = Mathf.Sign(slipVelocity) * Mathf.Min(Mathf.Abs(slipVelocity), gripAmount);
        // gripForce should immediately cancel out all slip velocity
        var gripForce = gripStrength * -surfaceRight / Time.fixedDeltaTime;

        var accelInput = m_Accelerate - m_Decelerate;
        var acceleration = accelInput * (accelInput < 0 ? backwardAcceleration : forwardAcceleration) * surfaceForward;
        if (turboActivated) acceleration *= turboBoost;

        var totalAcceleration = friction + gripForce + acceleration;
        // All of this acceleration should be in the surface plane
        totalAcceleration = Vector3.ProjectOnPlane(totalAcceleration, surfaceNormal);
        velocity += totalAcceleration * Time.fixedDeltaTime;

        var forwardsComponent = Vector3.Dot(velocity, surfaceForward);
        var movingForwards = forwardsComponent > 0;

        var upwardsComponent = Vector3.Dot(velocity, surfaceNormal);
        var tangentialVelocity = velocity - upwardsComponent * surfaceNormal;
        var maxSpeedMultiplier = turboActivated ? turboBoost : 1;
        if (movingForwards && forwardsComponent > maxForwardSpeed * maxSpeedMultiplier)
        {
            tangentialVelocity *= maxForwardSpeed * maxSpeedMultiplier / forwardsComponent;
        } else if (!movingForwards && forwardsComponent < -maxBackwardSpeed * maxSpeedMultiplier)
        {
            tangentialVelocity *= -maxBackwardSpeed * maxSpeedMultiplier / forwardsComponent;
        }
        velocity = tangentialVelocity + upwardsComponent * surfaceNormal;

        rb.velocity = velocity;

        // WHEEL VISUALS
        float frontWheelSpeed = forwardsComponent / frontWheelRadii;
        Quaternion frontWheelRotation = Quaternion.AngleAxis(frontWheelSpeed * Mathf.Rad2Deg * Time.fixedDeltaTime, Vector3.right);
        float backWheelSpeed = forwardsComponent / backWheelRadii;
        Quaternion backWheelRotation = Quaternion.AngleAxis(backWheelSpeed * Mathf.Rad2Deg * Time.fixedDeltaTime, Vector3.right);

        float currentFrontY = steeringAssembly.localEulerAngles.y;
        if (currentFrontY > 180) currentFrontY -= 360;
        float frontAxleAngle = Mathf.Lerp(-maxFrontAxleYaw, maxFrontAxleYaw, (m_Steer + 1) / 2);
        frontAxleAngle = Mathf.Lerp(frontAxleAngle, currentFrontY, frontAxleYawSmoothing);
        frontAxleAngle = Mathf.Clamp(frontAxleAngle, -maxFrontAxleYaw, maxFrontAxleYaw);
        Quaternion frontAxleRotation = Quaternion.AngleAxis(frontAxleAngle, Vector3.up);

        backAxle.localRotation *= backWheelRotation;
        float currentFrontX = frontAxle.localEulerAngles.x;
        frontAxle.localRotation *= frontWheelRotation;

        steeringAssembly.localRotation = frontAxleRotation;

        m_Trick = false;
        lastHopVal = m_Hop;

        UpdateParticles();
        UpdateTurbo();
    }

    void UpdateParticles()
    {
        // DRIFTING PARTICLES
        bool utCharged = drifting && turboCharge > chargeForUltraturbo && canUltraTurbo;
        bool stCharged = drifting && turboCharge > chargeForSuperturbo;
        bool mtCharged = drifting && turboCharge > chargeForMiniturbo;

        bool shouldPlay = drifting && grounded;
        ParticleSystem.MinMaxGradient driftColor = default;
        if (utCharged)
        {
            driftColor = ultraturboChargeColor;
        } else if (stCharged)
        {
            driftColor = superturboChargeColor;
        } else if (mtCharged)
        {
            driftColor = miniturboChargeColor;
        } else if (drifting)
        {
            driftColor = driftingColor;
        }

        if (shouldPlay)
        {
            foreach (var p in driftParticles)
            {
                var colm = p.colorOverLifetime;
                colm.color = driftColor;
                p.Play();
            }
        } else
        {
            foreach (var p in driftParticles)
            {
                p.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        // NITRO PARTICLES
        foreach (var p in nitroParticles)
        {
            if (turboActivated) p.Play();
            else p.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    void EndDrift()
    {
        bool utCharged = turboCharge > chargeForUltraturbo && canUltraTurbo;
        bool stCharged = turboCharge > chargeForSuperturbo;
        bool mtCharged = turboCharge > chargeForMiniturbo;

        if (mtCharged || stCharged || utCharged)
        {
            turboActivated = true;
            turboStartedAt = Time.time;
        }
        if (utCharged)
        {
            turboDuration = ultraturboDuration;
            turboBoost = miniturboBoost;
        } else if (stCharged)
        {
            turboDuration = superturboDuration;
            turboBoost = miniturboBoost;
        } else if (mtCharged)
        {
            turboDuration = miniturboDuration;
            turboBoost = miniturboBoost;
        }
    }

    void UpdateTurbo()
    {
        if (turboStartedAt + turboDuration < Time.time)
        {
            turboActivated = false;
            turboDuration = 0;
            turboBoost = 0;
        }
    }

    public void OnAccelerate(InputValue value)
    {
        m_Accelerate = value.Get<float>();
    }

    public void OnDecelerate(InputValue value)
    {
        m_Decelerate = value.Get<float>();
    }

    public void OnSteer(InputValue value)
    {
        m_Steer = value.Get<float>();
    }

    public void OnHop(InputValue value)
    {
        m_Hop = value.Get<float>() > 0.5f;
    }

    public void OnLean(InputValue value)
    {
        m_Lean = value.Get<Vector2>();
    }

    public void OnTrick(InputValue value)
    {
        m_Trick = value.Get<float>() > 0.5f;
    }

    bool IsInLayerMask(LayerMask mask, int layer)
    {
        return mask == (mask | (1 << layer));
    }

    private ContactPoint[] surfaceContacts = new ContactPoint[16];
    public void OnCollisionStay(Collision col)
    {
        if (IsInLayerMask(groundLayerMask, col.gameObject.layer)) {
            // Find average direction of each contact normal
            int length = col.GetContacts(surfaceContacts);
            Vector3 totalNormal = Vector3.zero;
            for (var i = 0; i < length; i++)
            {
                totalNormal += surfaceContacts[i].normal;
            }
            grounded = true;
            contactNormal = totalNormal.normalized;
        }
    }

    public void OnCollisionEnter(Collision col)
    {
        if (IsInLayerMask(groundLayerMask, col.gameObject.layer))
        {
            grounded = true;
        }
    }

    public void OnCollisionExit(Collision col)
    {
        if (IsInLayerMask(groundLayerMask, col.gameObject.layer))
        {
            grounded = false;
        }
    }
}