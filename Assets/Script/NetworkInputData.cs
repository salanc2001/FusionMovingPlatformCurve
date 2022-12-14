using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

public enum InputButtons
{
    Jump,
    Fire
}

public struct NetworkInputData : INetworkInput
{
    public NetworkButtons buttons;

    public Vector3 movementInput;


}
