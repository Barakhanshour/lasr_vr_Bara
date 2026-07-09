using System;
using UnityEngine;

[Serializable]
public class SwingingPaintExperimentData
{
    public int formatVersion = 1;
    public string experimentId;
    public string savedAtIso;
    public string imageFileName;

    public float bucketDryMassKg;
    public float bucketBottomRadiusMeters;
    public float bucketTopRadiusMeters;
    public float bucketHeightMeters;

    public float initialPaintVolumeLiters;
    public float remainingPaintVolumeLiters;
    public float usedPaintVolumeLiters;
    public float paintDensityKgPerLiter;
    public float holeDiameterMillimeters;
    public float viscosity01;
    public Color paintColor;

    public float ropeLengthMeters;
    public float ropeSpringConstant;
    public float startAngleDegrees;
    public float startDirectionDegrees;
    public Vector3 initialHandleVelocity;
    public float gravity;
    public Vector3 windAcceleration;

    public int surfaceTypeIndex;
    public string surfaceTypeName;
    public Vector2 canvasSizeMeters;
    public float canvasTiltX;
    public float canvasTiltZ;

    public int particleCount;
    public int gridResolution;

    public float experimentTimeSeconds;
    public float maximumBucketSpeed;
    public float maximumRopeTensionNewton;
    public int swingCount;
    public float coveragePercent;
    public float paintedAreaSquareMeters;
}
