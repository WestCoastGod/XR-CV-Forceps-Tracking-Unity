using UnityEngine;

// Lightweight One Euro filters for smoothing
public class OneEuroFilter
{
    protected float minCutoff = 1.0f;
    protected float beta = 0.0f;
    protected float dCutoff = 1.0f;
    protected bool initialized = false;
    protected float lastTime = -1f;

    protected static float Alpha(float cutoff, float dt)
    {
        float tau = 1.0f / (2.0f * Mathf.PI * cutoff);
        return 1.0f / (1.0f + tau / dt);
    }
}

public class OneEuroFilterVec3 : OneEuroFilter
{
    private Vector3 xPrev;
    private Vector3 dxPrev;

    public OneEuroFilterVec3(float minCutoff = 1.0f, float beta = 0.0f, float dCutoff = 1.0f)
    {
        this.minCutoff = minCutoff;
        this.beta = beta;
        this.dCutoff = dCutoff;
    }

    public Vector3 Filter(Vector3 x, float dt)
    {
        if (!initialized)
        {
            xPrev = x;
            dxPrev = Vector3.zero;
            initialized = true;
            return x;
        }

        // Derivative of the signal
        Vector3 dx = (x - xPrev) / Mathf.Max(1e-4f, dt);
        float aD = Alpha(dCutoff, dt);
        dxPrev = Vector3.Lerp(dxPrev, dx, aD);

        // Adaptive cutoff
        float cutoff = minCutoff + beta * dxPrev.magnitude;
        float a = Alpha(cutoff, dt);
        xPrev = Vector3.Lerp(xPrev, x, a);
        return xPrev;
    }
}

public class OneEuroFilterQuat : OneEuroFilter
{
    private Quaternion qPrev;
    private Vector3 wPrev; // angular velocity approximation

    public OneEuroFilterQuat(float minCutoff = 1.0f, float beta = 0.0f, float dCutoff = 1.0f)
    {
        this.minCutoff = minCutoff;
        this.beta = beta;
        this.dCutoff = dCutoff;
    }

    public Quaternion Filter(Quaternion q, float dt)
    {
        if (!initialized)
        {
            qPrev = q;
            wPrev = Vector3.zero;
            initialized = true;
            return q;
        }

        // Approximate angular velocity via delta quaternion to axis-angle
        Quaternion dq = q * Quaternion.Inverse(qPrev);
        dq.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f; // shortest path
        Vector3 w = axis.normalized * (angle * Mathf.Deg2Rad) / Mathf.Max(1e-4f, dt);
        float aD = Alpha(dCutoff, dt);
        wPrev = Vector3.Lerp(wPrev, w, aD);

        float cutoff = minCutoff + beta * wPrev.magnitude;
        float a = Alpha(cutoff, dt);
        qPrev = Quaternion.Slerp(qPrev, q, a);
        return qPrev;
    }
}
