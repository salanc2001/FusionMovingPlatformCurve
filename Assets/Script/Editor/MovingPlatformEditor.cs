
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

        UpdateSpline();
    }

    void UpdateSpline()
    {
        mCatmullRomSpline.ClearPoint();

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

        if (Application.isPlaying)
            return;

        Transform[] WayTransforms = mMovingPlatform.GetWayTransforms();
        for (int i = 0; i < WayTransforms.Length; i++)
        {
            EditorGUI.BeginChangeCheck();
            WayTransforms[i].position = Handles.PositionHandle(WayTransforms[i].position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
                UpdateSpline();
        }
    }
}