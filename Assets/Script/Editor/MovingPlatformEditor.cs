
using com.spacepuppy.Geom;
using Fusion.KCC;
using UnityEditor;
using UnityEngine;

// Displays lines of various thickness in the scene view
[CustomEditor(typeof(MovingPlatform))]
public class MovingPlatformEditor : Editor
{

    MovingPlatform mMovingPlatform;
    CatmullRomSpline mCatmullRomSpline;

    Vector3[] mCurve = null;

    private void OnEnable()
    {
        mMovingPlatform = target as MovingPlatform;
        mCatmullRomSpline = new CatmullRomSpline
        {
            UseConstantSpeed = true
        };
        Vector3[] WayPoints = mMovingPlatform.GenerateCurvePath(mMovingPlatform.GetWayPoints());

        for (int i = 0; i < WayPoints.Length; ++i)
        {
            mCatmullRomSpline.AddControlPoint(WayPoints[i]);
        }
    }

    public void OnSceneGUI()
    {
        Handles.color = Color.yellow;
        for (float i = 0; i <= 1f; i += 0.05f)
        {
            Handles.DrawLine(mCatmullRomSpline.GetPosition(i), mCatmullRomSpline.GetPosition(i + 0.05f), 4);
        }
    }
}