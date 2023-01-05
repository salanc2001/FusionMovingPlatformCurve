
using System.Collections.Generic;
using com.spacepuppy.Geom;
using Fusion.KCC;
using UnityEditor;
using UnityEngine;

// Displays lines of various thickness in the scene view
[CustomEditor(typeof(MovingPlatform))]
public class MovingPlatformEditor : Editor
{

    MovingPlatform me;
    CatmullRomSpline mCatmullRomSpline;

    Vector3[] mCurve = null;

    private void OnEnable()
    {
        me = target as MovingPlatform;
        mCatmullRomSpline = new CatmullRomSpline
        {
            UseConstantSpeed = true
        };

        UpdateSpline();
    }

    void UpdateSpline()
    {
        mCatmullRomSpline.ClearPoint();

        Vector3[] WayPoints = me.GenerateCurvePath(me.GetWayPoints());

        for (int i = 0; i < WayPoints.Length; ++i)
        {
            mCatmullRomSpline.AddControlPoint(WayPoints[i]);
        }
        List<Vector3> aSpline = new List<Vector3>();
        for (float i = 0; i <= 1f; i += 0.05f)
        {
            aSpline.Add(mCatmullRomSpline.GetPosition(i));
        }

        mSpline = aSpline.ToArray();
    }

    Vector3[] mSpline;

    public void OnSceneGUI()
    {
        Handles.color = Color.yellow;
        for (int i = 0; i < mSpline.Length - 1; i++)
        {
            Handles.DrawLine(mSpline[i], mSpline[i + 1], 4);
        }
        Handles.DrawLine(mSpline[mSpline.Length - 1], mSpline[0], 4);

        MovingPlatform.AdjustFocus[] aFocus = me.GetAdjustFocus();

        foreach (var f in aFocus)
        {
            Handles.color = Color.cyan;
            Handles.DrawLine(f.mTargetPos, f.mTargetPos + f.mDeltaPos * 1000, 4);
            Handles.color = Color.green;
            Handles.DrawLine(f.mTargetPos, f.mTargetPos + f.mCurvePos * 1000, 4);
        }

        if (Application.isPlaying)
            return;

        Transform[] WayTransforms = me.GetWayTransforms();
        for (int i = 0; i < WayTransforms.Length; i++)
        {
            EditorGUI.BeginChangeCheck();
            WayTransforms[i].position = Handles.PositionHandle(WayTransforms[i].position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
                UpdateSpline();
        }


    }
}