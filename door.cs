using UnityEngine;

public enum DoorDirection { North, East, South, West }

public class Door : MonoBehaviour
{
    // Set this in the Inspector for each door.
    public DoorDirection doorDirection;
}

