using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Plate : MonoBehaviour
{
    public GameObject Player;
    void OnTriggerEnter(Collider other)
    {
        Debug.Log(other.gameObject.name);
        if (other.gameObject.name.StartsWith("Player"))
        {
            other.transform.SetParent(transform);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject.name.StartsWith("Player"))
        {
            other.transform.SetParent(null);
        }
    }
}
