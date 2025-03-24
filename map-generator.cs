using System.Collections.Generic;
using UnityEngine;

public class RoomGeneration : MonoBehaviour
{
    [Header("Room Generation Settings")]
    [Tooltip("List of normal room prefabs (must have a BoxCollider and Door children)")]
    public List<GameObject> roomPrefabs;

    [Tooltip("The special generator room prefab (destination room)")]
    public GameObject generatorRoomPrefab;

    [Tooltip("The starting room prefab")]
    public GameObject startRoomPrefab;

    [Tooltip("Total number of rooms to generate (including start and finish)")]
    public int totalRooms = 10;

    [Tooltip("Tolerance for door alignment (in world units)")]
    public float doorSnapTolerance = 0.2f;

    // Lists to track placed rooms and open door connection points.
    private List<PlacedRoom> placedRooms = new List<PlacedRoom>();
    private List<OpenDoor> openDoors = new List<OpenDoor>();

    void Start()
    {
        GenerateDungeon();
        RemoveUnusedDoors();
    }

    void GenerateDungeon()
    {
        // 1) Place the start room at the origin.
        GameObject startRoom = Instantiate(startRoomPrefab, Vector3.zero, Quaternion.identity);
        PlacedRoom startPlacedRoom = new PlacedRoom(startRoom);
        placedRooms.Add(startPlacedRoom);
        AddRoomDoorsToOpenList(startPlacedRoom);
        Debug.Log("Start room placed.");

        bool generatorPlaced = false;

        // 2) Use a "wave BFS" approach until we reach totalRooms or run out of open doors.
        while (placedRooms.Count < totalRooms && openDoors.Count > 0)
        {
            int roomsBeforeWave = placedRooms.Count;
            // Copy current open doors (this wave) without clearing them immediately.
            List<OpenDoor> currentWave = new List<OpenDoor>(openDoors);

            foreach (OpenDoor doorToUse in currentWave)
            {
                if (placedRooms.Count >= totalRooms)
                    break;

                bool roomAttached = false;
                // Decide which prefab to use (random).
                GameObject candidatePrefab;
                if (!generatorPlaced && placedRooms.Count == totalRooms - 1)
                    candidatePrefab = generatorRoomPrefab;
                else
                    candidatePrefab = roomPrefabs[Random.Range(0, roomPrefabs.Count)];

                // Get all Door components from the candidate prefab.
                Door[] candidateDoors = candidatePrefab.GetComponentsInChildren<Door>();
                foreach (Door candidateDoor in candidateDoors)
                {
                    // Doors to connect must be opposite directions.
                    if (!IsOppositeDirection(candidateDoor.doorDirection, doorToUse.doorDirection))
                        continue;

                    // Randomize the angles [0, 90, 180, 270].
                    // This prevents fractal-like generation
                    int[] angles = new int[] { 0, 90, 180, 270 };
                    for (int i = 0; i < angles.Length; i++)
                    {
                        int randIndex = Random.Range(i, angles.Length);
                        int temp = angles[i];
                        angles[i] = angles[randIndex];
                        angles[randIndex] = temp;
                    }

                    foreach (int angle in angles)
                    {
                        Quaternion candidateRotation = Quaternion.Euler(0, angle, 0);
                        Vector3 candidateDoorDir = GetDirectionVector(candidateDoor.doorDirection);
                        Vector3 rotatedCandidateDoorDir = candidateRotation * candidateDoorDir;
                        Vector3 openDoorDirVec = GetDirectionVector(doorToUse.doorDirection);

                        // Check alignment – the candidate door must face opposite to the open door.
                        if (Vector3.Distance(rotatedCandidateDoorDir, -openDoorDirVec) < doorSnapTolerance)
                        {
                            // Compute the position so that the candidate door aligns with the open door.
                            Vector3 candidateDoorLocalPos = candidateDoor.transform.localPosition;
                            Vector3 rotatedCandidateDoorPos = candidateRotation * candidateDoorLocalPos;
                            Vector3 candidateRoomPos = doorToUse.position - rotatedCandidateDoorPos;

                            GameObject candidateRoomInstance = Instantiate(candidatePrefab, candidateRoomPos, candidateRotation);
                            PlacedRoom candidatePlacedRoom = new PlacedRoom(candidateRoomInstance);

                            if (!IsOverlapping(candidatePlacedRoom))
                            {
                                // Mark both connecting doors as used.
                                doorToUse.doorScript.isUsed = true;
                                candidateDoor.isUsed = true;

                                placedRooms.Add(candidatePlacedRoom);
                                roomAttached = true;
                                Debug.Log($"Room placed: {candidatePrefab.name} at {candidateRoomPos}");

                                // Add all other doors of the new room.
                                AddRoomDoorsToOpenList(candidatePlacedRoom, candidateDoor);

                                if (candidatePrefab == generatorRoomPrefab)
                                    generatorPlaced = true;

                                break; // Stop trying further angles.
                            }
                            else
                            {
                                Debug.Log($"Overlap detected for {candidatePrefab.name} at {candidateRoomPos}");
                                Destroy(candidateRoomInstance);
                            }
                        }
                    }
                    if (roomAttached)
                        break;
                }
                // If a room was attached from this door, remove it from openDoors.
                if (roomAttached)
                    openDoors.Remove(doorToUse);
            }

            if (placedRooms.Count == roomsBeforeWave)
            {
                Debug.LogWarning("No new rooms added this wave. Ending generation to avoid infinite loop.");
                break;
            }
        }

        // 3) Force placement of generator room if it hasn't been placed.
        if (!generatorPlaced && placedRooms.Count < totalRooms)
        {
            Debug.LogWarning("Generator room not placed; forcing attachment.");
            if (openDoors.Count > 0)
            {
                OpenDoor forcedDoor = openDoors[0];
                GameObject forcedInstance = Instantiate(generatorRoomPrefab, forcedDoor.position, Quaternion.identity);
                PlacedRoom forcedRoom = new PlacedRoom(forcedInstance);

                if (!IsOverlapping(forcedRoom))
                {
                    placedRooms.Add(forcedRoom);
                    generatorPlaced = true;
                    openDoors.Remove(forcedDoor);
                    AddRoomDoorsToOpenList(forcedRoom);
                    Debug.LogWarning("Generator room forced at " + forcedDoor.position);
                }
                else
                {
                    Debug.LogError("Forced generator room placement overlapped. Check prefab alignment.");
                    Destroy(forcedInstance);
                }
            }
            else
            {
                Debug.LogError("No open doors available to force generator room placement.");
            }
        }
    }

    // Removes all Door GameObjects that were never used after the mapping process is complete.
    void RemoveUnusedDoors()
    {
        Debug.Log("Removing all unused doors...");
        foreach (PlacedRoom placed in placedRooms)
        {
            if (placed.roomInstance == null)
                continue;
            Door[] doors = placed.roomInstance.GetComponentsInChildren<Door>();
            foreach (Door door in doors)
            {
                if (!door.isUsed)
                    Destroy(door.gameObject);
            }
        }
    }

    // Adds all door connection points from a placed room into the openDoors list.
    // A door used for the connection is excluded.
    void AddRoomDoorsToOpenList(PlacedRoom placedRoom, Door usedDoor = null)
    {
        Door[] doors = placedRoom.roomInstance.GetComponentsInChildren<Door>();
        foreach (Door door in doors)
        {

            Vector3 doorWorldPos = door.transform.position;
            DoorDirection doorDir = GetClosestDoorDirection(door.transform.forward);
            OpenDoor openDoor = new OpenDoor(door, doorWorldPos, doorDir, placedRoom);
            if (!openDoors.Contains(openDoor) && !openDoor.Equals(usedDoor))
                openDoors.Add(openDoor);
        }
    }

    // Checks whether the candidate room's BoxCollider overlaps any already placed room's BoxCollider.

    bool IsOverlapping(PlacedRoom candidate)
    {
        if (candidate == null || candidate.roomInstance == null)
        {
            Debug.LogWarning("Candidate or candidate.roomInstance is null. Cannot check overlap.");
            return true;
        }

        BoxCollider candidateCollider = candidate.roomInstance.GetComponentInChildren<BoxCollider>();
        if (candidateCollider == null)
        {
            Debug.LogWarning("Candidate room is missing a BoxCollider (neither on root nor children).");
            return true;
        }

        Bounds candidateBounds = candidateCollider.bounds;
        foreach (PlacedRoom placed in placedRooms)
        {
            if (placed == null || placed.roomInstance == null)
                continue;
            BoxCollider placedCollider = placed.roomInstance.GetComponentInChildren<BoxCollider>();
            if (placedCollider == null)
                continue;
            if (candidateBounds.Intersects(placedCollider.bounds))
                return true;
        }
        return false;
    }

    bool IsOppositeDirection(DoorDirection d1, DoorDirection d2)
    {
        return (d1 == DoorDirection.North && d2 == DoorDirection.South) ||
               (d1 == DoorDirection.South && d2 == DoorDirection.North) ||
               (d1 == DoorDirection.East && d2 == DoorDirection.West) ||
               (d1 == DoorDirection.West && d2 == DoorDirection.East);
    }

    Vector3 GetDirectionVector(DoorDirection direction)
    {
        switch (direction)
        {
            case DoorDirection.North: return Vector3.forward;
            case DoorDirection.South: return Vector3.back;
            case DoorDirection.East: return Vector3.right;
            case DoorDirection.West: return Vector3.left;
            default: return Vector3.zero;
        }
    }

    DoorDirection GetClosestDoorDirection(Vector3 dir)
    {
        Vector3 n = dir.normalized;
        float dotN = Vector3.Dot(n, Vector3.forward);
        float dotS = Vector3.Dot(n, Vector3.back);
        float dotE = Vector3.Dot(n, Vector3.right);
        float dotW = Vector3.Dot(n, Vector3.left);
        float max = Mathf.Max(dotN, dotS, dotE, dotW);
        if (max == dotN) return DoorDirection.North;
        if (max == dotS) return DoorDirection.South;
        if (max == dotE) return DoorDirection.East;
        return DoorDirection.West;
    }

    public class PlacedRoom
    {
        public GameObject roomInstance;
        public PlacedRoom(GameObject instance)
        {
            roomInstance = instance;
        }
    }

    public class OpenDoor
    {
        public Door doorScript;         // Reference to the Door component.
        public Vector3 position;        // World position of the door.
        public DoorDirection doorDirection;  // Direction the door's facing.
        public PlacedRoom parentRoom;   // The room this door belongs to.

        public OpenDoor(Door door, Vector3 pos, DoorDirection dir, PlacedRoom parent)
        {
            doorScript = door;
            position = pos;
            doorDirection = dir;
            parentRoom = parent;
        }

        public override bool Equals(object obj)
        {
            if (obj is OpenDoor other)
                return doorScript == other.doorScript;
            return false;
        }
        public override int GetHashCode()
        {
            return doorScript != null ? doorScript.GetHashCode() : base.GetHashCode();
        }
    }
}
